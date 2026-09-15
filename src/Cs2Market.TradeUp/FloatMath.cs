namespace Cs2Market.TradeUp;

/// <summary>docs/DOMAIN.md § Float.</summary>
public static class FloatMath
{
    private static readonly Wear[] AllWears = Enum.GetValues<Wear>();

    /// <summary>The mean of the raw input floats: only the sum of ten matters.</summary>
    public static double AverageInputFloat(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return contract.Inputs.Sum(static input => input.Float) / contract.Inputs.Count;
    }

    /// <summary>
    /// <c>avg × (max_float − min_float) + min_float</c>, clamped into the skin's own range so a
    /// rounding error at the top end cannot leave it.
    /// </summary>
    public static double OutputFloat(double averageInputFloat, Skin output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (!(averageInputFloat is >= 0.0 and <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(averageInputFloat), averageInputFloat, "An average float lies in [0, 1].");
        }

        return Math.Clamp((averageInputFloat * output.FloatRange) + output.MinFloat, output.MinFloat, output.MaxFloat);
    }

    /// <summary>
    /// Highest average input float that still lands the output inside <paramref name="targetWear"/>.
    /// General form: <c>(bandMax − minFloat) / (maxFloat − minFloat)</c>, clamped to [0, 1]. The bound
    /// is exclusive, as the band is: an average equal to it lands one band worse.
    /// </summary>
    public static double ThresholdAverage(Skin output, Wear targetWear)
    {
        ArgumentNullException.ThrowIfNull(output);

        var bandMax = WearBands.Range(targetWear).MaxExclusive;

        if (output.FloatRange <= 0.0)
        {
            // A fixed-float skin lands in the same band for every average.
            return output.MinFloat < bandMax ? 1.0 : 0.0;
        }

        return Math.Clamp((bandMax - output.MinFloat) / output.FloatRange, 0.0, 1.0);
    }

    /// <summary>
    /// Average input float above which profit stops being positive.
    /// Profit is piecewise constant in the average float: it only changes where some output
    /// crosses a wear boundary. Evaluate profit at each boundary, return the last average
    /// with positive profit. Returns 1.0 when the contract is profitable across the range,
    /// and 0.0 when it is never profitable.
    /// </summary>
    public static double BreakEvenFloat(
        Contract contract, SkinCatalog catalog, PriceSnapshot snapshot, EvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        return BreakEvenFloat(new OutcomeModel(contract, catalog), snapshot, options.NetMultiplier);
    }

    internal static double BreakEvenFloat(OutcomeModel model, PriceSnapshot snapshot, decimal netMultiplier)
    {
        var breakpoints = new SortedSet<double> { 0.0, 1.0 };
        foreach (var output in model.Outputs)
        {
            foreach (var wear in AllWears)
            {
                breakpoints.Add(ThresholdAverage(output, wear));
            }
        }

        var points = breakpoints.ToArray();
        var breakEven = 0.0;

        for (var i = 0; i + 1 < points.Length; i++)
        {
            // Profit is constant inside the interval; its midpoint is clear of both boundaries.
            var midpoint = points[i] + ((points[i + 1] - points[i]) / 2);
            var sum = model.WeightedPriceSum(midpoint, snapshot);

            if (model.Profit(sum, netMultiplier, model.Contract.Cost).Value > 0)
            {
                breakEven = points[i + 1];
            }
        }

        return breakEven;
    }

    /// <summary>
    /// The average that maximises value: the most valuable output, priced at the best wear the
    /// contract's input bands can reach for it, and the highest average that keeps it there,
    /// capped by the worst average the input bands allow. When no output is priced at its best
    /// reachable wear, the float does not change what can be seen and the cap is returned.
    /// </summary>
    internal static double TargetAverageFloat(OutcomeModel model, SkinCatalog catalog, PriceSnapshot snapshot)
    {
        var (lowest, highest) = AchievableAverage(model.Contract.Inputs.Select(input => (catalog.Get(input.SkinId), input.Wear)));

        Skin? best = null;
        var bestPrice = Cents.Zero;
        var bestThreshold = highest;

        foreach (var output in model.Outputs)
        {
            foreach (var wear in AllWears)
            {
                if (!WearBands.TryFeasibleRange(output, wear, out _, out _))
                {
                    continue;
                }

                var threshold = ThresholdAverage(output, wear);
                if (threshold <= lowest && wear != Wear.BattleScarred)
                {
                    continue;   // no achievable average keeps this output in so good a band
                }

                if (snapshot.TryGetPrice(new MarketKey(output.Id, wear, model.Contract.StatTrak), out var price)
                    && (best is null || price > bestPrice
                        || (price == bestPrice && string.CompareOrdinal(output.Id, best.Id) < 0)))
                {
                    best = output;
                    bestPrice = price;
                    bestThreshold = threshold;
                }

                break;  // the best reachable wear for this output, priced or not
            }
        }

        return Math.Min(bestThreshold, highest);
    }

    /// <summary>
    /// The lowest and highest average the inputs can have while each stays inside its wear band
    /// and its skin's float range.
    /// </summary>
    internal static (double Lowest, double Highest) AchievableAverage(IEnumerable<(Skin Skin, Wear Wear)> inputs)
    {
        double lowest = 0, highest = 0;
        var count = 0;

        foreach (var (skin, wear) in inputs)
        {
            if (!WearBands.TryFeasibleRange(skin, wear, out var min, out var max))
            {
                throw new InvalidOperationException($"Skin '{skin.Id}' cannot exist in {wear}.");
            }

            lowest += min;
            highest += max;
            count++;
        }

        return count == 0 ? (0, 0) : (lowest / count, highest / count);
    }
}
