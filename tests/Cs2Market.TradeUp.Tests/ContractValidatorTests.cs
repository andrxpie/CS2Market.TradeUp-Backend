using Cs2Market.TradeUp.Tests.Fixtures;

namespace Cs2Market.TradeUp.Tests;

public sealed class ContractValidatorTests
{
    private static readonly SkinCatalog Catalog = SyntheticCatalog.Create();

    /// <summary>G7: one row per rejection; an expected rejection never throws.</summary>
    [Theory]
    [InlineData("nine inputs", ContractErrorCode.WrongInputCount)]
    [InlineData("eleven inputs", ContractErrorCode.WrongInputCount)]
    [InlineData("no inputs", ContractErrorCode.WrongInputCount)]
    [InlineData("mixed rarity", ContractErrorCode.MixedRarity)]
    [InlineData("mixed StatTrak", ContractErrorCode.MixedStatTrak)]
    [InlineData("souvenir input", ContractErrorCode.SouvenirInput)]
    [InlineData("covert inputs", ContractErrorCode.CovertInput)]
    [InlineData("unknown skin", ContractErrorCode.UnknownSkin)]
    [InlineData("knife input", ContractErrorCode.UnknownSkin)]
    [InlineData("collection without outputs", ContractErrorCode.NoOutputsAvailable)]
    [InlineData("float outside its wear band", ContractErrorCode.FloatOutOfRange)]
    [InlineData("float outside the skin range", ContractErrorCode.FloatOutOfRange)]
    [InlineData("float is NaN", ContractErrorCode.FloatOutOfRange)]
    [InlineData("zero price", ContractErrorCode.UnpricedInput)]
    public void Create_InvalidContract_ReturnsErrorCode(string scenario, ContractErrorCode expected)
    {
        var result = Contract.Create(Scenario(scenario), Catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Equal(expected, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Throws<InvalidOperationException>(() => result.ValueOrThrow());
    }

    [Fact]
    public void Create_ValidContract_ExposesItsShape()
    {
        var contract = ContractBuilder.New(Catalog).From("alpha", 3).From("filler1", 7).AtFloat(0.10).Build();

        Assert.Equal(Rarity.MilSpec, contract.InputRarity);
        Assert.Equal(Rarity.Restricted, contract.OutputRarity);
        Assert.False(contract.StatTrak);
        Assert.Equal(new Cents(1000), contract.Cost);
        Assert.Equal([("alpha", 3), ("filler1", 7)], contract.InputsByCollection.Select(static pair => (pair.Key, pair.Value)));
        Assert.Equal(10, contract.Inputs.Count);
    }

    [Fact]
    public void Create_AllStatTrak_IsValid()
    {
        var contract = ContractBuilder.New(Catalog).From("alpha", 10).StatTrak().Priced(SyntheticSnapshot.Create()).Build();

        Assert.True(contract.StatTrak);
        Assert.Equal(new Cents(2000), contract.Cost);
    }

    [Fact]
    public void Create_InputsAreCopied_SoLaterMutationCannotChangeTheContract()
    {
        var inputs = ContractBuilder.New(Catalog).From("alpha", 10).Inputs();
        var contract = Contract.Create(inputs, Catalog).ValueOrThrow();

        inputs[0] = inputs[0] with { Price = new Cents(1) };

        Assert.Equal(new Cents(1000), contract.Cost);
        Assert.Equal(new Cents(100), contract.Inputs[0].Price);
    }

    [Fact]
    public void Create_FloatAtTheSkinsOwnCap_IsValid()
    {
        var capped = new Skin("cap-in", "cap-in", Rarity.MilSpec, 0.0, 0.08, ["cap"], false, false);
        var catalog = new SkinCatalog([capped, SyntheticCatalog.Skin("cap-out", Rarity.Restricted, "cap")]);
        var inputs = Enumerable.Repeat(new ContractInput("cap-in", Wear.MinimalWear, 0.08, false, new Cents(5)), 10).ToArray();

        Assert.True(Contract.Create(inputs, catalog).IsValid);
        Assert.Equal(
            ContractErrorCode.FloatOutOfRange,
            Contract.Create(inputs.Select(static input => input with { Float = 0.0801 }).ToArray(), catalog).Error!.Code);
    }

    [Fact]
    public void Create_NullArguments_Throw()
    {
        var inputs = ContractBuilder.New(Catalog).From("alpha", 10).Inputs();

        Assert.Throws<ArgumentNullException>(() => Contract.Create(null!, Catalog));
        Assert.Throws<ArgumentNullException>(() => Contract.Create(inputs, null!));

        inputs[4] = null!;
        Assert.Throws<ArgumentException>(() => Contract.Create(inputs, Catalog));
    }

    [Fact]
    public void Validated_Default_IsNeitherValidNorAnError()
    {
        var result = default(Validated<Contract>);

        Assert.False(result.IsValid);
        Assert.Null(result.Error);
        Assert.Throws<InvalidOperationException>(() => result.ValueOrThrow());
    }

    private static List<ContractInput> Scenario(string scenario)
    {
        var valid = ContractBuilder.New(Catalog).From("alpha", 3).From("filler1", 7).AtFloat(0.20).Inputs();

        switch (scenario)
        {
            case "nine inputs":
                return valid.Take(9).ToList();
            case "eleven inputs":
                return [.. valid, valid[0]];
            case "no inputs":
                return [];
            case "mixed rarity":
                valid[9] = valid[9] with { SkinId = "alpha-out-1" };
                return valid;
            case "mixed StatTrak":
                valid[9] = valid[9] with { StatTrak = true };
                return valid;
            case "souvenir input":
                valid[0] = valid[0] with { Souvenir = true };
                return valid;
            case "covert inputs":
                return ContractBuilder.New(Catalog).From("apex", 10).OfRarity(Rarity.Covert).Inputs();
            case "unknown skin":
                valid[5] = valid[5] with { SkinId = "does-not-exist" };
                return valid;
            case "knife input":
                valid[5] = valid[5] with { SkinId = "karambit-fade", Float = 0.05, Wear = Wear.FactoryNew };
                return valid;
            case "collection without outputs":
                return ContractBuilder.New(Catalog).From("alpha", 5).From("barren", 5).Inputs();
            case "float outside its wear band":
                valid[2] = valid[2] with { Wear = Wear.FactoryNew };
                return valid;
            case "float outside the skin range":
                return ContractBuilder.New(Catalog).From("capped", 10).OfRarity(Rarity.Restricted).AtFloat(0.7).Inputs();
            case "float is NaN":
                valid[2] = valid[2] with { Float = double.NaN };
                return valid;
            case "zero price":
                valid[7] = valid[7] with { Price = Cents.Zero };
                return valid;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
        }
    }
}
