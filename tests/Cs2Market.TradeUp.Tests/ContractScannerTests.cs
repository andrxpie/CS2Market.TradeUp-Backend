using System.Diagnostics;
using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class ContractScannerTests
{
    private static readonly ScanCriteria FixtureCriteria = new(new Cents(3000), Top: 100);

    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();
    private readonly PriceSnapshot _snapshot = SyntheticSnapshot.Create();

    [Fact]
    public void Scan_SameSnapshot_ProducesIdenticalOrdering()
    {
        var (catalog, snapshot) = SyntheticMarket.Generate();
        var criteria = new ScanCriteria(new Cents(2500), Top: 50);

        var first = Scan(criteria, catalog, snapshot);
        var second = Scan(criteria, catalog, snapshot);

        Assert.NotEmpty(first.Top);
        Assert.Equal(Fingerprint(first), Fingerprint(second));
        Assert.Equal(first.CombinationsEvaluated, second.CombinationsEvaluated);
        Assert.Equal(first.CombinationsPruned, second.CombinationsPruned);
    }

    [Fact]
    public void Scan_SyntheticCatalog_Deterministic()
    {
        var first = Scan(FixtureCriteria, _catalog, _snapshot);
        var second = Scan(FixtureCriteria, _catalog, _snapshot);

        Assert.NotEmpty(first.Top);
        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void Scan_Cancelled_StopsPromptly()
    {
        var (catalog, snapshot) = SyntheticMarket.Generate();
        using var cancellation = new CancellationTokenSource();
        var stopwatch = new Stopwatch();
        var progress = new SynchronousProgress<ScanProgress>(_ =>
        {
            if (!cancellation.IsCancellationRequested)
            {
                cancellation.Cancel();
                stopwatch.Start();
            }
        });

        Assert.Throws<OperationCanceledException>(() =>
            ContractScanner.Scan(new ScanCriteria(new Cents(1_000_000)), catalog, snapshot, progress, cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(200), $"Took {stopwatch.Elapsed.TotalMilliseconds} ms to stop.");
    }

    [Fact]
    public void Scan_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ContractScanner.Scan(FixtureCriteria, _catalog, _snapshot, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Scan_TimeBudgetExhausted_ReturnsPartialWithTimedOut()
    {
        var (catalog, snapshot) = SyntheticMarket.Generate();

        var outcome = Scan(new ScanCriteria(new Cents(2500), TimeBudget: TimeSpan.Zero), catalog, snapshot);

        Assert.True(outcome.TimedOut);
        Assert.True(outcome.CombinationsEvaluated + outcome.CombinationsPruned < outcome.CombinationsTotal);
        Assert.Equal(snapshot.CapturedAt, outcome.CapturedAt);
    }

    [Fact]
    public void Scan_TimeBudgetExhaustedMidScan_KeepsWhatWasFound()
    {
        // The budget runs out while the first transitions are being scanned: a slow progress
        // consumer stands in for a slow machine, so the test does not depend on CPU speed.
        var (catalog, snapshot) = SyntheticMarket.Generate();
        var criteria = new ScanCriteria(new Cents(2500), Top: int.MaxValue, TimeBudget: TimeSpan.FromMilliseconds(50));
        var slowed = false;
        var progress = new SynchronousProgress<ScanProgress>(report =>
        {
            if (report.Evaluated > 0 && !slowed)
            {
                slowed = true;
                Thread.Sleep(150);
            }
        });

        var partial = Scan(criteria, catalog, snapshot, progress);
        var complete = Scan(criteria with { TimeBudget = null }, catalog, snapshot);

        Assert.True(partial.TimedOut);
        Assert.InRange(partial.CombinationsEvaluated + partial.CombinationsPruned, 1, partial.CombinationsTotal - 1);
        Assert.Subset(Fingerprint(complete).ToHashSet(), Fingerprint(partial).ToHashSet());
    }

    [Fact]
    public void Scan_GenerousTimeBudget_CompletesWithoutTimingOut()
    {
        var outcome = Scan(FixtureCriteria with { TimeBudget = TimeSpan.FromMinutes(5) }, _catalog, _snapshot);

        Assert.False(outcome.TimedOut);
        Assert.Equal(outcome.CombinationsTotal, outcome.CombinationsEvaluated + outcome.CombinationsPruned);
    }

    [Fact]
    public void Scan_UnorderedPairs_ProducesNoDuplicateContracts()
    {
        var outcome = Scan(FixtureCriteria with { Top = 1000 }, _catalog, _snapshot);

        Assert.NotEmpty(outcome.Top);
        Assert.All(outcome.Top, static candidate =>
        {
            if (candidate.PrimaryCount == Contract.InputCount)
            {
                Assert.Equal(candidate.PrimaryCollectionId, candidate.FillerCollectionId);
            }
            else
            {
                Assert.True(string.CompareOrdinal(candidate.PrimaryCollectionId, candidate.FillerCollectionId) < 0);
            }
        });

        var contracts = outcome.Top.Select(static candidate => string.Join(
            ',',
            candidate.Evaluation.Contract.InputsByCollection.Select(static pair => $"{pair.Key}={pair.Value}"))
            + $"@{candidate.Evaluation.Contract.InputRarity}/{candidate.Basket.Lots[0].Wear}").ToList();

        Assert.Equal(contracts.Count, contracts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Scan_EvUpperBound_NeverPrunesAProfitableCombination()
    {
        foreach (var (catalog, snapshot, criteria) in DifferentialCases())
        {
            var (withBound, boundFunnel) = ContractScanner.Run(criteria, catalog, snapshot, null, useEvUpperBound: true, TestContext.Current.CancellationToken);
            var (withoutBound, bruteFunnel) = ContractScanner.Run(criteria, catalog, snapshot, null, useEvUpperBound: false, TestContext.Current.CancellationToken);

            Assert.True(boundFunnel.PrunedByEvUpperBound > 0, "The fixture must exercise stage 3 for this test to mean anything.");
            Assert.Equal(0, bruteFunnel.PrunedByEvUpperBound);
            Assert.NotEmpty(withoutBound.Top);
            Assert.Equal(Fingerprint(withoutBound), Fingerprint(withBound));
            Assert.Equal(withoutBound.CombinationsTotal, withBound.CombinationsTotal);
        }
    }

    [Fact]
    public void Scan_MatchesABruteForceOracle()
    {
        // Every combination built through the public API, no pruning at all, ranked by the documented order.
        var criteria = FixtureCriteria with { Top = 1000 };
        var expected = new List<(decimal Profit, decimal Roi, string Key, Rarity Rarity, Wear Wear)>();
        var collections = _catalog.CollectionIds.ToArray();

        foreach (var wear in Enum.GetValues<Wear>())
        {
            for (var a = 0; a < collections.Length; a++)
            {
                AddIfProfitable([(collections[a], 10)], wear, expected, criteria);
                for (var b = a + 1; b < collections.Length; b++)
                {
                    for (var n = 1; n <= 9; n++)
                    {
                        AddIfProfitable([(collections[a], n), (collections[b], 10 - n)], wear, expected, criteria);
                    }
                }
            }
        }

        var ranked = expected
            .OrderByDescending(static row => row.Profit)
            .ThenByDescending(static row => row.Roi)
            .ThenBy(static row => row.Key, StringComparer.Ordinal)
            .ThenBy(static row => row.Rarity)
            .ThenBy(static row => row.Wear)
            .Select(static row => $"{row.Key}@{row.Rarity}/{row.Wear}:{row.Profit}")
            .ToList();

        var outcome = Scan(criteria, _catalog, _snapshot);

        Assert.NotEmpty(ranked);
        Assert.Equal(ranked, Fingerprint(outcome));
    }

    [Fact]
    public void Scan_ProgressPercent_IsMonotonicAndReaches100()
    {
        var reports = new List<ScanProgress>();
        var (catalog, snapshot) = SyntheticMarket.Generate(12);

        var outcome = Scan(
            new ScanCriteria(new Cents(2500)), catalog, snapshot, new SynchronousProgress<ScanProgress>(reports.Add));

        Assert.True(reports.Count > 2);
        Assert.Equal(0, reports[0].Percent);
        Assert.Equal(100, reports[^1].Percent);
        Assert.All(reports.Zip(reports.Skip(1)), static pair =>
        {
            Assert.True(pair.Second.Percent > pair.First.Percent);
            Assert.True(pair.Second.Evaluated >= pair.First.Evaluated);
        });
        Assert.All(reports, report => Assert.Equal(outcome.CombinationsTotal, report.Total));
        Assert.Equal(outcome.CombinationsEvaluated, reports[^1].Evaluated);
    }

    [Fact]
    public void Scan_SyntheticCatalog_CompletesUnder2Seconds()
    {
        var (catalog, snapshot) = SyntheticMarket.Generate(30);

        var outcome = Scan(new ScanCriteria(new Cents(2500)), catalog, snapshot);

        Assert.False(outcome.TimedOut);
        Assert.True(outcome.CombinationsTotal > 80_000, $"Expected the ~88k-combination space, got {outcome.CombinationsTotal}.");
        Assert.True(outcome.Elapsed < TimeSpan.FromSeconds(2), $"Scan took {outcome.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public void Scan_TotalIsComputedFromTheSearchSpace_AndEveryCombinationIsAccountedFor()
    {
        // Per wear — Mil-Spec: 11 collections hold inputs → C(11,2) × 9 + 11 = 506; Restricted: 10 → 415;
        // Classified: apex alone → 1. No Restricted collection has a Classified skin, so stage 1 takes them all.
        var outcome = Scan(FixtureCriteria, _catalog, _snapshot);

        Assert.Equal(5 * (506 + 415 + 1), outcome.CombinationsTotal);
        Assert.Equal(outcome.CombinationsTotal, outcome.CombinationsEvaluated + outcome.CombinationsPruned);
        Assert.False(outcome.TimedOut);
    }

    [Fact]
    public void Scan_OnlyProfitableContractsWithinBudget_RankedByProfitThenRoi()
    {
        var outcome = Scan(FixtureCriteria, _catalog, _snapshot);

        Assert.All(outcome.Top, candidate =>
        {
            Assert.True(candidate.Evaluation.Profit.Value > 0);
            Assert.True(candidate.Basket.Cost <= FixtureCriteria.Budget);
            Assert.Equal(candidate.Basket.Cost, candidate.Evaluation.BasketCost);
            Assert.Equal(_snapshot.CapturedAt, candidate.Evaluation.CapturedAt);
        });
        Assert.All(outcome.Top.Zip(outcome.Top.Skip(1)), static pair =>
            Assert.True(
                pair.First.Evaluation.Profit > pair.Second.Evaluation.Profit
                || (pair.First.Evaluation.Profit == pair.Second.Evaluation.Profit && pair.First.Evaluation.Roi >= pair.Second.Evaluation.Roi)));
    }

    [Fact]
    public void Scan_CandidateEvaluation_IsWhatTheEvaluatorReturnsForItsContract()
    {
        var outcome = Scan(FixtureCriteria, _catalog, _snapshot);

        Assert.All(outcome.Top, candidate =>
        {
            var again = ContractEvaluator.Evaluate(
                candidate.Evaluation.Contract, _catalog, _snapshot, new EvaluationOptions(FixtureCriteria.FeeRate, FixtureCriteria.Depth)).ValueOrThrow();

            Assert.Equal(again.Profit, candidate.Evaluation.Profit);
            Assert.Equal(again.Stress, candidate.Evaluation.Stress);
            Assert.Equal(again.BreakEvenFloat, candidate.Evaluation.BreakEvenFloat);
        });
    }

    [Fact]
    public void Scan_TopLimit_KeepsTheBestN()
    {
        var all = Scan(FixtureCriteria, _catalog, _snapshot);
        var three = Scan(FixtureCriteria with { Top = 3 }, _catalog, _snapshot);

        Assert.True(all.Top.Count > 3);
        Assert.Equal(Fingerprint(all).Take(3), Fingerprint(three));
    }

    [Fact]
    public void Scan_TinyBudget_FindsNothingAndSaysWhy()
    {
        var outcome = Scan(new ScanCriteria(new Cents(10)), _catalog, _snapshot);

        Assert.Empty(outcome.Top);
        Assert.Equal(0, outcome.CombinationsEvaluated);
        Assert.Equal(outcome.CombinationsTotal, outcome.CombinationsPruned);
    }

    [Fact]
    public void Scan_FiltersByRarityAndWear()
    {
        var outcome = Scan(
            FixtureCriteria with { InputRarity = Rarity.MilSpec, Wear = Wear.FactoryNew }, _catalog, _snapshot);

        Assert.Equal(506, outcome.CombinationsTotal);
        Assert.NotEmpty(outcome.Top);
        Assert.All(outcome.Top, static candidate =>
        {
            Assert.Equal(Rarity.MilSpec, candidate.Evaluation.Contract.InputRarity);
            Assert.All(candidate.Basket.Lots, static lot => Assert.Equal(Wear.FactoryNew, lot.Wear));
        });
    }

    [Fact]
    public void Scan_MinPrimaryCount_LimitsSplitsSymmetrically()
    {
        var outcome = Scan(FixtureCriteria with { MinPrimaryCount = 4, Wear = Wear.FieldTested }, _catalog, _snapshot);

        // n ∈ [4, 6]: three splits per pair. Mil-Spec: 55 × 3 + 11; Restricted: 45 × 3 + 10; Classified: 1.
        Assert.Equal((55 * 3) + 11 + (45 * 3) + 10 + 1, outcome.CombinationsTotal);
        Assert.All(outcome.Top.Where(static candidate => candidate.PrimaryCount < 10), static candidate =>
            Assert.InRange(candidate.PrimaryCount, 4, 6));
    }

    [Fact]
    public void Scan_ClassifiedTransition_CanProduceACovertContract()
    {
        var outcome = Scan(
            new ScanCriteria(new Cents(20_000), InputRarity: Rarity.Classified, Wear: Wear.FieldTested), _catalog, _snapshot);

        var candidate = Assert.Single(outcome.Top);
        Assert.Equal(("apex", "apex", 10), (candidate.PrimaryCollectionId, candidate.FillerCollectionId, candidate.PrimaryCount));
        Assert.Equal(Rarity.Covert, candidate.Evaluation.Contract.OutputRarity);
    }

    [Fact]
    public void Scan_EmptyCatalog_IsCompleteWithNothing()
    {
        var reports = new List<ScanProgress>();

        var outcome = Scan(
            FixtureCriteria, new SkinCatalog([]), _snapshot, new SynchronousProgress<ScanProgress>(reports.Add));

        Assert.Equal(0, outcome.CombinationsTotal);
        Assert.Empty(outcome.Top);
        Assert.False(outcome.TimedOut);
        Assert.Equal(100, Assert.Single(reports).Percent);
    }

    public static TheoryData<string> InvalidCriteria => ["negative budget", "covert", "wear", "fee", "depth", "top", "min primary", "time budget"];

    [Theory]
    [MemberData(nameof(InvalidCriteria))]
    public void Scan_InvalidCriteria_Throws(string field)
    {
        var criteria = field switch
        {
            "negative budget" => FixtureCriteria with { Budget = new Cents(-1) },
            "covert" => FixtureCriteria with { InputRarity = Rarity.Covert },
            "wear" => FixtureCriteria with { Wear = (Wear)7 },
            "fee" => FixtureCriteria with { FeeRate = 1.0 },
            "depth" => FixtureCriteria with { Depth = 0 },
            "top" => FixtureCriteria with { Top = 0 },
            "min primary" => FixtureCriteria with { MinPrimaryCount = 10 },
            _ => FixtureCriteria with { TimeBudget = TimeSpan.FromSeconds(-1) },
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => Scan(criteria, _catalog, _snapshot));
    }

    [Fact]
    public void Scan_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Scan(null!, _catalog, _snapshot));
        Assert.Throws<ArgumentNullException>(() => Scan(FixtureCriteria, null!, _snapshot));
        Assert.Throws<ArgumentNullException>(() => Scan(FixtureCriteria, _catalog, null!));
    }

    private static IEnumerable<(SkinCatalog Catalog, PriceSnapshot Snapshot, ScanCriteria Criteria)> DifferentialCases()
    {
        yield return (SyntheticCatalog.Create(), SyntheticSnapshot.Create(), FixtureCriteria);

        var (catalog, snapshot) = SyntheticMarket.Generate();
        yield return (catalog, snapshot, new ScanCriteria(new Cents(2500), Top: 1000));
        yield return (catalog, snapshot, new ScanCriteria(new Cents(100_000), Top: 1000, FeeRate: 0.15, Depth: 2));
    }

    private static ScanOutcome Scan(
        ScanCriteria criteria, SkinCatalog catalog, PriceSnapshot snapshot, IProgress<ScanProgress>? progress = null) =>
        ContractScanner.Scan(criteria, catalog, snapshot, progress, TestContext.Current.CancellationToken);

    private static List<string> Fingerprint(ScanOutcome outcome) => outcome.Top
        .Select(static candidate =>
            $"{candidate.PrimaryCollectionId}/{candidate.FillerCollectionId}/{candidate.PrimaryCount}"
            + $"@{candidate.Evaluation.Contract.InputRarity}/{candidate.Basket.Lots[0].Wear}:{candidate.Evaluation.Profit.Value}")
        .ToList();

    private void AddIfProfitable(
        (string CollectionId, int Count)[] split, Wear wear,
        List<(decimal Profit, decimal Roi, string Key, Rarity Rarity, Wear Wear)> into, ScanCriteria criteria)
    {
        foreach (var rarity in new[] { Rarity.Consumer, Rarity.Industrial, Rarity.MilSpec, Rarity.Restricted, Rarity.Classified })
        {
            var basket = BasketBuilder.BuildSplit(split, rarity, wear, false, _catalog, _snapshot, new BasketOptions(criteria.Depth));
            if (!basket.IsValid || basket.Value.Cost > criteria.Budget)
            {
                continue;
            }

            var (lowest, highest) = FloatMath.AchievableAverage(
                basket.Value.Lots.SelectMany(static lot => Enumerable.Repeat((lot.Skin, lot.Wear), lot.Quantity)));
            var contract = Contract.Create(BasketBuilder.ToInputs(basket.Value, lowest + ((highest - lowest) / 2)), _catalog);
            if (!contract.IsValid)
            {
                continue;
            }

            var evaluation = ContractEvaluator.Evaluate(
                contract.Value, _catalog, _snapshot, new EvaluationOptions(criteria.FeeRate, criteria.Depth)).ValueOrThrow();
            if (evaluation.Profit.Value > 0)
            {
                var filler = split.Length > 1 ? split[1].CollectionId : split[0].CollectionId;
                into.Add((evaluation.Profit.Value, evaluation.Roi, $"{split[0].CollectionId}/{filler}/{split[0].Count}", rarity, wear));
            }
        }
    }

    /// <summary><see cref="Progress{T}"/> posts asynchronously; the tests need the report inline.</summary>
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
