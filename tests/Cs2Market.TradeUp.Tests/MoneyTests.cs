namespace Cs2Market.TradeUp.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Cents_Arithmetic_StaysInWholeCents()
    {
        var a = new Cents(1250);
        var b = new Cents(99);

        Assert.Equal(new Cents(1349), a + b);
        Assert.Equal(new Cents(1151), a - b);
        Assert.Equal(new Cents(3750), a * 3);
        Assert.Equal(Cents.Zero, new Cents(0));
    }

    [Fact]
    public void Cents_Scale_ReturnsExactCentsWithoutRounding()
    {
        Assert.Equal(new ExactCents(1350m), new Cents(900).Scale(1.5m));
        Assert.Equal(new ExactCents(0.87m), new Cents(1).Scale(0.87m));
    }

    [Fact]
    public void Cents_Overflow_Throws() =>
        Assert.Throws<OverflowException>(() => new Cents(long.MaxValue) + new Cents(1));

    [Fact]
    public void Cents_Comparison_OrdersByValue()
    {
        var cheap = new Cents(100);
        var dear = new Cents(200);

        Assert.True(cheap < dear);
        Assert.True(cheap <= dear);
        Assert.True(dear > cheap);
        Assert.True(dear >= cheap);
        Assert.True(cheap.CompareTo(dear) < 0);
        Assert.Equal(0, cheap.CompareTo(new Cents(100)));
    }

    [Theory]
    [InlineData(1234L, "$12.34")]
    [InlineData(5L, "$0.05")]
    [InlineData(0L, "$0.00")]
    [InlineData(-250L, "-$2.50")]
    [InlineData(100_000L, "$1000.00")]
    public void Cents_ToString_FormatsDollars(long cents, string expected) =>
        Assert.Equal(expected, new Cents(cents).ToString());

    [Fact]
    public void ExactCents_Arithmetic_IsDecimal()
    {
        ExactCents fromCents = new Cents(17500);

        Assert.Equal(17500m / 15, (fromCents / 15).Value);
        Assert.Equal(new ExactCents(15225m), fromCents * 0.87m);
        Assert.Equal(new ExactCents(115m), new ExactCents(1015m) - new Cents(900));
        Assert.Equal(new ExactCents(1015.5m), new ExactCents(1015m) + new ExactCents(0.5m));
        Assert.Equal(ExactCents.Zero, new ExactCents(0m));
    }

    [Fact]
    public void ExactCents_Comparison_OrdersByValue()
    {
        var low = new ExactCents(1.5m);
        var high = new ExactCents(2m);

        Assert.True(low < high);
        Assert.True(low <= high);
        Assert.True(high > low);
        Assert.True(high >= low);
        Assert.True(high.CompareTo(low) > 0);
    }

    [Theory]
    [InlineData("1166.6666", "$11.67")]
    [InlineData("0.5", "$0.01")]
    [InlineData("-0.5", "-$0.01")]
    [InlineData("-1234.4", "-$12.34")]
    public void ExactCents_ToString_RoundsOnlyForDisplay(string cents, string expected) =>
        Assert.Equal(expected, new ExactCents(decimal.Parse(cents, System.Globalization.CultureInfo.InvariantCulture)).ToString());
}
