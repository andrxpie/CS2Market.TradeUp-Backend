using System.Diagnostics.CodeAnalysis;

namespace Cs2Market.TradeUp;

public enum ContractErrorCode
{
    WrongInputCount,
    MixedRarity,
    MixedStatTrak,
    SouvenirInput,
    CovertInput,
    UnknownSkin,
    NoOutputsAvailable,
    FloatOutOfRange,
    UnpricedInput,
}

public sealed record ContractError(ContractErrorCode Code, string Message);

/// <summary>
/// The result of an operation whose rejection is expected rather than exceptional.
/// No exceptions for expected rejections; <see cref="ValueOrThrow"/> exists for tests.
/// <c>default(Validated&lt;T&gt;)</c> is neither valid nor carries an error.
/// </summary>
public readonly record struct Validated<T>
{
    private Validated(T value)
    {
        IsValid = true;
        Value = value;
    }

    private Validated(ContractError error)
    {
        Error = error;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsValid { get; }

    public T? Value { get; }

    public ContractError? Error { get; }

    // CA1000: the static factories are the fixed shape in docs/M1_DOMAIN_API.md § Validation.
#pragma warning disable CA1000
    public static Validated<T> Ok(T value) => new(value);

    public static Validated<T> Fail(ContractErrorCode code, string message) => new(new ContractError(code, message));
#pragma warning restore CA1000

    /// <exception cref="InvalidOperationException">The result is a rejection.</exception>
    public T ValueOrThrow() => IsValid
        ? Value
        : throw new InvalidOperationException(Error is null ? "Uninitialized result." : $"{Error.Code}: {Error.Message}");
}
