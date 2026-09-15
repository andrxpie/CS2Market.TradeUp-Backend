// M1 exit criterion: a ranked scan over an inline synthetic catalog, zero configuration.
// Synthetic prices, not market data. Reading the real dumps arrives with `--cache` in M2-3.
using System.Globalization;
using Cs2Market.TradeUp;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var (catalog, snapshot) = SyntheticMarket.Create();
var criteria = new ScanCriteria(Budget: new Cents(2500), Top: 15);

var outcome = ContractScanner.Scan(criteria, catalog, snapshot);

Console.WriteLine($"Cs2Market.TradeUp demo — synthetic snapshot captured {outcome.CapturedAt:yyyy-MM-dd HH:mm} UTC");
Console.WriteLine(
    $"budget {criteria.Budget}, fee {criteria.FeeRate:P0}, depth {criteria.Depth} · combinations {outcome.CombinationsTotal:N0}: "
    + $"{outcome.CombinationsPruned:N0} pruned, {outcome.CombinationsEvaluated:N0} evaluated · {outcome.Elapsed.TotalMilliseconds:F0} ms"
    + (outcome.TimedOut ? " · TIMED OUT, partial result" : string.Empty));
Console.WriteLine();

if (outcome.Top.Count == 0)
{
    Console.WriteLine("No profitable contract within the budget.");
    return;
}

Console.WriteLine(
    $"{"#",2}  {"contract",-22} {"inputs",-19} {"cost",8} {"EV",9} {"profit",9} {"ROI",7} {"P(win)",6} "
    + $"{"target",6} {"b/even",6} {"×1.5",9} {"worse fl",9} {"−top",9}");

var rank = 0;
foreach (var candidate in outcome.Top)
{
    var e = candidate.Evaluation;
    var contract = candidate.PrimaryCount == Contract.InputCount
        ? $"{candidate.PrimaryCollectionId} ×10"
        : $"{candidate.PrimaryCollectionId} {candidate.PrimaryCount} + {candidate.FillerCollectionId} {10 - candidate.PrimaryCount}";
    var inputs = $"{e.Contract.InputRarity} {Short(candidate.Basket.Lots[0].Wear)}";

    Console.WriteLine(
        $"{++rank,2}  {contract,-22} {inputs,-19} {e.BasketCost,8} {e.Ev,9} {e.Profit,9} {e.Roi,7:P0} {e.ProfitProbability,6:P0} "
        + $"{e.TargetAverageFloat,6:F3} {e.BreakEvenFloat,6:F3} {e.Stress.DepthX15.Profit,9} {e.Stress.WorseFloat.Profit,9} "
        + $"{e.Stress.MinusTop.Profit,9}{(e.LiquidityWarning ? "  ⚠ liquidity: EV rests on one output" : string.Empty)}");
}

Console.WriteLine();
Console.WriteLine("Profit is wallet-denominated: Steam wallet funds cannot be withdrawn. Not financial advice.");

static string Short(Wear wear) => wear switch
{
    Wear.FactoryNew => "FN",
    Wear.MinimalWear => "MW",
    Wear.FieldTested => "FT",
    Wear.WellWorn => "WW",
    _ => "BS",
};

/// <summary>
/// Twenty-four collections with every rarity, capped float ranges, missing top tiers, unpriced
/// keys and a few outliers. Deterministic: derived from a string hash, no <see cref="Random"/>.
/// </summary>
internal static class SyntheticMarket
{
    private static readonly string[] Names =
    [
        "anubis", "ancient", "baggage", "bank", "cache", "canals", "chop-shop", "cobblestone",
        "dust-2", "gods-and-monsters", "havoc", "inferno", "italy", "lake", "militia", "mirage",
        "norse", "nuke", "office", "overpass", "rising-sun", "safehouse", "train", "vertigo",
    ];

    private static readonly long[] BasePrice = [4, 12, 40, 180, 800, 4000];
    private static readonly double[] MaxFloats = [1.0, 1.0, 0.8, 0.6, 0.5, 0.08];

    public static (SkinCatalog Catalog, PriceSnapshot Snapshot) Create()
    {
        var skins = new List<Skin>();
        var prices = new List<KeyValuePair<MarketKey, Cents>>();

        foreach (var collectionId in Names)
        {
            foreach (var rarity in Enum.GetValues<Rarity>())
            {
                var count = 1 + (int)(Hash(collectionId, rarity) % 5);
                if (rarity == Rarity.Covert && Hash(collectionId) % 4 == 0)
                {
                    count = 0;
                }

                for (var k = 0; k < count; k++)
                {
                    var id = $"{collectionId}-{rarity.ToString().ToLowerInvariant()}-{k + 1}";
                    var h = Hash(id);
                    var skin = new Skin(
                        id, id, rarity, h % 5 == 0 ? 0.06 : 0.0, MaxFloats[(int)(h / 7 % (ulong)MaxFloats.Length)],
                        [collectionId], StatTrakAvailable: true, SouvenirAvailable: false);
                    skins.Add(skin);

                    var multiplier = (0.5 + (h / 13 % 30 / 10.0)) * (h % 17 == 0 ? 12 : 1);
                    foreach (var wear in Enum.GetValues<Wear>())
                    {
                        var (bandMin, _) = WearBands.Range(wear);
                        if (bandMin > skin.MaxFloat || Hash(id, wear) % 11 == 0)
                        {
                            continue;
                        }

                        var factor = wear switch
                        {
                            Wear.FactoryNew => 1.8,
                            Wear.MinimalWear => 1.3,
                            Wear.FieldTested => 1.0,
                            Wear.WellWorn => 0.9,
                            _ => 0.85,
                        };
                        var cents = (long)Math.Max(1, Math.Round(BasePrice[(int)rarity] * multiplier * factor));
                        prices.Add(KeyValuePair.Create(new MarketKey(id, wear, false), new Cents(cents)));
                    }
                }
            }
        }

        var capturedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        return (new SkinCatalog(skins), new PriceSnapshot(capturedAt, prices));
    }

    private static ulong Hash(params object[] parts)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in string.Join('|', parts))
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        return hash;
    }
}
