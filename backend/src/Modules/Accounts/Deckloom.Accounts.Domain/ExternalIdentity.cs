using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record ExternalIdentity
{
    public const string GoogleProvider = "google";
    public const string InvalidCode = "invalid";
    public const string RequiredCode = "required";

    private ExternalIdentity(string provider, string subject)
    {
        Provider = provider;
        Subject = subject;
    }

    public string Provider { get; }

    public string Subject { get; }

    public static Result<ExternalIdentity> Create(string? provider, string? subject)
    {
        if (!string.Equals(provider, GoogleProvider, StringComparison.Ordinal))
        {
            return Result<ExternalIdentity>.Failure(new DomainError(InvalidCode));
        }

        return string.IsNullOrWhiteSpace(subject)
            ? Result<ExternalIdentity>.Failure(new DomainError(RequiredCode))
            : Result<ExternalIdentity>.Success(new ExternalIdentity(provider!, subject));
    }
}
