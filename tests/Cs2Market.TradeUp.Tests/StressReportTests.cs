using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class StressReportTests
{
    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();
    private readonly PriceSnapshot _snapshot = SyntheticSnapshot.Create();

    [Fact]
    public void LiquidityWarning_WhenEvRestsOnOneOutput()
    {
        // One output at 50 000¢, three at 100¢; ten 500¢ inputs.
        var evaluation = Evaluate(ContractBuilder.New(_catalog).From("whale", 10).Priced(_snapshot).Build());

        Assert.Equal(5940.25m, evaluation.Profit.Value);
        Assert.Equal((300m * 10 * 0.87m / 40) - 5000m, evaluation.Stress.MinusTop.Profit.Value);
        Assert.True(evaluation.LiquidityWarning);
    }

    [Fact]
    public void LiquidityWarning_NotRaised_WhenOutputsCarryEvTogether()
    {
        // Four outputs at 2000¢; −top still clears 20% of the 740¢ profit: 305 ≥ 148.
        var evaluation = Evaluate(ContractBuilder.New(_catalog).From("steady", 10).Priced(_snapshot).Build());

        Assert.Equal(740m, evaluation.Profit.Value);
        Assert.Equal(305m, evaluation.Stress.MinusTop.Profit.Value);
        Assert.False(evaluation.LiquidityWarning);
    }

    [Fact]
    public void LiquidityWarning_RatioIsConfigurable()
    {
        var contract = ContractBuilder.New(_catalog).From("steady", 10).Priced(_snapshot).Build();

        var strict = ContractEvaluator.Evaluate(contract, _catalog, _snapshot, new EvaluationOptions(LiquidityWarningRatio: 0.5m)).ValueOrThrow();

        Assert.True(strict.LiquidityWarning);   // 305 < 740 × 0.5
    }

    [Fact]
    public void Stress_G6Contract_AllThreeResults()
    {
        var evaluation = Evaluate(ContractBuilder.New(_catalog).From("ev", 5).From("ev-filler", 5).Priced(_snapshot).Build());
        var stress = evaluation.Stress;

        // ×1.5 depth: same EV, basket 1350¢.
        Assert.Equal(ContractEvaluator.DepthStressName, stress.DepthX15.Name);
        Assert.Equal(evaluation.Ev, stress.DepthX15.Ev);
        Assert.Equal(1015m - 1350m, stress.DepthX15.Profit.Value);
        Assert.Equal(-335m / 1350m, stress.DepthX15.Roi);

        // Worse float: every wear is priced alike here, so nothing moves.
        Assert.Equal(ContractEvaluator.WorseFloatStressName, stress.WorseFloat.Name);
        Assert.Equal(115m, stress.WorseFloat.Profit.Value);

        // −top: the 2000¢ output sells for nothing, EV = 7500/15 = 500.
        Assert.Equal(ContractEvaluator.MinusTopStressName, stress.MinusTop.Name);
        Assert.Equal(500m, stress.MinusTop.Ev.Value);
        Assert.Equal(435m - 900m, stress.MinusTop.Profit.Value);
        Assert.Equal(-465m / 900m, stress.MinusTop.Roi);
        Assert.True(evaluation.LiquidityWarning);
    }

    [Fact]
    public void Stress_WorseFloat_MovesTheOutputOutOfFactoryNew()
    {
        // At 0.10 the capped output is FN (5000¢); at the worst MW float it lands MW (1000¢).
        var evaluation = Evaluate(ContractBuilder.New(_catalog).From("capped", 10).AtFloat(0.10).Build());

        Assert.Equal(3350m, evaluation.Profit.Value);
        Assert.Equal(1000m, evaluation.Stress.WorseFloat.Ev.Value);
        Assert.Equal(870m - 1000m, evaluation.Stress.WorseFloat.Profit.Value);
    }

    [Fact]
    public void Stress_NoPricedOutput_MinusTopEqualsTheBaseline()
    {
        var snapshot = SyntheticSnapshot.Create(key => key.SkinId.StartsWith("alpha-out", StringComparison.Ordinal) ? 0 : null);
        var contract = ContractBuilder.New(_catalog).From("alpha", 10).Build();

        var evaluation = ContractEvaluator.Evaluate(contract, _catalog, snapshot, new EvaluationOptions()).ValueOrThrow();

        Assert.Equal(ExactCents.Zero, evaluation.Ev);
        Assert.Equal(evaluation.Profit, evaluation.Stress.MinusTop.Profit);
        Assert.Equal(-1000m, evaluation.Profit.Value);
        Assert.True(evaluation.LiquidityWarning);
    }

    [Fact]
    public void StressReport_AllThreeFieldsAreRequired()
    {
        var parameters = typeof(StressReport).GetConstructors().Single().GetParameters();

        Assert.Equal(["DepthX15", "WorseFloat", "MinusTop"], parameters.Select(static p => p.Name));
        Assert.All(parameters, static p => Assert.False(p.HasDefaultValue));
    }

    private ContractEvaluation Evaluate(Contract contract) =>
        ContractEvaluator.Evaluate(contract, _catalog, _snapshot, new EvaluationOptions()).ValueOrThrow();
}
