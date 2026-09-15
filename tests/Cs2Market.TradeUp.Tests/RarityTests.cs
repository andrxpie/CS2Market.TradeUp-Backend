namespace Cs2Market.TradeUp.Tests;

public sealed class RarityTests
{
    [Theory]
    [InlineData(Rarity.Consumer, Rarity.Industrial)]
    [InlineData(Rarity.Industrial, Rarity.MilSpec)]
    [InlineData(Rarity.MilSpec, Rarity.Restricted)]
    [InlineData(Rarity.Restricted, Rarity.Classified)]
    [InlineData(Rarity.Classified, Rarity.Covert)]
    public void Next_ClimbsOneStep(Rarity rarity, Rarity expected)
    {
        Assert.Equal(expected, rarity.Next());
        Assert.True(rarity.CanBeContractInput());
    }

    [Fact]
    public void Next_Covert_IsTerminal()
    {
        Assert.Null(Rarity.Covert.Next());
        Assert.False(Rarity.Covert.CanBeContractInput());
    }

    [Fact]
    public void Next_UndefinedValue_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ((Rarity)42).Next());
}
