namespace Cs2Market.TradeUp;

public readonly record struct MarketKey(string SkinId, Wear Wear, bool StatTrak);

/// <summary>
/// Prices from one upstream snapshot, stamped with the time upstream captured them.
/// A missing key means unpriced; nothing in this project substitutes zero for it.
/// </summary>
public sealed class PriceSnapshot
{
    private static readonly Wear[] AllWears = Enum.GetValues<Wear>();

    private readonly Dictionary<MarketKey, Cents> _prices = [];

    /// <exception cref="ArgumentException">A duplicate key, or a price that is not positive.</exception>
    public PriceSnapshot(DateTimeOffset capturedAt, IEnumerable<KeyValuePair<MarketKey, Cents>> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        CapturedAt = capturedAt;

        foreach (var (key, price) in prices)
        {
            if (key.SkinId is null)
            {
                throw new ArgumentException("A market key needs a skin id.", nameof(prices));
            }

            if (price.Value <= 0)
            {
                throw new ArgumentException($"Price for {key} must be positive, was {price.Value}¢.", nameof(prices));
            }

            if (!_prices.TryAdd(key, price))
            {
                throw new ArgumentException($"Duplicate price for {key}.", nameof(prices));
            }
        }
    }

    public DateTimeOffset CapturedAt { get; }

    public int Count => _prices.Count;

    public bool TryGetPrice(MarketKey key, out Cents price) => _prices.TryGetValue(key, out price);

    /// <summary>
    /// Priced skins whose inputs count toward the collection, at the rarity and wear, cheapest
    /// first and then ordinal by id. Skins whose float range cannot reach the wear are left out.
    /// </summary>
    public IReadOnlyList<(Skin Skin, Cents Price)> CheapestFirst(
        string collectionId, Rarity rarity, Wear wear, bool statTrak, SkinCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var priced = new List<(Skin Skin, Cents Price)>();

        foreach (var skin in catalog.InputsInCollection(collectionId, rarity))
        {
            if (WearBands.TryFeasibleRange(skin, wear, out _, out _)
                && _prices.TryGetValue(new MarketKey(skin.Id, wear, statTrak), out var price))
            {
                priced.Add((skin, price));
            }
        }

        // Stable sort over an id-ordered list: ties on price stay ordinal by id.
        return priced.OrderBy(static lot => lot.Price.Value).ToArray();
    }

    /// <summary>The highest price of the skin across every wear; null when no wear is priced.</summary>
    internal Cents? HighestPrice(string skinId, bool statTrak)
    {
        Cents? highest = null;

        foreach (var wear in AllWears)
        {
            if (_prices.TryGetValue(new MarketKey(skinId, wear, statTrak), out var price)
                && (highest is null || price > highest.Value))
            {
                highest = price;
            }
        }

        return highest;
    }
}
