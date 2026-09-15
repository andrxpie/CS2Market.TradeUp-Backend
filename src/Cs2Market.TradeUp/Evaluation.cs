namespace Cs2Market.TradeUp;

/// <summary>
/// <paramref name="FeeRate"/> is the share of the listing price the seller does not receive.
/// Steam's 15% is charged on top of the seller's price, which is about 13% of the listing
/// price; the default keeps the net multiplier at exactly 0.87 (docs/DOMAIN.md § Economics).
/// </summary>
public sealed record EvaluationOptions(
    double FeeRate = 0.13,
    int Depth = 3,
    decimal LiquidityWarningRatio = 0.2m)
{
    public decimal NetMultiplier => 1m - (decimal)FeeRate;   // 0.87 at the default fee

    internal void Validate()
    {
        if (!(FeeRate is >= 0.0 and < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(FeeRate), FeeRate, "The fee rate lies in [0, 1).");
        }

        if (Depth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Depth), Depth, "Depth is at least 1.");
        }

        if (LiquidityWarningRatio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LiquidityWarningRatio), LiquidityWarningRatio, "The ratio is not negative.");
        }
    }
}

public sealed record StressResult(string Name, ExactCents Ev, ExactCents Profit, decimal Roi);

public sealed record StressReport(
    StressResult DepthX15,      // input prices × 1.5
    StressResult WorseFloat,    // inputs at the worst float of their band
    StressResult MinusTop);     // most expensive output priced at 0

/// <summary>
/// Everything known about one contract against one snapshot. <paramref name="CapturedAt"/> and
/// <paramref name="Stress"/> are positional and non-optional: a result without a snapshot date
/// or without stress tests cannot be constructed (golden case G9).
/// </summary>
public sealed record ContractEvaluation(
    DateTimeOffset CapturedAt,
    Contract Contract,
    Cents BasketCost,
    ExactCents Ev,
    ExactCents Profit,
    decimal Roi,
    double ProfitProbability,
    double AverageInputFloat,
    double TargetAverageFloat,
    double BreakEvenFloat,
    IReadOnlyList<OutcomeOdds> Outcomes,
    StressReport Stress,
    bool LiquidityWarning);

/// <summary>docs/DOMAIN.md § Economics and § Stress tests.</summary>
public static class ContractEvaluator
{
    public const string DepthStressName = "×1.5 depth";
    public const string WorseFloatStressName = "worse float";
    public const string MinusTopStressName = "−top";

    private const decimal DepthStressFactor = 1.5m;

    /// <summary>
    /// Fails with <see cref="ContractErrorCode.UnknownSkin"/> or
    /// <see cref="ContractErrorCode.NoOutputsAvailable"/> when <paramref name="catalog"/> is not
    /// the catalog the contract was created against and no longer supports it.
    /// </summary>
    public static Validated<ContractEvaluation> Evaluate(
        Contract contract, SkinCatalog catalog, PriceSnapshot snapshot, EvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (Contract.CheckAgainst(contract, catalog) is { } error)
        {
            return Validated<ContractEvaluation>.Fail(error.Code, error.Message);
        }

        var model = new OutcomeModel(contract, catalog);
        var net = options.NetMultiplier;
        var cost = contract.Cost;
        var average = FloatMath.AverageInputFloat(contract);

        var outcomes = model.Odds(average, snapshot);
        var sum = model.WeightedPriceSum(average, snapshot);
        var ev = model.Ev(sum);
        var profit = model.Profit(sum, net, cost);

        var profitProbability = outcomes
            .Where(odds => odds.Price is { } price && price.Value * net > cost.Value)
            .Sum(static odds => odds.Probability);

        var stress = new StressReport(
            DepthX15(model, sum, net, cost),
            WorseFloat(model, catalog, snapshot, net, cost),
            MinusTop(model, outcomes, snapshot, average, net, cost));

        var liquidityWarning = stress.MinusTop.Profit.Value <= 0
            || stress.MinusTop.Profit.Value < profit.Value * options.LiquidityWarningRatio;

        return Validated<ContractEvaluation>.Ok(new ContractEvaluation(
            snapshot.CapturedAt,
            contract,
            cost,
            ev,
            profit,
            Roi(profit, cost),
            Math.Min(profitProbability, 1.0),
            average,
            FloatMath.TargetAverageFloat(model, catalog, snapshot),
            FloatMath.BreakEvenFloat(model, snapshot, net),
            outcomes,
            stress,
            liquidityWarning));
    }

    private static StressResult DepthX15(OutcomeModel model, decimal sum, decimal net, Cents cost)
    {
        var stressedCost = cost.Scale(DepthStressFactor);
        var profit = model.Profit(sum, net, stressedCost);
        return new StressResult(DepthStressName, model.Ev(sum), profit, Roi(profit, stressedCost));
    }

    private static StressResult WorseFloat(OutcomeModel model, SkinCatalog catalog, PriceSnapshot snapshot, decimal net, Cents cost)
    {
        var (_, worst) = FloatMath.AchievableAverage(model.Contract.Inputs.Select(input => (catalog.Get(input.SkinId), input.Wear)));
        var sum = model.WeightedPriceSum(worst, snapshot);
        var profit = model.Profit(sum, net, cost);
        return new StressResult(WorseFloatStressName, model.Ev(sum), profit, Roi(profit, cost));
    }

    private static StressResult MinusTop(
        OutcomeModel model, IReadOnlyList<OutcomeOdds> outcomes, PriceSnapshot snapshot, double average, decimal net, Cents cost)
    {
        var top = outcomes
            .Where(static odds => odds.Price is not null)
            .OrderByDescending(static odds => odds.Price!.Value.Value)
            .ThenBy(static odds => odds.Skin.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var sum = model.WeightedPriceSum(average, snapshot, top?.Skin.Id);
        var profit = model.Profit(sum, net, cost);
        return new StressResult(MinusTopStressName, model.Ev(sum), profit, Roi(profit, cost));
    }

    private static decimal Roi(ExactCents profit, ExactCents cost) => profit.Value / cost.Value;
}
