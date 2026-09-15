namespace Cs2Market.TradeUp.Tests.Fixtures;

/// <summary>
/// Named collections with controlled output counts, so the golden cases in docs/DOMAIN.md read
/// directly. Inputs are Mil-Spec and outputs Restricted unless a row says otherwise.
/// </summary>
internal static class SyntheticCatalog
{
    /// <summary>(collection id, Mil-Spec inputs, Restricted outputs).</summary>
    public static readonly IReadOnlyList<(string Id, int Inputs, int Outputs)> Collections =
    [
        ("alpha", 4, 3),       // primary collection, G1/G2
        ("filler1", 4, 1),     // low-output filler, G1
        ("filler5", 4, 5),     // high-output filler, G2
        ("ev", 4, 2),          // G6: outputs at 1000¢ and 2000¢
        ("ev-filler", 4, 1),   // G6: output at 500¢
        ("depth", 5, 1),       // G8: inputs priced 100/100/100/400/900
        ("capped", 4, 1),      // G4, G5: output max_float 0.6
        ("uncapped", 4, 1),    // G3: output max_float 1.0
        ("whale", 4, 4),       // liquidity: one output at 50 000¢, three at 100¢
        ("steady", 4, 4),      // liquidity control: four outputs at 2000¢
        ("barren", 4, 0),      // no Restricted skin: NoOutputsAvailable
    ];

    public static SkinCatalog Create() => new(Skins());

    public static IReadOnlyList<Skin> Skins()
    {
        var skins = new List<Skin>();

        foreach (var (id, inputs, outputs) in Collections)
        {
            for (var i = 1; i <= inputs; i++)
            {
                skins.Add(Skin($"{id}-in-{i}", Rarity.MilSpec, id));
            }

            for (var i = 1; i <= outputs; i++)
            {
                var maxFloat = id == "capped" ? 0.6 : 1.0;
                skins.Add(Skin($"{id}-out-{i}", Rarity.Restricted, id, maxFloat: maxFloat));
            }
        }

        // Covert is terminal: ten of these can never be traded up.
        for (var i = 1; i <= 4; i++)
        {
            skins.Add(Skin($"apex-in-{i}", Rarity.Classified, "apex"));
            skins.Add(Skin($"apex-out-{i}", Rarity.Covert, "apex"));
        }

        // Knives and gloves: no collection.
        skins.Add(new Skin("karambit-fade", "Karambit | Fade", Rarity.Covert, 0.0, 0.08, [], true, false));
        skins.Add(new Skin("sport-gloves-vice", "Sport Gloves | Vice", Rarity.Covert, 0.06, 0.8, [], false, false));

        return skins;
    }

    public static Skin Skin(string id, Rarity rarity, string collectionId, double minFloat = 0.0, double maxFloat = 1.0) =>
        new(id, id, rarity, minFloat, maxFloat, [collectionId], StatTrakAvailable: true, SouvenirAvailable: false);
}
