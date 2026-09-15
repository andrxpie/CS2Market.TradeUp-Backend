namespace Cs2Market.TradeUp.Tests.Fixtures;

/// <summary>
/// Fluent helper so tests state intent rather than assemble ten records. Inputs rotate through
/// the collection's skins of the chosen rarity; unpriced builders use 100¢ per input.
/// <see cref="Build"/> throws on an invalid contract — tests for rejections use
/// <see cref="Contract.Create"/> directly.
/// </summary>
internal sealed class ContractBuilder
{
    private readonly SkinCatalog _catalog;
    private readonly List<(string CollectionId, int Count)> _parts = [];
    private Rarity _rarity = Rarity.MilSpec;
    private double _float = 0.20;
    private bool _statTrak;
    private PriceSnapshot? _snapshot;

    private ContractBuilder(SkinCatalog catalog) => _catalog = catalog;

    public static ContractBuilder New(SkinCatalog catalog) => new(catalog);

    public ContractBuilder From(string collectionId, int count)
    {
        _parts.Add((collectionId, count));
        return this;
    }

    public ContractBuilder OfRarity(Rarity rarity)
    {
        _rarity = rarity;
        return this;
    }

    public ContractBuilder AtFloat(double value)
    {
        _float = value;
        return this;
    }

    public ContractBuilder StatTrak()
    {
        _statTrak = true;
        return this;
    }

    public ContractBuilder Priced(PriceSnapshot snapshot)
    {
        _snapshot = snapshot;
        return this;
    }

    public List<ContractInput> Inputs()
    {
        var wear = WearBands.FromFloat(_float);
        var inputs = new List<ContractInput>();

        foreach (var (collectionId, count) in _parts)
        {
            var skins = _catalog.InCollection(collectionId, _rarity);
            for (var i = 0; i < count; i++)
            {
                var skin = skins[i % skins.Count];
                var price = _snapshot is null
                    ? new Cents(100)
                    : _snapshot.TryGetPrice(new MarketKey(skin.Id, wear, _statTrak), out var listed)
                        ? listed
                        : throw new InvalidOperationException($"Fixture has no price for {skin.Id} {wear}.");

                inputs.Add(new ContractInput(skin.Id, wear, _float, _statTrak, price));
            }
        }

        return inputs;
    }

    public Contract Build() => Contract.Create(Inputs(), _catalog).ValueOrThrow();
}
