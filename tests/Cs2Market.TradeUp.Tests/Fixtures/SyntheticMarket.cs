namespace Cs2Market.TradeUp.Tests.Fixtures;

/// <summary>
/// A larger generated market for the scanner: every rarity, capped and offset float ranges,
/// collections without a top tier, unpriced keys and a few expensive outliers. Deterministic —
/// derived from a string hash, no <see cref="Random"/>.
/// </summary>
internal static class SyntheticMarket
{
    private static readonly long[] BasePrice = [4, 12, 40, 180, 800, 4000];
    private static readonly double[] MaxFloats = [1.0, 1.0, 0.8, 0.6, 0.5, 0.08];

    public static (SkinCatalog Catalog, PriceSnapshot Snapshot) Generate(int collections = 30)
    {
        var skins = new List<Skin>();
        var prices = new List<KeyValuePair<MarketKey, Cents>>();

        for (var c = 0; c < collections; c++)
        {
            var collectionId = $"col-{c:00}";

            foreach (var rarity in Enum.GetValues<Rarity>())
            {
                var count = 1 + (int)(Hash(collectionId, rarity) % 5);
                if ((rarity == Rarity.Covert && c % 4 == 0) || (rarity == Rarity.Consumer && c % 3 == 0))
                {
                    count = 0;
                }

                for (var k = 0; k < count; k++)
                {
                    var id = $"{collectionId}-{rarity}-{k}";
                    var h = Hash(id);
                    var minFloat = h % 5 == 0 ? 0.06 : 0.0;
                    var maxFloat = MaxFloats[(int)(h / 7 % (ulong)MaxFloats.Length)];
                    var skin = new Skin(id, id, rarity, minFloat, maxFloat, [collectionId], true, false);
                    skins.Add(skin);

                    var multiplier = 0.5 + (h / 13 % 30 / 10.0);
                    if (h % 17 == 0)
                    {
                        multiplier *= 12;
                    }

                    foreach (var wear in Enum.GetValues<Wear>())
                    {
                        if (!WearBands.TryFeasibleRange(skin, wear, out _, out _) || Hash(id, wear) % 11 == 0)
                        {
                            continue;
                        }

                        var cents = (long)Math.Max(1, Math.Round(BasePrice[(int)rarity] * multiplier * WearFactor(wear)));
                        prices.Add(KeyValuePair.Create(new MarketKey(id, wear, false), new Cents(cents)));
                    }
                }
            }
        }

        return (new SkinCatalog(skins), new PriceSnapshot(SyntheticSnapshot.CapturedAt, prices));
    }

    private static double WearFactor(Wear wear) => wear switch
    {
        Wear.FactoryNew => 1.8,
        Wear.MinimalWear => 1.3,
        Wear.FieldTested => 1.0,
        Wear.WellWorn => 0.9,
        _ => 0.85,
    };

    private static ulong Hash(params object[] parts)
    {
        // FNV-1a over the invariant text of the parts.
        var hash = 14695981039346656037UL;
        foreach (var ch in string.Join('|', parts))
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        return hash;
    }
}
