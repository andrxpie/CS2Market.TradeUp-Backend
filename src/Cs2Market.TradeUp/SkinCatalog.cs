using System.Diagnostics.CodeAnalysis;

namespace Cs2Market.TradeUp;

/// <summary>
/// Immutable index over the skin catalog. Skins without a collection are kept in
/// <see cref="Excluded"/> rather than dropped, so the count stays observable; they are not
/// reachable through any lookup.
/// </summary>
public sealed class SkinCatalog
{
    private static readonly IReadOnlyList<Skin> NoSkins = [];

    private readonly Dictionary<string, Skin> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<(string CollectionId, Rarity Rarity), List<Skin>> _members = [];
    private readonly Dictionary<(string CollectionId, Rarity Rarity), List<Skin>> _inputs = [];

    /// <exception cref="ArgumentException">
    /// A duplicate id, or a float range that is not <c>0 ≤ MinFloat ≤ MaxFloat ≤ 1</c>.
    /// Ingestion skips such skins; the catalog does not guess.
    /// </exception>
    public SkinCatalog(IEnumerable<Skin> skins)
    {
        ArgumentNullException.ThrowIfNull(skins);

        var excluded = new List<Skin>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var skin in skins)
        {
            if (skin is null)
            {
                throw new ArgumentException("The catalog cannot contain a null skin.", nameof(skins));
            }

            if (!seen.Add(skin.Id))
            {
                throw new ArgumentException($"Duplicate skin id '{skin.Id}'.", nameof(skins));
            }

            if (!(skin.MinFloat >= 0.0 && skin.MinFloat <= skin.MaxFloat && skin.MaxFloat <= 1.0))
            {
                throw new ArgumentException(
                    $"Skin '{skin.Id}' has an invalid float range [{skin.MinFloat}, {skin.MaxFloat}].", nameof(skins));
            }

            if (skin.CollectionIds is null || skin.CollectionIds.Count == 0)
            {
                excluded.Add(skin);
                continue;
            }

            _byId.Add(skin.Id, skin);

            foreach (var collectionId in skin.CollectionIds.Distinct(StringComparer.Ordinal))
            {
                Bucket(_members, collectionId, skin.Rarity).Add(skin);
            }

            Bucket(_inputs, skin.InputCollectionId, skin.Rarity).Add(skin);
        }

        foreach (var list in _members.Values.Concat(_inputs.Values))
        {
            list.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        Excluded = excluded.AsReadOnly();
        CollectionIds = _members.Keys
            .Select(static key => key.CollectionId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Skins without a collection: knives, gloves.</summary>
    public IReadOnlyList<Skin> Excluded { get; }

    /// <summary>Every collection id, ordinal ascending.</summary>
    public IReadOnlyCollection<string> CollectionIds { get; }

    public bool TryGet(string skinId, [MaybeNullWhen(false)] out Skin skin) => _byId.TryGetValue(skinId, out skin);

    /// <exception cref="KeyNotFoundException">The id is unknown or belongs to an excluded skin.</exception>
    public Skin Get(string skinId) =>
        TryGet(skinId, out var skin) ? skin : throw new KeyNotFoundException($"Unknown skin id '{skinId}'.");

    /// <summary>Skins of <paramref name="rarity"/> listing the collection, ordinal by id.</summary>
    public IReadOnlyList<Skin> InCollection(string collectionId, Rarity rarity) =>
        _members.TryGetValue((collectionId, rarity), out var list) ? list.AsReadOnly() : NoSkins;

    /// <summary><c>O_j</c> in docs/DOMAIN.md: output skins the collection has at the target rarity.</summary>
    public int OutputCount(string collectionId, Rarity targetRarity) =>
        _members.TryGetValue((collectionId, targetRarity), out var list) ? list.Count : 0;

    /// <summary>Skins whose inputs count toward the collection: first-listed collection only.</summary>
    internal IReadOnlyList<Skin> InputsInCollection(string collectionId, Rarity rarity) =>
        _inputs.TryGetValue((collectionId, rarity), out var list) ? list.AsReadOnly() : NoSkins;

    private static List<Skin> Bucket(
        Dictionary<(string CollectionId, Rarity Rarity), List<Skin>> index, string collectionId, Rarity rarity)
    {
        if (!index.TryGetValue((collectionId, rarity), out var list))
        {
            list = [];
            index.Add((collectionId, rarity), list);
        }

        return list;
    }
}
