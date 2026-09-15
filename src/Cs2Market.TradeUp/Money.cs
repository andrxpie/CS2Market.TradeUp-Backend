using System.Globalization;

namespace Cs2Market.TradeUp;

/// <summary>
/// An observed price in whole cents: listing prices, lot totals, basket cost.
/// Never a <see cref="double"/>, and never substituted for a missing price.
/// </summary>
public readonly record struct Cents(long Value) : IComparable<Cents>
{
    public static readonly Cents Zero;

    public static Cents operator +(Cents a, Cents b) => new(checked(a.Value + b.Value));

    public static Cents operator -(Cents a, Cents b) => new(checked(a.Value - b.Value));

    public static Cents operator *(Cents a, int quantity) => new(checked(a.Value * quantity));

    public static bool operator <(Cents a, Cents b) => a.Value < b.Value;

    public static bool operator <=(Cents a, Cents b) => a.Value <= b.Value;

    public static bool operator >(Cents a, Cents b) => a.Value > b.Value;

    public static bool operator >=(Cents a, Cents b) => a.Value >= b.Value;

    /// <summary>Rounding happens only at display time.</summary>
    public ExactCents Scale(decimal factor) => new(Value * factor);

    public int CompareTo(Cents other) => Value.CompareTo(other.Value);

    /// <summary>"$12.34", or "-$12.34" for a negative amount.</summary>
    public override string ToString() => MoneyFormat.Dollars(Value);
}

/// <summary>
/// A computed amount of cents that need not be whole: expected value, profit, stress results.
/// </summary>
public readonly record struct ExactCents(decimal Value) : IComparable<ExactCents>
{
    public static readonly ExactCents Zero;

    public static implicit operator ExactCents(Cents c) => new(c.Value);

    public static ExactCents operator +(ExactCents a, ExactCents b) => new(a.Value + b.Value);

    public static ExactCents operator -(ExactCents a, ExactCents b) => new(a.Value - b.Value);

    public static ExactCents operator *(ExactCents a, decimal f) => new(a.Value * f);

    public static ExactCents operator /(ExactCents a, decimal d) => new(a.Value / d);

    public static bool operator <(ExactCents a, ExactCents b) => a.Value < b.Value;

    public static bool operator <=(ExactCents a, ExactCents b) => a.Value <= b.Value;

    public static bool operator >(ExactCents a, ExactCents b) => a.Value > b.Value;

    public static bool operator >=(ExactCents a, ExactCents b) => a.Value >= b.Value;

    public int CompareTo(ExactCents other) => Value.CompareTo(other.Value);

    /// <summary>Rounded to the nearest cent for display only: "$11.67".</summary>
    public override string ToString() => MoneyFormat.Dollars(Value);
}

internal static class MoneyFormat
{
    public static string Dollars(decimal cents)
    {
        var rounded = Math.Round(cents, 0, MidpointRounding.AwayFromZero);
        var sign = rounded < 0 ? "-" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}${Math.Abs(rounded) / 100m:0.00}");
    }
}
