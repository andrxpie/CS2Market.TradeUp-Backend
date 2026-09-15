using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class BasketBuilderTests
{
    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();
    private readonly PriceSnapshot _snapshot = SyntheticSnapshot.Create();

    /// <summary>G8: skins at 100/100/100/400/900 with depth 3 cost 1300¢, and the list says how.</summary>
    [Fact]
    public void Build_DepthCap_G8()
    {
        var basket = BasketBuilder.Build(
            "depth", Rarity.MilSpec, Wear.FieldTested, false, _catalog, _snapshot, new BasketOptions()).ValueOrThrow();

        Assert.Equal(new Cents(1300), basket.Cost);
        Assert.Equal(
            [("depth-in-1", 3, 100L), ("depth-in-2", 3, 100L), ("depth-in-3", 3, 100L), ("depth-in-4", 1, 400L)],
            basket.Lots.Select(static lot => (lot.Skin.Id, lot.Quantity, lot.UnitPrice.Value)));
        Assert.Equal(basket.Cost, basket.Lots.Aggregate(Cents.Zero, static (sum, lot) => sum + lot.Total));
        Assert.All(basket.Lots, static lot => Assert.Equal(Wear.FieldTested, lot.Wear));
        Assert.False(basket.StatTrak);
    }

    [Fact]
    public void Build_Substitutes_AreTheNextCheapestUnusedSkins()
    {
        var basket = BasketBuilder.Build(
            "depth", Rarity.MilSpec, Wear.FieldTested, false, _catalog, _snapshot, new BasketOptions()).ValueOrThrow();

        var substitute = Assert.Single(basket.Substitutes);
        Assert.Equal(("depth-in-5", 3, 900L), (substitute.Skin.Id, substitute.Quantity, substitute.UnitPrice.Value));
    }

    [Fact]
    public void Build_NaiveCheapestTimesTen_IsNotWhatItCosts()
    {
        var basket = BasketBuilder.Build(
            "depth", Rarity.MilSpec, Wear.FieldTested, false, _catalog, _snapshot, new BasketOptions(Depth: 1, Size: 5)).ValueOrThrow();

        Assert.Equal(new Cents(1600), basket.Cost);   // 100 + 100 + 100 + 400 + 900, not 5 × 100
        Assert.Empty(basket.Substitutes);
    }

    [Fact]
    public void Build_TooFewPricedLots_FailsWithUnpricedInput()
    {
        var result = BasketBuilder.Build(
            "depth", Rarity.MilSpec, Wear.FieldTested, false, _catalog, _snapshot, new BasketOptions(Depth: 1));

        Assert.False(result.IsValid);
        Assert.Equal(ContractErrorCode.UnpricedInput, result.Error!.Code);
    }

    [Fact]
    public void Build_UnpricedWear_FailsWithUnpricedInput()
    {
        var snapshot = SyntheticSnapshot.Create(key => key.SkinId.StartsWith("alpha-in", StringComparison.Ordinal) && key.Wear == Wear.FactoryNew ? 0 : null);

        var result = BasketBuilder.Build("alpha", Rarity.MilSpec, Wear.FactoryNew, false, _catalog, snapshot, new BasketOptions());

        Assert.Equal(ContractErrorCode.UnpricedInput, result.Error!.Code);
    }

    [Fact]
    public void BuildSplit_TwoCollections_ListsCheapestFirst()
    {
        var basket = BasketBuilder.BuildSplit(
            [("alpha", 3), ("ev", 7)], Rarity.MilSpec, Wear.FieldTested, false, _catalog, _snapshot, new BasketOptions()).ValueOrThrow();

        Assert.Equal(new Cents((3 * 100) + (7 * 90)), basket.Cost);
        Assert.Equal(
            [("ev-in-1", 3), ("ev-in-2", 3), ("ev-in-3", 1), ("alpha-in-1", 3)],
            basket.Lots.Select(static lot => (lot.Skin.Id, lot.Quantity)));
        Assert.Equal(["ev-in-4", "alpha-in-2"], basket.Substitutes.Select(static lot => lot.Skin.Id));
        Assert.Equal(10, basket.Lots.Sum(static lot => lot.Quantity));
    }

    [Theory]
    [InlineData("alpha", 3, "ev", 6)]
    [InlineData("alpha", 0, "ev", 10)]
    [InlineData("alpha", 5, "alpha", 5)]
    public void BuildSplit_MalformedSplit_Throws(string first, int firstCount, string second, int secondCount) =>
        Assert.Throws<ArgumentException>(() => BasketBuilder.BuildSplit(
            [(first, firstCount), (second, secondCount)], Rarity.MilSpec, Wear.FieldTested, false, _catalog, _snapshot, new BasketOptions()));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(3, 0)]
    public void Build_InvalidOptions_Throw(int depth, int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BasketBuilder.Build(
            "alpha", Rarity.MilSpec, Wear.FieldTested, false, _catalog, _snapshot, new BasketOptions(depth, size)));

    [Fact]
    public void ToWorstCaseInputs_EachInputAtTheTopOfItsBand()
    {
        var basket = BasketBuilder.Build(
            "alpha", Rarity.MilSpec, Wear.MinimalWear, false, _catalog, _snapshot, new BasketOptions()).ValueOrThrow();

        var inputs = BasketBuilder.ToWorstCaseInputs(basket);

        Assert.Equal(10, inputs.Count);
        Assert.All(inputs, static input =>
        {
            Assert.Equal(Math.BitDecrement(WearBands.MinimalWearMax), input.Float);
            Assert.Equal(Wear.MinimalWear, WearBands.FromFloat(input.Float));
        });
        Assert.True(Contract.Create(inputs, _catalog).IsValid);
    }

    [Fact]
    public void ToWorstCaseInputs_CappedSkin_StopsAtItsOwnMaxFloat()
    {
        var capped = new Skin("cap-in", "cap-in", Rarity.MilSpec, 0.0, 0.08, ["cap"], false, false);
        var catalog = new SkinCatalog([capped, SyntheticCatalog.Skin("cap-out", Rarity.Restricted, "cap")]);
        var snapshot = new PriceSnapshot(
            SyntheticSnapshot.CapturedAt, [KeyValuePair.Create(new MarketKey("cap-in", Wear.MinimalWear, false), new Cents(7))]);
        var basket = BasketBuilder.Build("cap", Rarity.MilSpec, Wear.MinimalWear, false, catalog, snapshot, new BasketOptions(Depth: 10))
            .ValueOrThrow();

        Assert.All(BasketBuilder.ToWorstCaseInputs(basket), static input => Assert.Equal(0.08, input.Float));
    }

    [Theory]
    [InlineData(0.07)]
    [InlineData(0.10)]
    [InlineData(0.1499)]
    public void ToInputs_HitsTheRequestedAverage(double average)
    {
        var basket = BasketBuilder.BuildSplit(
            [("alpha", 4), ("ev", 6)], Rarity.MilSpec, Wear.MinimalWear, false, _catalog, _snapshot, new BasketOptions()).ValueOrThrow();

        var inputs = BasketBuilder.ToInputs(basket, average);
        var contract = Contract.Create(inputs, _catalog).ValueOrThrow();

        Tolerance.AssertClose(average, FloatMath.AverageInputFloat(contract));
        Assert.Equal(basket.Cost, contract.Cost);
        Assert.Equal([("alpha", 4), ("ev", 6)], contract.InputsByCollection.Select(static pair => (pair.Key, pair.Value)));
    }

    [Fact]
    public void ToInputs_ConstrainedLots_ShiftTheRestToKeepTheAverage()
    {
        // Three copies can reach at most 0.08; the other seven make up the difference.
        Skin[] skins =
        [
            new("cap-in", "cap-in", Rarity.MilSpec, 0.0, 0.08, ["mix"], false, false),
            SyntheticCatalog.Skin("wide-in-1", Rarity.MilSpec, "mix"),
            SyntheticCatalog.Skin("wide-in-2", Rarity.MilSpec, "mix"),
            SyntheticCatalog.Skin("wide-in-3", Rarity.MilSpec, "mix"),
            SyntheticCatalog.Skin("mix-out", Rarity.Restricted, "mix"),
        ];
        var catalog = new SkinCatalog(skins);
        var snapshot = new PriceSnapshot(
            SyntheticSnapshot.CapturedAt,
            skins.Where(static skin => skin.Rarity == Rarity.MilSpec).Select(static skin => KeyValuePair.Create(
                new MarketKey(skin.Id, Wear.MinimalWear, false), new Cents(skin.Id == "cap-in" ? 5 : 6))));
        var basket = BasketBuilder.Build("mix", Rarity.MilSpec, Wear.MinimalWear, false, catalog, snapshot, new BasketOptions())
            .ValueOrThrow();

        var inputs = BasketBuilder.ToInputs(basket, 0.12);

        Assert.All(inputs.Where(static input => input.SkinId == "cap-in"), static input => Assert.Equal(0.08, input.Float));
        Tolerance.AssertClose(0.12, inputs.Average(static input => input.Float));
        Assert.True(Contract.Create(inputs, catalog).IsValid);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.20)]
    [InlineData(double.NaN)]
    public void ToInputs_UnreachableAverage_Throws(double average)
    {
        var basket = BasketBuilder.Build(
            "alpha", Rarity.MilSpec, Wear.MinimalWear, false, _catalog, _snapshot, new BasketOptions()).ValueOrThrow();

        Assert.Throws<ArgumentOutOfRangeException>(() => BasketBuilder.ToInputs(basket, average));
    }

    [Fact]
    public void ToInputs_EmptyBasket_ReturnsNoInputs()
    {
        var empty = new Basket([], Cents.Zero, [], false);

        Assert.Empty(BasketBuilder.ToInputs(empty, 0.5));
        Assert.Empty(BasketBuilder.ToWorstCaseInputs(empty));
    }

    [Fact]
    public void Materializing_ALotThatCannotExistInItsWear_Throws()
    {
        var impossible = new Basket(
            [new BasketLot(SyntheticCatalog.Skin("low", Rarity.MilSpec, "c", 0.0, 0.05), Wear.FieldTested, 10, new Cents(1))],
            new Cents(10),
            [],
            false);

        Assert.Throws<InvalidOperationException>(() => BasketBuilder.ToWorstCaseInputs(impossible));
        Assert.Throws<InvalidOperationException>(() => BasketBuilder.ToInputs(impossible, 0.2));
    }
}
