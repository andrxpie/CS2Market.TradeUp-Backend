namespace Cs2Market.TradeUp.Tests.Fixtures;

/// <summary>
/// Prices for <see cref="SyntheticCatalog"/>, stamped 2026-09-01. Every skin is priced at every
/// wear unless noted, so a test only has to care about wear where it says so.
/// </summary>
internal static class SyntheticSnapshot
{
    public static readonly DateTimeOffset CapturedAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    public static PriceSnapshot Create() => new(CapturedAt, Prices());

    public static PriceSnapshot Create(Func<MarketKey, long?> overridePrice) =>
        new(CapturedAt, Prices()
            .Select(pair => (pair.Key, Price: overridePrice(pair.Key) ?? pair.Value.Value))
            .Where(static pair => pair.Price > 0)
            .Select(static pair => KeyValuePair.Create(pair.Key, new Cents(pair.Price))));

    public static IEnumerable<KeyValuePair<MarketKey, Cents>> Prices()
    {
        foreach (var skin in SyntheticCatalog.Skins().Where(static skin => skin.CollectionIds.Count > 0))
        {
            foreach (var wear in Enum.GetValues<Wear>())
            {
                yield return Price(skin.Id, wear, false, PriceOf(skin.Id, wear));

                if (skin.CollectionIds[0] is "alpha" or "filler1")
                {
                    yield return Price(skin.Id, wear, true, 2 * PriceOf(skin.Id, wear));
                }
            }
        }
    }

    public static long PriceOf(string skinId, Wear wear) => skinId switch
    {
        "depth-in-1" or "depth-in-2" or "depth-in-3" => 100,
        "depth-in-4" => 400,
        "depth-in-5" => 900,
        "ev-out-1" => 1000,
        "ev-out-2" => 2000,
        "ev-filler-out-1" => 500,
        "capped-out-1" => wear switch
        {
            Wear.FactoryNew => 5000,
            Wear.MinimalWear => 1000,
            _ => 500,
        },
        "whale-out-1" => 50_000,
        _ when skinId.StartsWith("ev-", StringComparison.Ordinal) && skinId.Contains("-in-", StringComparison.Ordinal) => 90,
        _ when skinId.StartsWith("whale-in-", StringComparison.Ordinal) => 500,
        _ when skinId.StartsWith("whale-out-", StringComparison.Ordinal) => 100,
        _ when skinId.StartsWith("steady-out-", StringComparison.Ordinal) => 2000,
        _ when skinId.StartsWith("alpha-out-", StringComparison.Ordinal) => 300,
        _ when skinId.StartsWith("filler1-out-", StringComparison.Ordinal) => 200,
        _ when skinId.StartsWith("filler5-out-", StringComparison.Ordinal) => 150,
        _ when skinId.StartsWith("depth-out-", StringComparison.Ordinal) => 400,
        _ when skinId.StartsWith("uncapped-out-", StringComparison.Ordinal) => 400,
        _ when skinId.StartsWith("apex-in-", StringComparison.Ordinal) => 1000,
        _ when skinId.StartsWith("apex-out-", StringComparison.Ordinal) => 20_000,
        _ => 100,
    };

    private static KeyValuePair<MarketKey, Cents> Price(string skinId, Wear wear, bool statTrak, long cents) =>
        KeyValuePair.Create(new MarketKey(skinId, wear, statTrak), new Cents(cents));
}
