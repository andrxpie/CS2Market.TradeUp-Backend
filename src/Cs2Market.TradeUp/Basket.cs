namespace Cs2Market.TradeUp;

public sealed record BasketOptions(int Depth = 3, int Size = 10);

public sealed record BasketLot(Skin Skin, Wear Wear, int Quantity, Cents UnitPrice)
{
    public Cents Total => UnitPrice * Quantity;
}

/// <summary>
/// A costed shopping list. <paramref name="StatTrak"/> travels with the basket because every
/// lot shares it and the inputs built from the basket need it.
/// </summary>
public sealed record Basket(
    IReadOnlyList<BasketLot> Lots,        // the shopping list, cheapest first
    Cents Cost,
    IReadOnlyList<BasketLot> Substitutes, // next cheapest, same collection, for delisted lots
    bool StatTrak);

/// <summary>docs/DOMAIN.md § Basket costing: never "cheapest skin × 10".</summary>
public static class BasketBuilder
{
    /// <summary>
    /// Single-collection basket. Fails with <see cref="ContractErrorCode.UnpricedInput"/> when the
    /// collection has fewer than <c>Size</c> priced lots at <c>Depth</c> copies each.
    /// </summary>
    public static Validated<Basket> Build(
        string collectionId, Rarity rarity, Wear wear, bool statTrak,
        SkinCatalog catalog, PriceSnapshot snapshot, BasketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return BuildSplit([(collectionId, options.Size)], rarity, wear, statTrak, catalog, snapshot, options);
    }

    /// <summary>Mixed-collection basket, e.g. [("alpha", 3), ("filler", 7)]. Counts must sum to Size.</summary>
    /// <exception cref="ArgumentException">Counts that are not positive, repeat a collection, or do not sum to Size.</exception>
    public static Validated<Basket> BuildSplit(
        IReadOnlyList<(string CollectionId, int Count)> split, Rarity rarity, Wear wear, bool statTrak,
        SkinCatalog catalog, PriceSnapshot snapshot, BasketOptions options)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        if (split.Any(static part => part.Count <= 0)
            || split.Select(static part => part.CollectionId).Distinct(StringComparer.Ordinal).Count() != split.Count
            || split.Sum(static part => part.Count) != options.Size)
        {
            throw new ArgumentException(
                $"A split names each collection once with a positive count, and the counts sum to {options.Size}.", nameof(split));
        }

        var parts = split
            .Select(part => (part.Count, Cheapest: snapshot.CheapestFirst(part.CollectionId, rarity, wear, statTrak, catalog)))
            .ToArray();

        for (var i = 0; i < parts.Length; i++)
        {
            if (LotsAvailable(parts[i].Cheapest.Count, options.Depth) < parts[i].Count)
            {
                return Validated<Basket>.Fail(
                    ContractErrorCode.UnpricedInput,
                    $"Collection '{split[i].CollectionId}' has {LotsAvailable(parts[i].Cheapest.Count, options.Depth)} priced "
                    + $"{rarity} {wear} lots at depth {options.Depth}; the basket needs {parts[i].Count}.");
            }
        }

