namespace Cs2Market.TradeUp;

/// <summary>The contract rarity ladder; Covert is terminal (docs/DOMAIN.md § Contract rules).</summary>
public enum Rarity
{
    Consumer = 0,
    Industrial = 1,
    MilSpec = 2,
    Restricted = 3,
    Classified = 4,
    Covert = 5,
}

public static class RarityExtensions
{
    /// <summary>The rarity a contract of <paramref name="rarity"/> inputs produces; null for Covert.</summary>
    public static Rarity? Next(this Rarity rarity) => rarity switch
    {
        >= Rarity.Consumer and < Rarity.Covert => rarity + 1,
        Rarity.Covert => null,
        _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Unknown rarity."),
    };

    /// <summary>False for Covert: knives and gloves cannot be produced by a contract.</summary>
    public static bool CanBeContractInput(this Rarity r) => r.Next() is not null;
}
