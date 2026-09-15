using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class FloatMathTests
{
    private readonly SkinCatalog _catalog = SyntheticCatalog.Create();
    private readonly PriceSnapshot _snapshot = SyntheticSnapshot.Create();

    /// <summary>G3: avg 0.10 on a 0–1 output → 0.10 → Minimal Wear.</summary>
    [Fact]
    public void OutputFloat_UncappedSkin_G3()
    {
        var output = _catalog.Get("uncapped-out-1");

        var value = FloatMath.OutputFloat(0.10, output);

        Tolerance.AssertClose(0.10, value);
        Assert.Equal(Wear.MinimalWear, WearBands.FromFloat(value));
    }

    /// <summary>G4: avg 0.10 on a 0–0.6 output → 0.06 → Factory New.</summary>
    [Fact]
    public void OutputFloat_CappedSkin_G4()
    {
        var output = _catalog.Get("capped-out-1");

        var value = FloatMath.OutputFloat(0.10, output);

        Tolerance.AssertClose(0.06, value);
        Assert.Equal(Wear.FactoryNew, WearBands.FromFloat(value));
    }

    /// <summary>G5: the FN threshold on a 0–0.6 output is 0.07/0.6; 0.1166 lands FN, 0.1167 does not.</summary>
    [Fact]
    public void ThresholdAverage_CappedSkin_G5()
    {
        var output = _catalog.Get("capped-out-1");

        Assert.Equal(0.07 / 0.6, FloatMath.ThresholdAverage(output, Wear.FactoryNew));
        Assert.Equal(Wear.FactoryNew, WearBands.FromFloat(FloatMath.OutputFloat(0.1166, output)));
        Assert.NotEqual(Wear.FactoryNew, WearBands.FromFloat(FloatMath.OutputFloat(0.1167, output)));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.6)]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(0.08)]
    public void ThresholdAverage_ZeroMinFloat_EqualsTheShorthand(double maxFloat)
    {
        var output = SyntheticCatalog.Skin("out", Rarity.Restricted, "c", 0.0, maxFloat);

        Assert.Equal(Math.Min(1.0, 0.07 / maxFloat), FloatMath.ThresholdAverage(output, Wear.FactoryNew));
    }

    [Fact]
    public void ThresholdAverage_NonZeroMinFloat_UsesTheGeneralForm()
    {
        var output = SyntheticCatalog.Skin("out", Rarity.Restricted, "c", 0.06, 0.80);

        Tolerance.AssertClose((0.07 - 0.06) / (0.80 - 0.06), FloatMath.ThresholdAverage(output, Wear.FactoryNew));
        Tolerance.AssertClose((0.15 - 0.06) / (0.80 - 0.06), FloatMath.ThresholdAverage(output, Wear.MinimalWear));
        Assert.Equal(1.0, FloatMath.ThresholdAverage(output, Wear.BattleScarred));
    }

    [Fact]
    public void ThresholdAverage_ClampsToTheUnitInterval()
    {
        var worn = SyntheticCatalog.Skin("worn", Rarity.Restricted, "c", 0.40, 1.0);

        Assert.Equal(0.0, FloatMath.ThresholdAverage(worn, Wear.FactoryNew));
        Assert.Equal(0.0, FloatMath.ThresholdAverage(worn, Wear.FieldTested));
        Tolerance.AssertClose(0.05 / 0.6, FloatMath.ThresholdAverage(worn, Wear.WellWorn));
    }

    [Fact]
    public void ThresholdAverage_FixedFloatSkin_IsAllOrNothing()
    {
        var fixedFloat = SyntheticCatalog.Skin("fixed", Rarity.Restricted, "c", 0.10, 0.10);

        Assert.Equal(0.0, FloatMath.ThresholdAverage(fixedFloat, Wear.FactoryNew));
        Assert.Equal(1.0, FloatMath.ThresholdAverage(fixedFloat, Wear.MinimalWear));
        Tolerance.AssertClose(0.10, FloatMath.OutputFloat(0.9, fixedFloat));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void OutputFloat_AverageOutsideUnitInterval_Throws(double average) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FloatMath.OutputFloat(average, _catalog.Get("uncapped-out-1")));

    [Fact]
    public void OutputFloat_TopOfTheRange_NeverLeavesTheSkinRange()
    {
        var skin = SyntheticCatalog.Skin("odd", Rarity.Restricted, "c", 0.1, 0.7);

        Assert.InRange(FloatMath.OutputFloat(1.0, skin), 0.1, 0.7);
        Assert.Equal(0.1, FloatMath.OutputFloat(0.0, skin));
    }

    [Fact]
    public void AverageInputFloat_IsTheMeanOfRawFloats()
    {
        // One input at 0.14 is offset by nine at 0.075 (docs/DOMAIN.md § Float).
        var inputs = ContractBuilder.New(_catalog).From("alpha", 10).AtFloat(0.075).Inputs();
        inputs[0] = inputs[0] with { Float = 0.14 };

        var contract = Contract.Create(inputs, _catalog).ValueOrThrow();

        Tolerance.AssertClose(0.0815, FloatMath.AverageInputFloat(contract));
    }

    [Fact]
    public void BreakEvenFloat_ProfitOnlyWhileTheOutputStaysFactoryNew_IsTheFnThreshold()
    {
        // Ten 100¢ inputs; the output sells for 5000¢ FN but only 1000¢ MW: 870 − 1000 < 0.
        var contract = ContractBuilder.New(_catalog).From("capped", 10).AtFloat(0.10).Build();

        var breakEven = FloatMath.BreakEvenFloat(contract, _catalog, _snapshot, new EvaluationOptions());

        Assert.Equal(0.07 / 0.6, breakEven);
    }

    [Fact]
    public void BreakEvenFloat_ProfitableAcrossTheRange_IsOne()
    {
        var contract = ContractBuilder.New(_catalog).From("ev", 5).From("ev-filler", 5).Priced(_snapshot).Build();

        Assert.Equal(1.0, FloatMath.BreakEvenFloat(contract, _catalog, _snapshot, new EvaluationOptions()));
    }

    [Fact]
    public void BreakEvenFloat_NeverProfitable_IsZero()
    {
        var contract = ContractBuilder.New(_catalog).From("alpha", 10).Build();

        Assert.Equal(0.0, FloatMath.BreakEvenFloat(contract, _catalog, _snapshot, new EvaluationOptions()));
    }

    [Fact]
    public void BreakEvenFloat_NonMonotoneProfit_ReturnsTheEndOfTheLastProfitableStretch()
    {
        // Battle-Scarred sells above Well-Worn: profitable FN..FT, not WW, profitable again BS.
        var snapshot = SyntheticSnapshot.Create(key => key.SkinId == "uncapped-out-1"
            ? key.Wear switch { Wear.WellWorn => 500, Wear.BattleScarred => 3000, _ => 2000 }
            : null);
        var contract = ContractBuilder.New(_catalog).From("uncapped", 10).Build();

        Assert.Equal(1.0, FloatMath.BreakEvenFloat(contract, _catalog, snapshot, new EvaluationOptions()));

        var noBattleScarred = SyntheticSnapshot.Create(key => key.SkinId == "uncapped-out-1"
            ? key.Wear switch { Wear.WellWorn or Wear.BattleScarred => 500, _ => 2000 }
            : null);

        Assert.Equal(WearBands.FieldTestedMax, FloatMath.BreakEvenFloat(contract, _catalog, noBattleScarred, new EvaluationOptions()));
    }
}
