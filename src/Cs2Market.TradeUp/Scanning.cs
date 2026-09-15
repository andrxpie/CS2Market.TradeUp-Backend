using System.Diagnostics;

namespace Cs2Market.TradeUp;

/// <summary>
/// What to scan. Two-collection splits give each collection between
/// <paramref name="MinPrimaryCount"/> and <c>10 − MinPrimaryCount</c> inputs; single-collection
/// contracts are always included. Only normal (non-StatTrak) contracts are scanned.
/// </summary>
public sealed record ScanCriteria(
    Cents Budget,
    Rarity? InputRarity = null,      // null scans every tradable transition
    Wear? Wear = null,               // null scans every wear
    double FeeRate = 0.13,
    int Depth = 3,
    int Top = 20,
    int MinPrimaryCount = 1,         // splits from MinPrimaryCount/10 to 9/1
    TimeSpan? TimeBudget = null);    // null = run to completion

/// <summary><c>Percent = (evaluated + pruned) × 100 / total</c>; <c>Evaluated</c> counts stage-4 evaluations only.</summary>
public readonly record struct ScanProgress(int Percent, int Evaluated, int Total);

/// <summary>
/// A profitable contract found by the scanner. A single-collection contract has
/// <c>FillerCollectionId == PrimaryCollectionId</c> and <c>PrimaryCount == 10</c>.
/// </summary>
public sealed record ScanCandidate(
    ContractEvaluation Evaluation,
    Basket Basket,                   // shopping list and substitutes, required by the UI
    string PrimaryCollectionId,
    string FillerCollectionId,
    int PrimaryCount);

public sealed record ScanOutcome(
    DateTimeOffset CapturedAt,
    IReadOnlyList<ScanCandidate> Top,
    int CombinationsTotal,
    int CombinationsEvaluated,
    int CombinationsPruned,
    bool TimedOut,
    TimeSpan Elapsed);

/// <summary>
/// Per-stage counts of one scan (docs/M4_SCANNING.md § Pruning ladder). Internal until M4-3
/// needs them for the funnel log line.
/// </summary>
internal sealed record ScanFunnel(int PrunedByCollectionCap, int PrunedByBudget, int PrunedByEvUpperBound, int Evaluated);

/// <summary>
/// Budget-constrained search over rarity transitions × wears × unordered collection pairs ×
/// splits. Pure, synchronous, single-threaded and cancellable; see docs/M4_SCANNING.md.
/// </summary>
public static class ContractScanner
{
    /// <summary>
    /// Returns only profitable contracts, ranked by profit descending, then ROI descending, then
    /// <c>"{Primary}/{Filler}/{PrimaryCount}"</c> ordinal ascending, then input rarity and wear.
    /// An exhausted <see cref="ScanCriteria.TimeBudget"/> returns what was found with
    /// <c>TimedOut = true</c>; a cancelled token throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static ScanOutcome Scan(
        ScanCriteria criteria, SkinCatalog catalog, PriceSnapshot snapshot,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Run(criteria, catalog, snapshot, progress, useEvUpperBound: true, cancellationToken).Outcome;

    /// <summary>
    /// The scan with the EV upper bound (stage 3) switchable, so a test can prove the bound never
    /// changes the answer.
    /// </summary>
    internal static (ScanOutcome Outcome, ScanFunnel Funnel) Run(
        ScanCriteria criteria, SkinCatalog catalog, PriceSnapshot snapshot,
        IProgress<ScanProgress>? progress, bool useEvUpperBound, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(criteria);

        return new ScanRun(criteria, catalog, snapshot, progress, useEvUpperBound, cancellationToken).Execute();
    }

    private static void Validate(ScanCriteria criteria)
    {
        if (criteria.Budget.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(criteria), criteria.Budget, "The budget is not negative.");
        }

        if (criteria.InputRarity is { } rarity && (!Enum.IsDefined(rarity) || !rarity.CanBeContractInput()))
        {
            throw new ArgumentOutOfRangeException(nameof(criteria), rarity, "The input rarity must be tradable.");
        }

        if (criteria.Wear is { } wear && !Enum.IsDefined(wear))
        {
            throw new ArgumentOutOfRangeException(nameof(criteria), wear, "Unknown wear.");
        }

