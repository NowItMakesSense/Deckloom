using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record TermsAcceptance
{
    public const string AcceptTermsField = "acceptTerms";
    public const string AdultDeclarationField = "adultDeclaration";
    public const string RequiredCode = "required";
    public const string VersionMismatchCode = "version_mismatch";

    private TermsAcceptance(string termsVersion, DateTimeOffset acceptedAt, bool adultDeclared)
    {
        TermsVersion = termsVersion;
        AcceptedAt = acceptedAt;
        AdultDeclared = adultDeclared;
    }

    public string TermsVersion { get; }

    public DateTimeOffset AcceptedAt { get; }

    public bool AdultDeclared { get; }

    public static Result<TermsAcceptance> Create(
        string currentVersion,
        string? submittedVersion,
        bool adultDeclared,
        bool accepted,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        if (!accepted)
        {
            return Fail(RequiredCode, AcceptTermsField);
        }

        if (!string.Equals(currentVersion, submittedVersion, StringComparison.Ordinal))
        {
            return Fail(VersionMismatchCode, AcceptTermsField);
        }

        if (!adultDeclared)
        {
            return Fail(RequiredCode, AdultDeclarationField);
        }

        return Result<TermsAcceptance>.Success(new TermsAcceptance(currentVersion, now, adultDeclared));
    }

    private static Result<TermsAcceptance> Fail(string code, string field) =>
        Result<TermsAcceptance>.Failure(new DomainError(code, field));
}
