using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class Bio : IEquatable<Bio>
{
    public const int MaxLength = 280;
    public const string TooLongCode = "too_long";
    public const string InvalidCharactersCode = "invalid_characters";

    private Bio(string value) => Value = value;

    public string Value { get; }

    public bool Equals(Bio? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Bio);

    public static bool operator ==(Bio? left, Bio? right) => Equals(left, right);

    public static bool operator !=(Bio? left, Bio? right) => !Equals(left, right);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static Result<Bio?> Create(string? input)
    {
        if (input is null)
        {
            return Result<Bio?>.Success(null);
        }

        var text = TextRules.NormalizeLineBreaks(input);

        if (TextRules.ContainsControlCharacterExceptLineFeed(text))
        {
            return Result<Bio?>.Failure(new DomainError(InvalidCharactersCode));
        }

        var value = TextRules.NormalizeAndTrim(text);

        if (value.Length == 0)
        {
            return Result<Bio?>.Success(null);
        }

        return TextRules.LengthInTextElements(value) > MaxLength
            ? Result<Bio?>.Failure(new DomainError(TooLongCode))
            : Result<Bio?>.Success(new Bio(value));
    }
}
