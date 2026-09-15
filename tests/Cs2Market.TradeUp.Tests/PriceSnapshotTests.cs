using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class PriceSnapshotTests
{
    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();

    [Fact]
    public void TryGetPrice_MissingKey_ReturnsFalse()
    {
        var snapshot = new PriceSnapshot(
            SyntheticSnapshot.CapturedAt,
            [KeyValuePair.Create(new MarketKey("alpha-in-1", Wear.FieldTested, false), new Cents(100))]);

        Assert.True(snapshot.TryGetPrice(new MarketKey("alpha-in-1", Wear.FieldTested, false), out var price));
        Assert.Equal(new Cents(100), price);

        Assert.False(snapshot.TryGetPrice(new MarketKey("alpha-in-1", Wear.FactoryNew, false), out var missing));
        Assert.False(snapshot.TryGetPrice(new MarketKey("alpha-in-1", Wear.FieldTested, true), out _));
        Assert.False(snapshot.TryGetPrice(new MarketKey("nope", Wear.FieldTested, false), out _));
        Assert.Equal(default, missing);   // the out value is meaningless when false, and never a price
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(SyntheticSnapshot.CapturedAt, snapshot.CapturedAt);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Constructor_NonPositivePrice_Throws(long cents) =>
        Assert.Throws<ArgumentException>(() => new PriceSnapshot(
            SyntheticSnapshot.CapturedAt,
            [KeyValuePair.Create(new MarketKey("alpha-in-1", Wear.FieldTested, false), new Cents(cents))]));

    [Fact]
    public void Constructor_DuplicateKeyOrMissingSkinId_Throws()
    {
        var key = new MarketKey("alpha-in-1", Wear.FieldTested, false);

        Assert.Throws<ArgumentException>(() => new PriceSnapshot(
            SyntheticSnapshot.CapturedAt, [KeyValuePair.Create(key, new Cents(1)), KeyValuePair.Create(key, new Cents(2))]));
        Assert.Throws<ArgumentException>(() => new PriceSnapshot(
            SyntheticSnapshot.CapturedAt, [KeyValuePair.Create(new MarketKey(null!, Wear.FieldTested, false), new Cents(1))]));
        Assert.Throws<ArgumentNullException>(() => new PriceSnapshot(SyntheticSnapshot.CapturedAt, null!));
    }

    [Fact]
    public void CheapestFirst_OrdersByPriceThenId_AndSkipsUnpriced()
    {
        var snapshot = SyntheticSnapshot.Create(key => key.SkinId == "depth-in-2" ? 0 : null);

        var lots = snapshot.CheapestFirst("depth", Rarity.MilSpec, Wear.FieldTested, false, _catalog);

        Assert.Equal(["depth-in-1", "depth-in-3", "depth-in-4", "depth-in-5"], lots.Select(static lot => lot.Skin.Id));
        Assert.Equal([100L, 100L, 400L, 900L], lots.Select(static lot => lot.Price.Value));
    }

    [Fact]
    public void CheapestFirst_TiesOnPrice_BreakOrdinalById()
    {
        var lots = SyntheticSnapshot.Create().CheapestFirst("alpha", Rarity.MilSpec, Wear.MinimalWear, false, _catalog);

        Assert.Equal(["alpha-in-1", "alpha-in-2", "alpha-in-3", "alpha-in-4"], lots.Select(static lot => lot.Skin.Id));
    }

    [Fact]
    public void CheapestFirst_SkinThatCannotReachTheWear_IsLeftOut()
    {
        var capped = SyntheticCatalog.Skin("low-in", Rarity.MilSpec, "low", maxFloat: 0.10);
        var catalog = new SkinCatalog([capped, SyntheticCatalog.Skin("low-out", Rarity.Restricted, "low")]);
        var snapshot = new PriceSnapshot(
            SyntheticSnapshot.CapturedAt,
            Enum.GetValues<Wear>().Select(wear => KeyValuePair.Create(new MarketKey("low-in", wear, false), new Cents(10))));

        Assert.Single(snapshot.CheapestFirst("low", Rarity.MilSpec, Wear.MinimalWear, false, catalog));
        Assert.Empty(snapshot.CheapestFirst("low", Rarity.MilSpec, Wear.FieldTested, false, catalog));
    }

    [Fact]
    public void CheapestFirst_StatTrakAndNormal_AreSeparateMarkets()
    {
        var snapshot = SyntheticSnapshot.Create();

        Assert.Equal(200L, snapshot.CheapestFirst("alpha", Rarity.MilSpec, Wear.FieldTested, true, _catalog)[0].Price.Value);
        Assert.Empty(snapshot.CheapestFirst("ev", Rarity.MilSpec, Wear.FieldTested, true, _catalog));
    }

    [Fact]
    public void HighestPrice_TakesTheBestPricedWear()
    {
        var snapshot = SyntheticSnapshot.Create();

        Assert.Equal(new Cents(5000), snapshot.HighestPrice("capped-out-1", false));
        Assert.Null(snapshot.HighestPrice("capped-out-1", true));
    }
}
