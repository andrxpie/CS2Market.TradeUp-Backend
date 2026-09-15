namespace Cs2Market.TradeUp.Tests;

/// <summary>Float values and probabilities are doubles; money is not, and is never compared with this.</summary>
internal static class Tolerance
{
    public const double Eps = 1e-9;

    public static void AssertClose(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= Eps, $"Expected {expected:R} ± {Eps}, got {actual:R}.");
}
