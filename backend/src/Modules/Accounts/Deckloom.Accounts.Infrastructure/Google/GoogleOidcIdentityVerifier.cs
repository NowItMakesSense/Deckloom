using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

// Adapter da porta IGoogleIdentityVerifier sobre GoogleOidcClient. Cancelamento/negacao no Google vira nulo;
// qualquer outra falha vira GoogleVerificationFailedException (detalhes ficam no log do cliente).
public sealed class GoogleOidcIdentityVerifier(GoogleOidcClient client) : IGoogleIdentityVerifier
{
    public async Task<Uri> BuildAuthorizationUrlAsync(
        string state,
        string nonce,
        string codeChallenge,
        GoogleAuthIntent intent = GoogleAuthIntent.SignIn,
        CancellationToken ct = default)
    {
        await client.LoadDiscoveryAsync(ct).ConfigureAwait(false);
        var mapped = intent == GoogleAuthIntent.Reauth ? GoogleAuthorizationIntent.Reauth : GoogleAuthorizationIntent.SignIn;
        return client.BuildAuthorizationUrl(state, nonce, codeChallenge, mapped);
    }

    public async Task<GoogleVerifiedIdentity?> CompleteAsync(
        string code, string codeVerifier, string nonce, CancellationToken ct = default)
    {
        var outcome = await client.TryCompleteAsync(code, codeVerifier, nonce, ct: ct).ConfigureAwait(false);
        if (outcome.Failure is null)
        {
            return outcome.Identity;
        }

        return outcome.Failure.Reason == GoogleAuthFailureReason.Cancelled
            ? null
            : throw new GoogleVerificationFailedException();
    }
}
