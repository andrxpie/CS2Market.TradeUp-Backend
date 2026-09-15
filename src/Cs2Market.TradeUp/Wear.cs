namespace Cs2Market.TradeUp;

public enum Wear
{
    FactoryNew,
    MinimalWear,
    FieldTested,
    WellWorn,
    BattleScarred,
}

/// <summary>
/// Half-open wear bands: <c>FN &lt; 0.07 ≤ MW &lt; 0.15 ≤ FT &lt; 0.38 ≤ WW &lt; 0.45 ≤ BS</c>.
/// Battle-Scarred is the one closed band: it includes 1.0.
/// </summary>
public static class WearBands
{
    public const double FactoryNewMax = 0.07;   // exclusive upper bound
    public const double MinimalWearMax = 0.15;
    public const double FieldTestedMax = 0.38;
    public const double WellWornMax = 0.45;

    /// <summary>Throws when <paramref name="value"/> is NaN or outside [0, 1].</summary>
    public static Wear FromFloat(double value)
    {
        if (!(value is >= 0.0 and <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A float value lies in [0, 1].");
        }

        return value switch
        {
            < FactoryNewMax => Wear.FactoryNew,
            < MinimalWearMax => Wear.MinimalWear,
            < FieldTestedMax => Wear.FieldTested,
            < WellWornMax => Wear.WellWorn,
            _ => Wear.BattleScarred,
        };
    }

    /// <summary>
    /// The band as <c>[Min, MaxExclusive)</c>. For Battle-Scarred the upper bound is 1.0 and,
    /// unlike the other bands, 1.0 itself belongs to it.
    /// </summary>
    public static (double Min, double MaxExclusive) Range(Wear wear) => wear switch
    {
        Wear.FactoryNew => (0.0, FactoryNewMax),
        Wear.MinimalWear => (FactoryNewMax, MinimalWearMax),
        Wear.FieldTested => (MinimalWearMax, FieldTestedMax),
        Wear.WellWorn => (FieldTestedMax, WellWornMax),
        Wear.BattleScarred => (WellWornMax, 1.0),
        _ => throw new ArgumentOutOfRangeException(nameof(wear), wear, "Unknown wear."),
    };

    /// <summary>The Steam market suffix without parentheses: "Factory New", "Field-Tested".</summary>
    public static string MarketSuffix(this Wear wear) => wear switch
    {
        Wear.FactoryNew => "Factory New",
        Wear.MinimalWear => "Minimal Wear",
        Wear.FieldTested => "Field-Tested",
        Wear.WellWorn => "Well-Worn",
        Wear.BattleScarred => "Battle-Scarred",
        _ => throw new ArgumentOutOfRangeException(nameof(wear), wear, "Unknown wear."),
    };

    /// <summary>
    /// Reads a market suffix, bare ("Field-Tested") or as it appears in a market hash name
    /// ("(Field-Tested)"). Matching is ordinal and exact: no trimming, no case folding.
    /// </summary>
    public static bool TryParseMarketSuffix(ReadOnlySpan<char> text, out Wear wear)
    {
        if (text.Length >= 2 && text[0] == '(' && text[^1] == ')')
        {
            text = text[1..^1];
        }

        foreach (var candidate in Enum.GetValues<Wear>())
        {
            if (text.SequenceEqual(candidate.MarketSuffix()))
            {
                wear = candidate;
                return true;
            }
        }

        wear = default;
        return false;
    }

    /// <summary>
    /// The float values a skin can physically have in <paramref name="wear"/>: the band
    /// intersected with the skin's own range, both ends inclusive. False when they do not meet.
    /// </summary>
    internal static bool TryFeasibleRange(Skin skin, Wear wear, out double min, out double max)
    {
        var (bandMin, bandMaxExclusive) = Range(wear);
        var bandMax = wear == Wear.BattleScarred ? 1.0 : Math.BitDecrement(bandMaxExclusive);

        min = Math.Max(bandMin, skin.MinFloat);
        max = Math.Min(bandMax, skin.MaxFloat);
        return min <= max;
    }
}
