namespace Cs2Market.TradeUp;

/// <summary>
/// A weapon finish. <see cref="CollectionIds"/> is empty for knives and gloves, which no
/// contract can use or produce. When a skin belongs to more than one collection, the first id
/// is the one its inputs count toward; it is an output of every collection it lists.
/// </summary>
public sealed record Skin(
    string Id,
    string Name,
    Rarity Rarity,
    double MinFloat,
    double MaxFloat,
    IReadOnlyList<string> CollectionIds,
    bool StatTrakAvailable,
    bool SouvenirAvailable)
{
    public double FloatRange => MaxFloat - MinFloat;

    /// <summary>The collection an input of this skin counts toward: the first listed.</summary>
    internal string InputCollectionId => CollectionIds[0];
}
