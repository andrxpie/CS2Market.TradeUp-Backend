using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class SkinCatalogTests
{
    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();

    [Fact]
    public void Constructor_SkinWithoutCollection_GoesToExcluded()
    {
        Assert.Equal(["karambit-fade", "sport-gloves-vice"], _catalog.Excluded.Select(static skin => skin.Id));
        Assert.False(_catalog.TryGet("karambit-fade", out _));
        Assert.Throws<KeyNotFoundException>(() => _catalog.Get("karambit-fade"));
    }

    [Fact]
    public void CollectionIds_AreOrdinalAscending()
    {
        string[] expected =
        [
            "alpha", "apex", "barren", "capped", "depth", "ev", "ev-filler",
            "filler1", "filler5", "steady", "uncapped", "whale",
        ];

        Assert.Equal(expected, _catalog.CollectionIds);
    }

    [Fact]
    public void TryGet_KnownSkin_ReturnsIt()
    {
        Assert.True(_catalog.TryGet("alpha-in-1", out var skin));
        Assert.Equal(Rarity.MilSpec, skin.Rarity);
        Assert.Same(skin, _catalog.Get("alpha-in-1"));
        Assert.False(_catalog.TryGet("nope", out _));
    }

    [Fact]
    public void InCollection_FiltersByRarity_OrdinalById()
    {
        Assert.Equal(
            ["alpha-out-1", "alpha-out-2", "alpha-out-3"],
            _catalog.InCollection("alpha", Rarity.Restricted).Select(static skin => skin.Id));
        Assert.Empty(_catalog.InCollection("alpha", Rarity.Covert));
        Assert.Empty(_catalog.InCollection("no-such-collection", Rarity.MilSpec));
    }

    [Theory]
    [InlineData("alpha", 3)]
    [InlineData("filler1", 1)]
    [InlineData("filler5", 5)]
    [InlineData("barren", 0)]
    [InlineData("no-such-collection", 0)]
    public void OutputCount_CountsTargetRaritySkins(string collectionId, int expected) =>
        Assert.Equal(expected, _catalog.OutputCount(collectionId, Rarity.Restricted));

    [Fact]
    public void FloatRange_IsMaxMinusMin() =>
        Tolerance.AssertClose(0.6, _catalog.Get("capped-out-1").FloatRange);

    [Fact]
    public void Constructor_DuplicateId_Throws()
    {
        var skin = SyntheticCatalog.Skin("dup", Rarity.MilSpec, "alpha");
        Assert.Throws<ArgumentException>(() => new SkinCatalog([skin, skin with { Name = "other" }]));
    }

    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(0.6, 0.5)]
    [InlineData(0.0, 1.1)]
    [InlineData(double.NaN, 0.5)]
    public void Constructor_InvalidFloatRange_Throws(double min, double max) =>
        Assert.Throws<ArgumentException>(() => new SkinCatalog([SyntheticCatalog.Skin("bad", Rarity.MilSpec, "alpha", min, max)]));

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new SkinCatalog(null!));
        Assert.Throws<ArgumentException>(() => new SkinCatalog([null!]));
    }

    [Fact]
    public void MultiCollectionSkin_IsAnOutputOfEveryCollection_AndAnInputOfTheFirst()
    {
        var shared = new Skin("shared", "shared", Rarity.Restricted, 0, 1, ["b-col", "a-col"], false, false);
        var sharedInput = new Skin("shared-in", "shared-in", Rarity.MilSpec, 0, 1, ["b-col", "a-col"], false, false);
        var catalog = new SkinCatalog([shared, sharedInput]);

        Assert.Equal(1, catalog.OutputCount("a-col", Rarity.Restricted));
        Assert.Equal(1, catalog.OutputCount("b-col", Rarity.Restricted));
        Assert.Single(catalog.InputsInCollection("b-col", Rarity.MilSpec));
        Assert.Empty(catalog.InputsInCollection("a-col", Rarity.MilSpec));
        Assert.Equal(["a-col", "b-col"], catalog.CollectionIds);
    }
}
