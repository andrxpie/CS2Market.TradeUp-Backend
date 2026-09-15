using System.Collections.ObjectModel;
using System.Globalization;

namespace Cs2Market.TradeUp;

/// <summary>
/// One input lot as bought. <paramref name="Souvenir"/> exists so a Souvenir input can be
/// rejected: the skin record alone cannot tell a Souvenir copy from a normal one.
/// </summary>
// CA1720: "Float" is the game's term for the wear value and the fixed name in docs/M1_DOMAIN_API.md.
#pragma warning disable CA1720
public sealed record ContractInput(string SkinId, Wear Wear, double Float, bool StatTrak, Cents Price, bool Souvenir = false);
#pragma warning restore CA1720

/// <summary>
/// A valid trade-up contract. <see cref="Create"/> is the only way to build one, so an invalid
/// contract cannot reach the evaluator.
/// </summary>
public sealed record Contract
{
    public const int InputCount = 10;

    private Contract(
        IReadOnlyList<ContractInput> inputs,
        Rarity inputRarity,
        bool statTrak,
        Cents cost,
        IReadOnlyDictionary<string, int> inputsByCollection)
    {
        Inputs = inputs;
        InputRarity = inputRarity;
        OutputRarity = inputRarity + 1;
        StatTrak = statTrak;
        Cost = cost;
        InputsByCollection = inputsByCollection;
    }

    public IReadOnlyList<ContractInput> Inputs { get; }

    public Rarity InputRarity { get; }

    public Rarity OutputRarity { get; }

    public bool StatTrak { get; }

    /// <summary>Sum of the ten input prices.</summary>
    public Cents Cost { get; }

    /// <summary><c>n_C</c>: inputs per collection, ordinal by collection id.</summary>
    public IReadOnlyDictionary<string, int> InputsByCollection { get; }

    /// <summary>Enforces every rule in docs/DOMAIN.md § Contract rules; never throws for a rejection.</summary>
    public static Validated<Contract> Create(IReadOnlyList<ContractInput> inputs, SkinCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(catalog);

        if (inputs.Count != InputCount)
        {
            return Fail(ContractErrorCode.WrongInputCount, $"A contract takes exactly {InputCount} inputs, got {inputs.Count}.");
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i] is null)
            {
                throw new ArgumentException($"Input {i} is null.", nameof(inputs));
            }
        }

        if (inputs.FirstOrDefault(static input => input.Souvenir) is { } souvenir)
        {
            return Fail(ContractErrorCode.SouvenirInput, $"Souvenir items cannot be used: '{souvenir.SkinId}'.");
        }

        var statTrak = inputs[0].StatTrak;
        if (inputs.Any(input => input.StatTrak != statTrak))
        {
            return Fail(ContractErrorCode.MixedStatTrak, "Inputs must be all StatTrak or all normal, never mixed.");
        }

        var skins = new Skin[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            if (!catalog.TryGet(inputs[i].SkinId, out var skin))
            {
                return Fail(ContractErrorCode.UnknownSkin, $"Input {i} references unknown skin '{inputs[i].SkinId}'.");
            }

            skins[i] = skin;
        }

        var rarity = skins[0].Rarity;
        if (skins.Any(skin => skin.Rarity != rarity))
        {
            return Fail(ContractErrorCode.MixedRarity, "All inputs must share one rarity.");
        }

        if (!rarity.CanBeContractInput())
        {
            return Fail(ContractErrorCode.CovertInput, $"{rarity} is the highest rarity and cannot be traded up.");
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (!WearBands.TryFeasibleRange(skins[i], input.Wear, out var min, out var max)
                || !(input.Float >= min && input.Float <= max))
            {
                return Fail(
                    ContractErrorCode.FloatOutOfRange,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Input {i} ('{input.SkinId}') has float {input.Float}, outside what {input.Wear} allows for this skin."));
            }
        }

        var cost = Cents.Zero;
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i].Price.Value <= 0)
            {
                return Fail(ContractErrorCode.UnpricedInput, $"Input {i} ('{inputs[i].SkinId}') has no price.");
            }

            cost += inputs[i].Price;
        }

        var byCollection = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var skin in skins)
        {
            byCollection[skin.InputCollectionId] = byCollection.GetValueOrDefault(skin.InputCollectionId) + 1;
        }

        var outputRarity = rarity + 1;
        if (byCollection.Keys.FirstOrDefault(id => catalog.OutputCount(id, outputRarity) == 0) is { } barren)
        {
            return Fail(
                ContractErrorCode.NoOutputsAvailable,
                $"Collection '{barren}' has no {outputRarity} skin, so its inputs cannot be traded up.");
        }

        return Validated<Contract>.Ok(new Contract(
            inputs.ToArray().AsReadOnly(),
            rarity,
            statTrak,
            cost,
            new ReadOnlyDictionary<string, int>(byCollection)));
    }

    /// <summary>
    /// Re-checks a contract against a catalog other than the one it was created with:
    /// every input skin known, every represented collection still has outputs.
    /// </summary>
    internal static ContractError? CheckAgainst(Contract contract, SkinCatalog catalog)
    {
        foreach (var input in contract.Inputs)
        {
            if (!catalog.TryGet(input.SkinId, out _))
            {
                return new ContractError(ContractErrorCode.UnknownSkin, $"Unknown skin '{input.SkinId}'.");
            }
        }

        foreach (var collectionId in contract.InputsByCollection.Keys)
        {
            if (catalog.OutputCount(collectionId, contract.OutputRarity) == 0)
            {
                return new ContractError(
                    ContractErrorCode.NoOutputsAvailable, $"Collection '{collectionId}' has no {contract.OutputRarity} skin.");
            }
        }

        return null;
    }

    private static Validated<Contract> Fail(ContractErrorCode code, string message) => Validated<Contract>.Fail(code, message);
}
