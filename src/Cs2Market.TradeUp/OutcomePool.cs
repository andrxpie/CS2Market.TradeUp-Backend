namespace Cs2Market.TradeUp;

/// <summary>One possible output. <see cref="Price"/> is null when unpriced; it still takes tickets.</summary>
public sealed record OutcomeOdds(Skin Skin, string CollectionId, double Probability, Cents? Price);

/// <summary>The ticket pool and <c>P(X) = n_C / Σ_j (n_j × O_j)</c> from docs/DOMAIN.md § Probability.</summary>
public static class OutcomePool
{
    /// <summary><c>Σ_j (n_j × O_j)</c>.</summary>
    public static int TicketCount(Contract contract, SkinCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(catalog);

        return contract.InputsByCollection.Sum(pair => pair.Value * catalog.OutputCount(pair.Key, contract.OutputRarity));
    }

    /// <summary>
    /// Every output with its probability and its price at the wear the contract's average float
    /// lands it in. Ordered by probability descending, then skin id and collection id ascending.
    /// </summary>
    public static IReadOnlyList<OutcomeOdds> Build(Contract contract, SkinCatalog catalog, PriceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var model = new OutcomeModel(contract, catalog);
        return model.Odds(FloatMath.AverageInputFloat(contract), snapshot);
    }
}

/// <summary>
/// The ticket pool of one contract, shared by the pool, the float math and the evaluator so
/// every number about a contract comes from the same arithmetic.
/// </summary>
internal sealed class OutcomeModel
{
    public OutcomeModel(Contract contract, SkinCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(catalog);

        Contract = contract;

        var slots = new List<OutcomeSlot>();
        foreach (var (collectionId, inputs) in contract.InputsByCollection)
        {
            foreach (var output in catalog.InCollection(collectionId, contract.OutputRarity))
            {
                slots.Add(new OutcomeSlot(output, collectionId, inputs));
            }
        }

        Slots = slots;
        Tickets = slots.Sum(static slot => slot.Inputs);
        Outputs = slots.Select(static slot => slot.Skin).DistinctBy(static skin => skin.Id).ToArray();

        if (Tickets == 0)
        {
            throw new InvalidOperationException("The contract has no outputs in this catalog.");
        }
    }

    public Contract Contract { get; }

    /// <summary>One slot per (collection, output skin); a slot holds <c>n_C</c> tickets.</summary>
    public IReadOnlyList<OutcomeSlot> Slots { get; }

    public int Tickets { get; }

    /// <summary>Distinct output skins, in slot order.</summary>
    public IReadOnlyList<Skin> Outputs { get; }

    public static Wear OutputWear(double averageFloat, Skin output) =>
        WearBands.FromFloat(FloatMath.OutputFloat(averageFloat, output));

    public Cents? PriceAt(double averageFloat, Skin output, PriceSnapshot snapshot) =>
        snapshot.TryGetPrice(new MarketKey(output.Id, OutputWear(averageFloat, output), Contract.StatTrak), out var price)
            ? price
            : null;

    /// <summary>
    /// <c>Σ_C (n_C × Σ prices of C's outputs)</c> at the given average float. An unpriced output
    /// adds nothing, so the sum is a lower bound; so does <paramref name="unsellableSkinId"/>.
    /// </summary>
    public decimal WeightedPriceSum(double averageFloat, PriceSnapshot snapshot, string? unsellableSkinId = null)
    {
        decimal sum = 0;
        foreach (var slot in Slots)
        {
            if (!string.Equals(slot.Skin.Id, unsellableSkinId, StringComparison.Ordinal)
                && PriceAt(averageFloat, slot.Skin, snapshot) is { } price)
            {
                sum += slot.Inputs * price.Value;
            }
        }

        return sum;
    }

    public ExactCents Ev(decimal weightedPriceSum) => new(weightedPriceSum / Tickets);

    /// <summary>
    /// <c>EV × net − cost</c>, multiplying before the single division so a result that is a whole
    /// number of cents comes out exact (golden case G6).
    /// </summary>
    public ExactCents Profit(decimal weightedPriceSum, decimal netMultiplier, ExactCents cost) =>
        new ExactCents(weightedPriceSum * netMultiplier / Tickets) - cost;

    public IReadOnlyList<OutcomeOdds> Odds(double averageFloat, PriceSnapshot snapshot) => Slots
        .Select(slot => new OutcomeOdds(
            slot.Skin,
            slot.CollectionId,
            (double)slot.Inputs / Tickets,
            PriceAt(averageFloat, slot.Skin, snapshot)))
        .OrderByDescending(static odds => odds.Probability)
        .ThenBy(static odds => odds.Skin.Id, StringComparer.Ordinal)
        .ThenBy(static odds => odds.CollectionId, StringComparer.Ordinal)
        .ToArray();
}

internal readonly record struct OutcomeSlot(Skin Skin, string CollectionId, int Inputs);
