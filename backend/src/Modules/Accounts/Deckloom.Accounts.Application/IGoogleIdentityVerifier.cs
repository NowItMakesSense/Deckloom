using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public interface IGoogleIdentityVerifier
{
    Task<Uri> BuildAuthorizationUrlAsync(
        string state,
        string nonce,
        string codeChallenge,
        GoogleAuthIntent intent = GoogleAuthIntent.SignIn,
        CancellationToken ct = default);

    // Null means the player cancelled or denied consent at Google; provider/validation failures throw.
    Task<GoogleVerifiedIdentity?> CompleteAsync(
        string code,
        string codeVerifier,
        string nonce,
        CancellationToken ct = default);
}

public sealed record GoogleVerifiedIdentity(string Subject, Email Email, bool EmailVerified,
    string? Name,
    DateTimeOffset? AuthTime = null);

public sealed class GoogleVerificationFailedException : Exception
{
    public GoogleVerificationFailedException()
        : base("Google identity verification failed.")
    {
    }
}
