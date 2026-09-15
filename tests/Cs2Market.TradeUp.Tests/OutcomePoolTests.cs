using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class OutcomePoolTests
{
    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();
    private readonly PriceSnapshot _snapshot = SyntheticSnapshot.Create();

    /// <summary>G1: 3 inputs from a 3-output collection, 7 from a 1-output filler → 3/16 and 7/16.</summary>
    [Fact]
    public void Build_LowOutputFiller_G1()
    {
        var contract = ContractBuilder.New(_catalog).From("alpha", 3).From("filler1", 7).Build();

        var outcomes = OutcomePool.Build(contract, _catalog, _snapshot);

        Assert.Equal(16, OutcomePool.TicketCount(contract, _catalog));
        Assert.Equal(4, outcomes.Count);
        Assert.All(outcomes.Where(static odds => odds.CollectionId == "alpha"), static odds => Assert.Equal(0.1875, odds.Probability));
        Assert.Equal(0.4375, outcomes.Single(static odds => odds.CollectionId == "filler1").Probability);
        Tolerance.AssertClose(1.0, outcomes.Sum(static odds => odds.Probability));
    }

    /// <summary>G2: the same primary with a 5-output filler drops to 3/44.</summary>
    [Fact]
    public void Build_HighOutputFiller_G2()
    {
        var contract = ContractBuilder.New(_catalog).From("alpha", 3).From("filler5", 7).Build();

        var outcomes = OutcomePool.Build(contract, _catalog, _snapshot);

        Assert.Equal(44, OutcomePool.TicketCount(contract, _catalog));
        Assert.All(outcomes.Where(static odds => odds.CollectionId == "alpha"), static odds => Assert.Equal(3.0 / 44, odds.Probability));
        Tolerance.AssertClose(0.068182, Math.Round(outcomes.First(static odds => odds.CollectionId == "alpha").Probability, 6));
        Tolerance.AssertClose(1.0, outcomes.Sum(static odds => odds.Probability));
    }

    [Theory]
    [InlineData("alpha", 1, "filler5", 9)]
    [InlineData("ev", 5, "ev-filler", 5)]
    [InlineData("whale", 10, null, 0)]
    [InlineData("filler5", 2, "steady", 8)]
    public void Build_ProbabilitiesSumToOne(string first, int firstCount, string? second, int secondCount)
    {
        var builder = ContractBuilder.New(_catalog).From(first, firstCount);
        if (second is not null)
        {
            builder.From(second, secondCount);
        }

        var outcomes = OutcomePool.Build(builder.Build(), _catalog, _snapshot);

        Tolerance.AssertClose(1.0, outcomes.Sum(static odds => odds.Probability));
    }

    [Fact]
    public void Build_OrdersByProbabilityDescending_ThenSkinId()
    {
        var contract = ContractBuilder.New(_catalog).From("alpha", 3).From("filler1", 7).Build();

        var ids = OutcomePool.Build(contract, _catalog, _snapshot).Select(static odds => odds.Skin.Id);

        Assert.Equal(["filler1-out-1", "alpha-out-1", "alpha-out-2", "alpha-out-3"], ids);
    }

    [Fact]
    public void Build_PricesEachOutputAtTheWearItLandsIn()
    {
        // Average 0.10 on a 0–0.6 output lands at 0.06: Factory New.
        var contract = ContractBuilder.New(_catalog).From("capped", 10).AtFloat(0.10).Build();

        var outcome = Assert.Single(OutcomePool.Build(contract, _catalog, _snapshot));

        Assert.Equal(new Cents(5000), outcome.Price);
    }

    [Fact]
    public void Build_UnpricedOutput_IsNullAndStillTakesTickets()
    {
        var snapshot = SyntheticSnapshot.Create(key => key.SkinId == "alpha-out-2" ? 0 : null);
        var contract = ContractBuilder.New(_catalog).From("alpha", 3).From("filler1", 7).Build();

        var outcomes = OutcomePool.Build(contract, _catalog, snapshot);
        var unpriced = outcomes.Single(static odds => odds.Skin.Id == "alpha-out-2");

        Assert.Null(unpriced.Price);
        Assert.Equal(0.1875, unpriced.Probability);
        Assert.Equal(16, OutcomePool.TicketCount(contract, _catalog));
    }

    [Fact]
    public void Build_StatTrakContract_UsesStatTrakPrices()
    {
        var contract = ContractBuilder.New(_catalog).From("alpha", 3).From("filler1", 7).StatTrak().Priced(_snapshot).Build();

        var outcomes = OutcomePool.Build(contract, _catalog, _snapshot);

        Assert.Equal(new Cents(600), outcomes.First(static odds => odds.CollectionId == "alpha").Price);
    }

    [Fact]
    public void Build_CatalogWithoutTheOutputs_Throws()
    {
        var contract = ContractBuilder.New(_catalog).From("alpha", 10).Build();
        var inputsOnly = new SkinCatalog(SyntheticCatalog.Skins().Where(static skin => skin.Rarity == Rarity.MilSpec));

        Assert.Throws<InvalidOperationException>(() => OutcomePool.Build(contract, inputsOnly, _snapshot));
        Assert.Throws<ArgumentNullException>(() => OutcomePool.Build(contract, _catalog, null!));
        Assert.Throws<ArgumentNullException>(() => OutcomePool.TicketCount(null!, _catalog));
    }

    [Fact]
    public void Build_OutputSharedByTwoCollections_TakesTicketsFromBoth()
    {
        Skin[] skins =
        [
            SyntheticCatalog.Skin("a-in", Rarity.MilSpec, "a"),
            SyntheticCatalog.Skin("b-in", Rarity.MilSpec, "b"),
            new("shared-out", "shared-out", Rarity.Restricted, 0, 1, ["a", "b"], false, false),
            SyntheticCatalog.Skin("b-out", Rarity.Restricted, "b"),
        ];
        var catalog = new SkinCatalog(skins);
        var inputs = ContractBuilder.New(catalog).From("a", 4).From("b", 6).Build();

        var outcomes = OutcomePool.Build(inputs, catalog, SyntheticSnapshot.Create());

        // Tickets: 4 × 1 + 6 × 2 = 16. The shared skin appears once per collection.
        Assert.Equal(16, OutcomePool.TicketCount(inputs, catalog));
        Assert.Equal(10.0 / 16, outcomes.Where(static odds => odds.Skin.Id == "shared-out").Sum(static odds => odds.Probability));
        Tolerance.AssertClose(1.0, outcomes.Sum(static odds => odds.Probability));
    }
}
