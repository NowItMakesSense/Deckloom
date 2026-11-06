using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class DisplayName : IEquatable<DisplayName>
{
    public const int MinLength = 3;
    public const int MaxLength = 30;
    public const string LengthCode = "length";
    public const string InvalidCharactersCode = "invalid_characters";

    private DisplayName(string value) => Value = value;

    public string Value { get; }

    public bool Equals(DisplayName? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DisplayName);

    public static bool operator ==(DisplayName? left, DisplayName? right) => Equals(left, right);

    public static bool operator !=(DisplayName? left, DisplayName? right) => !Equals(left, right);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static Result<DisplayName> Create(string? input)
    {
        if (input is null)
        {
            return Result<DisplayName>.Failure(new DomainError(LengthCode));
        }

        if (TextRules.ContainsControlCharacter(input))
        {
            return Result<DisplayName>.Failure(new DomainError(InvalidCharactersCode));
        }

        var value = TextRules.NormalizeAndTrim(input);
        var length = TextRules.LengthInTextElements(value);

        return length is < MinLength or > MaxLength
            ? Result<DisplayName>.Failure(new DomainError(LengthCode))
            : Result<DisplayName>.Success(new DisplayName(value));
    }
}
