using System.Reflection;
using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class ContractEvaluatorTests
{
    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();
    private readonly PriceSnapshot _snapshot = SyntheticSnapshot.Create();

    /// <summary>
    /// G6: 5 inputs from a collection with outputs at 1000¢ and 2000¢, 5 from one with a 500¢
    /// output, 900¢ basket. <c>EV = 17500/15</c>, <c>profit = 115</c> — decimal, no tolerance.
    /// </summary>
    [Fact]
    public void Evaluate_TwoCollections_G6()
    {
        var evaluation = Evaluate(G6Contract());

        Assert.Equal(17500m / 15, evaluation.Ev.Value);
        Assert.Equal(115m, evaluation.Profit.Value);
        Assert.Equal(new Cents(900), evaluation.BasketCost);
        Assert.Equal(115m / 900m, evaluation.Roi);
    }

    [Fact]
    public void EvaluationOptions_DefaultNetMultiplier_IsExactly087()
    {
        Assert.Equal(0.87m, new EvaluationOptions().NetMultiplier);
        Assert.Equal(0.85m, new EvaluationOptions(FeeRate: 0.15).NetMultiplier);
        Assert.Equal(1m, new EvaluationOptions(FeeRate: 0).NetMultiplier);
    }

    /// <summary>G9: a result built from a snapshot stamped 2026-09-01 carries that date.</summary>
    [Fact]
    public void Evaluate_CarriesTheSnapshotDate_G9()
    {
        var evaluation = Evaluate(G6Contract());

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), evaluation.CapturedAt);
        Assert.Equal(_snapshot.CapturedAt, evaluation.CapturedAt);
    }

    /// <summary>G9: every public constructor demands the date and the stress report, with no default.</summary>
    [Fact]
    public void ContractEvaluation_CannotBeConstructedWithoutDateOrStress_G9()
    {
        var constructors = typeof(ContractEvaluation).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(constructors);
        Assert.All(constructors, static constructor =>
        {
            var parameters = constructor.GetParameters();
            Assert.Contains(parameters, static p => p.Name == "CapturedAt" && p.ParameterType == typeof(DateTimeOffset) && !p.HasDefaultValue);
            Assert.Contains(parameters, static p => p.Name == "Stress" && p.ParameterType == typeof(StressReport) && !p.HasDefaultValue);
        });
    }

    [Fact]
    public void Evaluate_ProfitProbability_SumsOutputsThatSellAboveTheBasket()
    {
        // Only the 2000¢ output clears 900¢ after the fee: 1740 > 900; 870 and 435 do not.
        var evaluation = Evaluate(G6Contract());

        Assert.Equal(5.0 / 15, evaluation.ProfitProbability);
    }

    [Fact]
    public void Evaluate_ReportsOutcomesAndFloatsFromTheSameMath()
    {
        var contract = G6Contract();

        var evaluation = Evaluate(contract);

        Assert.Same(contract, evaluation.Contract);
        Assert.Equal(OutcomePool.Build(contract, _catalog, _snapshot), evaluation.Outcomes);
        Assert.Equal(FloatMath.AverageInputFloat(contract), evaluation.AverageInputFloat);
        Assert.Equal(FloatMath.BreakEvenFloat(contract, _catalog, _snapshot, new EvaluationOptions()), evaluation.BreakEvenFloat);
    }

    [Fact]
    public void Evaluate_UnpricedOutput_AddsNothingToEv_AndIsReportedAsNull()
    {
        var snapshot = SyntheticSnapshot.Create(key => key.SkinId == "ev-out-2" ? 0 : null);

        var evaluation = ContractEvaluator.Evaluate(G6Contract(), _catalog, snapshot, new EvaluationOptions()).ValueOrThrow();

        Assert.Equal(7500m / 15, evaluation.Ev.Value);
        Assert.Null(evaluation.Outcomes.Single(static odds => odds.Skin.Id == "ev-out-2").Price);
        Assert.Equal(0.0, evaluation.ProfitProbability);
    }

    [Fact]
    public void Evaluate_CappedOutputFromMinimalWear_TargetsTheFactoryNewThreshold()
    {
        var contract = ContractBuilder.New(_catalog).From("capped", 10).AtFloat(0.10).Build();

        var evaluation = Evaluate(contract);

        Assert.Equal(0.07 / 0.6, evaluation.TargetAverageFloat);
        Assert.Equal(0.07 / 0.6, evaluation.BreakEvenFloat);
        Assert.Equal(3350m, evaluation.Profit.Value);
    }

    [Fact]
    public void Evaluate_UncappedOutputFromMinimalWear_CannotReachFactoryNew()
    {
        // MW inputs never average below 0.07, so the best reachable band is MW itself.
        var contract = ContractBuilder.New(_catalog).From("uncapped", 10).AtFloat(0.10).Build();

        var evaluation = Evaluate(contract);

        Tolerance.AssertClose(WearBands.MinimalWearMax, evaluation.TargetAverageFloat);
        Assert.True(evaluation.TargetAverageFloat < WearBands.MinimalWearMax);
    }

    [Fact]
    public void Evaluate_FactoryNewInputs_TargetStaysInsideTheirBand()
    {
        var contract = ContractBuilder.New(_catalog).From("uncapped", 10).AtFloat(0.01).Build();

        var evaluation = Evaluate(contract);

        Tolerance.AssertClose(WearBands.FactoryNewMax, evaluation.TargetAverageFloat);
    }

    [Fact]
    public void Evaluate_OutputThatCannotBeFactoryNew_TargetsItsBestPhysicalBand()
    {
        // The output's float starts at 0.10, so Minimal Wear is the best it can ever be; the most
        // valuable outcome at its best reachable band decides the target, not the cheaper one.
        Skin[] skins =
        [
            SyntheticCatalog.Skin("w-in", Rarity.MilSpec, "w"),
            SyntheticCatalog.Skin("w-out-worn", Rarity.Restricted, "w", 0.10, 0.80),
            SyntheticCatalog.Skin("w-out-cheap", Rarity.Restricted, "w"),
        ];
        var catalog = new SkinCatalog(skins);
        var snapshot = new PriceSnapshot(SyntheticSnapshot.CapturedAt,
        [
            KeyValuePair.Create(new MarketKey("w-in", Wear.MinimalWear, false), new Cents(10)),
            KeyValuePair.Create(new MarketKey("w-out-worn", Wear.MinimalWear, false), new Cents(900)),
            KeyValuePair.Create(new MarketKey("w-out-cheap", Wear.MinimalWear, false), new Cents(50)),
        ]);
        var contract = ContractBuilder.New(catalog).From("w", 10).AtFloat(0.10).Priced(snapshot).Build();

        var evaluation = ContractEvaluator.Evaluate(contract, catalog, snapshot, new EvaluationOptions()).ValueOrThrow();

        Tolerance.AssertClose((0.15 - 0.10) / 0.70, evaluation.TargetAverageFloat);
    }

    [Fact]
    public void Evaluate_NoOutputPricedAtItsBestWear_TargetsTheTopOfTheInputBands()
    {
        var snapshot = SyntheticSnapshot.Create(key => key.SkinId == "capped-out-1" && key.Wear == Wear.FactoryNew ? 0 : null);
        var contract = ContractBuilder.New(_catalog).From("capped", 10).AtFloat(0.10).Build();

        var evaluation = ContractEvaluator.Evaluate(contract, _catalog, snapshot, new EvaluationOptions()).ValueOrThrow();

        Tolerance.AssertClose(WearBands.MinimalWearMax, evaluation.TargetAverageFloat);
        Assert.True(evaluation.TargetAverageFloat < WearBands.MinimalWearMax);
    }

    [Fact]
    public void Evaluate_CatalogThatNoLongerKnowsTheSkins_Fails()
    {
        var contract = G6Contract();
        var withoutEv = new SkinCatalog(SyntheticCatalog.Skins().Where(static skin => !skin.Id.StartsWith("ev-in", StringComparison.Ordinal)));
        var withoutOutputs = new SkinCatalog(SyntheticCatalog.Skins().Where(static skin => !skin.Id.StartsWith("ev-filler-out", StringComparison.Ordinal)));

        Assert.Equal(
            ContractErrorCode.UnknownSkin,
            ContractEvaluator.Evaluate(contract, withoutEv, _snapshot, new EvaluationOptions()).Error!.Code);
        Assert.Equal(
            ContractErrorCode.NoOutputsAvailable,
            ContractEvaluator.Evaluate(contract, withoutOutputs, _snapshot, new EvaluationOptions()).Error!.Code);
    }

    [Theory]
    [InlineData(-0.01, 3, 0.2)]
    [InlineData(1.0, 3, 0.2)]
    [InlineData(double.NaN, 3, 0.2)]
    [InlineData(0.13, 0, 0.2)]
    [InlineData(0.13, 3, -0.1)]
    public void Evaluate_InvalidOptions_Throw(double feeRate, int depth, double ratio) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractEvaluator.Evaluate(
            G6Contract(), _catalog, _snapshot, new EvaluationOptions(feeRate, depth, (decimal)ratio)));

    [Fact]
    public void Evaluate_NullArguments_Throw()
    {
        var contract = G6Contract();

        Assert.Throws<ArgumentNullException>(() => ContractEvaluator.Evaluate(null!, _catalog, _snapshot, new EvaluationOptions()));
        Assert.Throws<ArgumentNullException>(() => ContractEvaluator.Evaluate(contract, null!, _snapshot, new EvaluationOptions()));
        Assert.Throws<ArgumentNullException>(() => ContractEvaluator.Evaluate(contract, _catalog, null!, new EvaluationOptions()));
        Assert.Throws<ArgumentNullException>(() => ContractEvaluator.Evaluate(contract, _catalog, _snapshot, null!));
    }

    private Contract G6Contract() =>
        ContractBuilder.New(_catalog).From("ev", 5).From("ev-filler", 5).Priced(_snapshot).Build();

    private ContractEvaluation Evaluate(Contract contract) =>
        ContractEvaluator.Evaluate(contract, _catalog, _snapshot, new EvaluationOptions()).ValueOrThrow();
}