        return Validated<Basket>.Ok(Assemble(parts, wear, statTrak, options.Depth));
    }

    /// <summary>Materializes a basket into ten inputs at the worst float of their wear band.</summary>
    public static IReadOnlyList<ContractInput> ToWorstCaseInputs(Basket basket)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var inputs = new List<ContractInput>();
        foreach (var lot in basket.Lots)
        {
            if (!WearBands.TryFeasibleRange(lot.Skin, lot.Wear, out _, out var worst))
            {
                throw new InvalidOperationException($"Skin '{lot.Skin.Id}' cannot exist in {lot.Wear}.");
            }

            for (var copy = 0; copy < lot.Quantity; copy++)
            {
                inputs.Add(new ContractInput(lot.Skin.Id, lot.Wear, worst, basket.StatTrak, lot.UnitPrice));
            }
        }

        return inputs.AsReadOnly();
    }

    /// <summary>
    /// Materializes a basket into ten inputs at a chosen average float, distributing evenly:
    /// every input takes the same float, except where its wear band or skin range forces it
    /// higher or lower, and the rest absorb the difference.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The lots' wear bands cannot average to <paramref name="averageFloat"/>.</exception>
    public static IReadOnlyList<ContractInput> ToInputs(Basket basket, double averageFloat)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var bounds = new List<(BasketLot Lot, double Min, double Max)>();
        foreach (var lot in basket.Lots)
        {
            if (!WearBands.TryFeasibleRange(lot.Skin, lot.Wear, out var min, out var max))
            {
                throw new InvalidOperationException($"Skin '{lot.Skin.Id}' cannot exist in {lot.Wear}.");
            }

            for (var copy = 0; copy < lot.Quantity; copy++)
            {
                bounds.Add((lot, min, max));
            }
        }

        if (bounds.Count == 0)
        {
            return [];
        }

        var target = averageFloat * bounds.Count;
        var lowest = bounds.Sum(static b => b.Min);
        var highest = bounds.Sum(static b => b.Max);
        const double Slack = 1e-12;

        if (double.IsNaN(averageFloat) || target < lowest - Slack || target > highest + Slack)
        {
            throw new ArgumentOutOfRangeException(
                nameof(averageFloat), averageFloat,
                $"These lots can only average between {lowest / bounds.Count} and {highest / bounds.Count}.");
        }

        // Water-filling: find the common level whose clamped values sum to the target.
        double low = bounds.Min(static b => b.Min), high = bounds.Max(static b => b.Max);
        for (var iteration = 0; iteration < 200 && low < high; iteration++)
        {
            var level = low + ((high - low) / 2);
            if (level <= low || level >= high)
            {
                break;
            }

            if (bounds.Sum(b => Math.Clamp(level, b.Min, b.Max)) < target)
            {
                low = level;
            }
            else
            {
                high = level;
            }
        }

        return bounds
            .Select(b => new ContractInput(b.Lot.Skin.Id, b.Lot.Wear, Math.Clamp(high, b.Min, b.Max), basket.StatTrak, b.Lot.UnitPrice))
            .ToArray()
            .AsReadOnly();
    }

    internal static void ValidateOptions(BasketOptions options)
    {
        if (options.Depth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Depth, "Depth is at least 1.");
        }

        if (options.Size < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Size, "Size is at least 1.");
        }
    }

    internal static int LotsAvailable(int pricedSkins, int depth) => (int)Math.Min((long)pricedSkins * depth, int.MaxValue);

    /// <summary>
    /// Builds the basket from price-ordered candidates. The caller has checked that every part
    /// has enough lots; the scanner calls this directly with its precomputed ladders.
    /// </summary>
    internal static Basket Assemble(
        IReadOnlyList<(int Count, IReadOnlyList<(Skin Skin, Cents Price)> Cheapest)> parts, Wear wear, bool statTrak, int depth)
    {
        var lots = new List<BasketLot>();
        var substitutes = new List<BasketLot>();
        var cost = Cents.Zero;

        foreach (var (count, cheapest) in parts)
        {
            var remaining = count;
            var next = 0;
            var partLots = 0;

            for (; next < cheapest.Count && remaining > 0; next++)
            {
                var quantity = Math.Min(depth, remaining);
                var lot = new BasketLot(cheapest[next].Skin, wear, quantity, cheapest[next].Price);
                lots.Add(lot);
                cost += lot.Total;
                remaining -= quantity;
                partLots++;
            }

            // One substitute per distinct lot: the next cheapest skins the list did not use.
            for (var taken = 0; next < cheapest.Count && taken < partLots; next++, taken++)
            {
                substitutes.Add(new BasketLot(cheapest[next].Skin, wear, depth, cheapest[next].Price));
            }
        }

        return new Basket(Ordered(lots), cost, Ordered(substitutes), statTrak);
    }

    private static BasketLot[] Ordered(List<BasketLot> lots) => lots
        .OrderBy(static lot => lot.UnitPrice.Value)
        .ThenBy(static lot => lot.Skin.Id, StringComparer.Ordinal)
        .ToArray();
}
