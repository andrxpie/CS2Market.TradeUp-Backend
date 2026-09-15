namespace Cs2Market.TradeUp.Tests;

public sealed class WearBandsTests
{
    [Theory]
    [InlineData(0.0, Wear.FactoryNew)]
    [InlineData(0.069999999, Wear.FactoryNew)]
    [InlineData(0.07, Wear.MinimalWear)]
    [InlineData(0.149999999, Wear.MinimalWear)]
    [InlineData(0.15, Wear.FieldTested)]
    [InlineData(0.379999999, Wear.FieldTested)]
    [InlineData(0.38, Wear.WellWorn)]
    [InlineData(0.449999999, Wear.WellWorn)]
    [InlineData(0.45, Wear.BattleScarred)]
    [InlineData(1.0, Wear.BattleScarred)]
    public void FromFloat_HalfOpenBands(double value, Wear expected) =>
        Assert.Equal(expected, WearBands.FromFloat(value));

    [Fact]
    public void FromFloat_JustBelowEachBoundary_StaysInTheBetterBand()
    {
        Assert.Equal(Wear.FactoryNew, WearBands.FromFloat(Math.BitDecrement(WearBands.FactoryNewMax)));
        Assert.Equal(Wear.MinimalWear, WearBands.FromFloat(Math.BitDecrement(WearBands.MinimalWearMax)));
        Assert.Equal(Wear.FieldTested, WearBands.FromFloat(Math.BitDecrement(WearBands.FieldTestedMax)));
        Assert.Equal(Wear.WellWorn, WearBands.FromFloat(Math.BitDecrement(WearBands.WellWornMax)));
    }

    [Theory]
    [InlineData(-0.000001)]
    [InlineData(1.000001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FromFloat_OutsideUnitInterval_Throws(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WearBands.FromFloat(value));

    [Theory]
    [InlineData(Wear.FactoryNew, 0.0, 0.07)]
    [InlineData(Wear.MinimalWear, 0.07, 0.15)]
    [InlineData(Wear.FieldTested, 0.15, 0.38)]
    [InlineData(Wear.WellWorn, 0.38, 0.45)]
    [InlineData(Wear.BattleScarred, 0.45, 1.0)]
    public void Range_MatchesDomainBands(Wear wear, double min, double maxExclusive)
    {
        Assert.Equal((min, maxExclusive), WearBands.Range(wear));
        Assert.Equal(wear, WearBands.FromFloat(min));
    }

    [Theory]
    [InlineData(Wear.FactoryNew, "Factory New")]
    [InlineData(Wear.MinimalWear, "Minimal Wear")]
    [InlineData(Wear.FieldTested, "Field-Tested")]
    [InlineData(Wear.WellWorn, "Well-Worn")]
    [InlineData(Wear.BattleScarred, "Battle-Scarred")]
    public void MarketSuffix_RoundTrips(Wear wear, string suffix)
    {
        Assert.Equal(suffix, wear.MarketSuffix());
        Assert.True(WearBands.TryParseMarketSuffix(suffix, out var bare));
        Assert.Equal(wear, bare);
        Assert.True(WearBands.TryParseMarketSuffix($"({suffix})", out var wrapped));
        Assert.Equal(wear, wrapped);
    }

    [Fact]
    public void TryParseMarketSuffix_ReadsTheTailOfAMarketHashName()
    {
        const string Name = "AK-47 | Redline (Field-Tested)";

        Assert.True(WearBands.TryParseMarketSuffix(Name.AsSpan(Name.LastIndexOf('(')), out var wear));
        Assert.Equal(Wear.FieldTested, wear);
    }

    [Theory]
    [InlineData("")]
    [InlineData("()")]
    [InlineData("field-tested")]
    [InlineData("Field-Tested ")]
    [InlineData("(Field-Tested")]
    [InlineData("Field Tested")]
    [InlineData("(Holo)")]
    public void TryParseMarketSuffix_Unknown_ReturnsFalse(string text) =>
        Assert.False(WearBands.TryParseMarketSuffix(text, out _));

    [Fact]
    public void Range_UndefinedWear_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WearBands.Range((Wear)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((Wear)9).MarketSuffix());
    }
}
