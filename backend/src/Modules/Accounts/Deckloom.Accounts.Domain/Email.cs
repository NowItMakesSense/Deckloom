using System.Globalization;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record Email
{
    public const int MaxLength = 254;
    public const string RequiredCode = "required";
    public const string InvalidFormatCode = "invalid_format";

    private const char AtSign = '@';
    private const char DomainSeparator = '.';

    private Email(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => "***";

    public static Result<Email> Create(string? input)
    {
        var normalized = input?.Trim().ToLower(CultureInfo.InvariantCulture);

        if (string.IsNullOrEmpty(normalized))
        {
            return Result<Email>.Failure(new DomainError(RequiredCode));
        }

        return IsValidFormat(normalized)
            ? Result<Email>.Success(new Email(normalized))
            : Result<Email>.Failure(new DomainError(InvalidFormatCode));
    }

    private static bool IsValidFormat(string value)
    {
        if (value.Length > MaxLength)
        {
            return false;
        }

        if (value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var parts = value.Split(AtSign);
        if (parts.Length != 2 || parts[0].Length == 0)
        {
            return false;
        }

        var labels = parts[1].Split(DomainSeparator);
        return labels.Length >= 2 && labels.All(label => label.Length > 0);
    }
}
