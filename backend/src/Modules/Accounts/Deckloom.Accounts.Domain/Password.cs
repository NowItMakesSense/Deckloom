using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class Password
{
    public const int MinLength = 8;
    public const int MaxLength = 128;
    public const string RequiredCode = "required";
    public const string TooShortCode = "too_short";
    public const string TooLongCode = "too_long";
    public const string TooCommonCode = "too_common";

    private const string Mask = "********";

    private readonly string _plaintext;

    private Password(string plaintext) => _plaintext = plaintext;

    public string Reveal() => _plaintext;

    public override string ToString() => Mask;

    public static Result<Password> Create(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Result<Password>.Failure(new DomainError(RequiredCode));
        }

        var length = input.EnumerateRunes().Count();

        if (length < MinLength)
        {
            return Result<Password>.Failure(new DomainError(TooShortCode));
        }

        return length > MaxLength
            ? Result<Password>.Failure(new DomainError(TooLongCode))
            : Result<Password>.Success(new Password(input));
    }
}