        if (!(criteria.FeeRate is >= 0.0 and < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(criteria), criteria.FeeRate, "The fee rate lies in [0, 1).");
        }

        if (criteria.Depth < 1 || criteria.Top < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(criteria), "Depth and Top are at least 1.");
        }

        if (criteria.MinPrimaryCount is < 1 or >= Contract.InputCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria), criteria.MinPrimaryCount, $"MinPrimaryCount lies in [1, {Contract.InputCount - 1}].");
        }

        if (criteria.TimeBudget is { } budget && budget < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(criteria), budget, "The time budget is not negative.");
        }
    }

    private sealed class ScanRun(
        ScanCriteria criteria, SkinCatalog catalog, PriceSnapshot snapshot,
        IProgress<ScanProgress>? progress, bool useEvUpperBound, CancellationToken cancellationToken)
    {
        private const int Size = Contract.InputCount;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly decimal _net = 1m - (decimal)criteria.FeeRate;
        private readonly EvaluationOptions _options = new(criteria.FeeRate, criteria.Depth);
        private readonly PriorityQueue<ScanCandidate, ScanCandidate> _top = new(WorstFirst.Instance);
        private readonly string[] _collections = catalog.CollectionIds.Order(StringComparer.Ordinal).ToArray();
        private readonly int[] _splitCounts = Enumerable
            .Range(criteria.MinPrimaryCount, Math.Max(0, Size - (2 * criteria.MinPrimaryCount) + 1))
            .ToArray();

        private int _total;
        private int _evaluated;
        private int _prunedByCollectionCap;
        private int _prunedByBudget;
        private int _prunedByEvUpperBound;
        private int _lastPercent = -1;
        private bool _timedOut;

        public (ScanOutcome Outcome, ScanFunnel Funnel) Execute()
        {
            Rarity[] rarities = criteria.InputRarity is { } only
                ? [only]
                : Enum.GetValues<Rarity>().Where(static rarity => rarity.CanBeContractInput()).ToArray();
            Wear[] wears = criteria.Wear is { } onlyWear ? [onlyWear] : Enum.GetValues<Wear>();

            // Computed before the loop so progress percentages are honest.
            _total = checked((int)rarities.Sum(rarity =>
            {
                long k = _collections.Count(id => catalog.InputsInCollection(id, rarity).Count > 0);
                return wears.Length * ((k * (k - 1) / 2 * _splitCounts.Length) + k);
            }));

            Report();

            foreach (var rarity in rarities)
            {
                foreach (var wear in wears)
                {
                    if (ShouldStop() || !ScanTransition(rarity, wear))
                    {
                        break;
                    }
                }

                if (_timedOut)
                {
                    break;
                }
            }

            if (!_timedOut)
            {
                Report();
            }

            var ranked = _top.UnorderedItems.Select(static item => item.Element).ToList();
            ranked.Sort(static (a, b) => WorstFirst.Instance.Compare(b, a));

            var pruned = _prunedByCollectionCap + _prunedByBudget + _prunedByEvUpperBound;
            var outcome = new ScanOutcome(
                snapshot.CapturedAt, ranked.AsReadOnly(), _total, _evaluated, pruned, _timedOut, _stopwatch.Elapsed);

            return (outcome, new ScanFunnel(_prunedByCollectionCap, _prunedByBudget, _prunedByEvUpperBound, _evaluated));
        }

        /// <summary>One rarity transition at one wear. False when the scan must stop.</summary>
        private bool ScanTransition(Rarity rarity, Wear wear)
        {
            var target = rarity + 1;
            var members = _collections.Where(id => catalog.InputsInCollection(id, rarity).Count > 0).ToArray();
            var count = members.Length;

            // Stage 0: the price ladder. ladder[c][n] is the cost of the n cheapest lots under the depth cap.
            var cheapest = new IReadOnlyList<(Skin Skin, Cents Price)>[count];
            var ladder = new long[count][];
            var outputs = new int[count];
            var outputSums = new decimal[count];

            for (var c = 0; c < count; c++)
            {
                cheapest[c] = snapshot.CheapestFirst(members[c], rarity, wear, statTrak: false, catalog);
                ladder[c] = Ladder(cheapest[c]);
                outputs[c] = catalog.OutputCount(members[c], target);

                // S_C with each output at its highest price across wears keeps the bound admissible
                // wherever the float lands.
                outputSums[c] = catalog.InCollection(members[c], target)
                    .Sum(skin => snapshot.HighestPrice(skin.Id, statTrak: false)?.Value ?? 0L);
            }

            // Stage 1: the collection cap. A collection stays only if some contract using it has
            // outputs, enough lots and fits the budget; everything else about it is pruned here.
            var alive = CollectionCap(ladder, outputs);

            for (var a = 0; a < count; a++)
            {
                if (ShouldStop())
                {
                    return false;
                }

                if (!alive[a] || ladder[a].Length <= Size)
                {
                    _prunedByCollectionCap++;
                }
                else if (!Consider([(a, Size)], ladder, outputs, outputSums, members, cheapest, wear))
                {
                    return false;
                }

                for (var b = a + 1; b < count; b++)
                {
                    if (ShouldStop())
                    {
                        return false;
                    }

                    if (!alive[a] || !alive[b])
                    {
                        _prunedByCollectionCap += _splitCounts.Length;
                        Report();
                        continue;
                    }

                    foreach (var n in _splitCounts)
                    {
                        if (ladder[a].Length <= n || ladder[b].Length <= Size - n)
                        {
                            _prunedByCollectionCap++;
                        }
                        else if (!Consider([(a, n), (b, Size - n)], ladder, outputs, outputSums, members, cheapest, wear))
                        {
                            return false;
                        }
                    }

                    Report();
                }
            }

            return true;
        }

        /// <summary>Stages 2, 3 and 4 for one combination whose lots exist. False when the scan must stop.</summary>
        private bool Consider(
            (int Collection, int Count)[] parts, long[][] ladder, int[] outputs, decimal[] outputSums,
            string[] members, IReadOnlyList<(Skin Skin, Cents Price)>[] cheapest, Wear wear)
        {
            var cost = parts.Sum(part => ladder[part.Collection][part.Count]);

            if (cost > criteria.Budget.Value)
            {
                _prunedByBudget++;
                return true;
            }

            // Stage 3: EV is a weighted mean of m_C = S_C / O_C, so EV ≤ max m_C. When every
            // m_C × net ≤ cost, no float makes this contract profitable. Cross-multiplied, exact.
            if (useEvUpperBound && parts.All(part => outputSums[part.Collection] * _net <= cost * outputs[part.Collection]))
            {
                _prunedByEvUpperBound++;
                return true;
            }

            if (ShouldStop())
            {
                return false;
            }

            _evaluated++;
            Evaluate(parts, members, cheapest, wear);
            Report();
            return true;
        }

        /// <summary>Stage 4: basket, contract, full evaluation; keeps it when profitable.</summary>
        private void Evaluate((int Collection, int Count)[] parts, string[] members, IReadOnlyList<(Skin Skin, Cents Price)>[] cheapest, Wear wear)
        {
            var basket = BasketBuilder.Assemble(
                parts.Select(part => (part.Count, cheapest[part.Collection])).ToArray(), wear, statTrak: false, criteria.Depth);

            // A typical lot: the middle of what the lots' wear bands allow. Worst case is a stress test.
            var (lowest, highest) = FloatMath.AchievableAverage(
                basket.Lots.SelectMany(static lot => Enumerable.Repeat((lot.Skin, lot.Wear), lot.Quantity)));
            var inputs = BasketBuilder.ToInputs(basket, lowest + ((highest - lowest) / 2));

            // Stages 0–3 only let through lots that exist, are priced, fit their band and have
            // outputs, so a rejection here is a scanner bug, not a combination to skip quietly.
            var contract = Contract.Create(inputs, catalog).ValueOrThrow();
            var evaluation = ContractEvaluator.Evaluate(contract, catalog, snapshot, _options).ValueOrThrow();
            if (evaluation.Profit.Value <= 0)
            {
                return;
            }

            var primary = members[parts[0].Collection];
            var filler = parts.Length > 1 ? members[parts[1].Collection] : primary;
            var candidate = new ScanCandidate(evaluation, basket, primary, filler, parts[0].Count);

            if (_top.Count < criteria.Top)
            {
                _top.Enqueue(candidate, candidate);
            }
            else if (WorstFirst.Instance.Compare(candidate, _top.Peek()) > 0)
            {
                _top.DequeueEnqueue(candidate, candidate);
            }
        }

        /// <summary>
        /// Exact: a collection is dropped only when every combination containing it would be
        /// rejected anyway — no outputs, too few lots, or over budget with any partner.
        /// </summary>
        private bool[] CollectionCap(long[][] ladder, int[] outputs)
        {
            var count = ladder.Length;

            // For each lot count k, the two cheapest collections that can supply k lots.
            var best = new (long Cost, int Collection)[Size + 1];
            var second = new (long Cost, int Collection)[Size + 1];
            Array.Fill(best, (long.MaxValue, -1));
            Array.Fill(second, (long.MaxValue, -1));

            for (var c = 0; c < count; c++)
            {
                if (outputs[c] == 0)
                {
                    continue;
                }

                for (var k = 1; k < ladder[c].Length && k <= Size; k++)
                {
                    if (ladder[c][k] < best[k].Cost)
                    {
                        second[k] = best[k];
                        best[k] = (ladder[c][k], c);
                    }
                    else if (ladder[c][k] < second[k].Cost)
                    {
                        second[k] = (ladder[c][k], c);
                    }
                }
            }

            var alive = new bool[count];
            for (var c = 0; c < count; c++)
            {
                if (outputs[c] == 0)
                {
                    continue;
                }

                alive[c] = ladder[c].Length > Size && ladder[c][Size] <= criteria.Budget.Value;

                foreach (var n in _splitCounts)
                {
                    if (alive[c] || ladder[c].Length <= n)
                    {
                        continue;
                    }

                    var partner = best[Size - n].Collection == c ? second[Size - n] : best[Size - n];
                    alive[c] = partner.Collection >= 0 && ladder[c][n] + partner.Cost <= criteria.Budget.Value;
                }
            }

            return alive;
        }

        private long[] Ladder(IReadOnlyList<(Skin Skin, Cents Price)> cheapest)
        {
            var steps = new List<long> { 0 };

            foreach (var (_, price) in cheapest)
            {
                for (var copy = 0; copy < criteria.Depth && steps.Count <= Size; copy++)
                {
                    steps.Add(steps[^1] + price.Value);
                }
            }

            return steps.ToArray();
        }

        private bool ShouldStop()
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_timedOut && criteria.TimeBudget is { } budget && _stopwatch.Elapsed >= budget)
            {
                _timedOut = true;
            }

            return _timedOut;
        }

        private void Report()
        {
            var processed = (long)_evaluated + _prunedByCollectionCap + _prunedByBudget + _prunedByEvUpperBound;
            var percent = _total == 0 ? 100 : (int)(processed * 100 / _total);

            if (percent > _lastPercent)
            {
                _lastPercent = percent;
                progress?.Report(new ScanProgress(percent, _evaluated, _total));
            }
        }
    }

    /// <summary>Orders the worse-ranked candidate first, so the min-heap evicts it.</summary>
    private sealed class WorstFirst : IComparer<ScanCandidate>
    {
        public static readonly WorstFirst Instance = new();

        public int Compare(ScanCandidate? x, ScanCandidate? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            var byProfit = x.Evaluation.Profit.CompareTo(y.Evaluation.Profit);
            if (byProfit != 0)
            {
                return byProfit;
            }

            var byRoi = x.Evaluation.Roi.CompareTo(y.Evaluation.Roi);
            if (byRoi != 0)
            {
                return byRoi;
            }

            // Ascending key ranks better, so the larger key is the worse candidate.
            var byKey = string.CompareOrdinal(Key(y), Key(x));
            if (byKey != 0)
            {
                return byKey;
            }

            var byRarity = y.Evaluation.Contract.InputRarity.CompareTo(x.Evaluation.Contract.InputRarity);
            return byRarity != 0 ? byRarity : y.Basket.Lots[0].Wear.CompareTo(x.Basket.Lots[0].Wear);
        }

        private static string Key(ScanCandidate candidate) =>
            $"{candidate.PrimaryCollectionId}/{candidate.FillerCollectionId}/{candidate.PrimaryCount}";
    }
}
