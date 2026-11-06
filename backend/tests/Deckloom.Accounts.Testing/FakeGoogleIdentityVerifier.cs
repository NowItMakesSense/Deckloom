using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

public sealed class FakeGoogleIdentityVerifier : IGoogleIdentityVerifier
{
    private readonly List<CompleteCall> _completeCalls = [];
    private Func<GoogleVerifiedIdentity?>? _outcome;

    public TransactionProbe TransactionProbe { get; } = new();

    public IReadOnlyList<CompleteCall> CompleteCalls => _completeCalls;

    public void Returns(GoogleVerifiedIdentity identity) => _outcome = () => identity;

    public void Cancel() => _outcome = () => null;

    public void Fail() => _outcome = () => throw new GoogleVerificationFailedException();

    public Task<Uri> BuildAuthorizationUrlAsync(
        string state,
        string nonce,
        string codeChallenge,
        GoogleAuthIntent intent = GoogleAuthIntent.SignIn,
        CancellationToken ct = default)
    {
        var url =
            $"https://accounts.google.test/authorize?state={Uri.EscapeDataString(state)}" +
            $"&nonce={Uri.EscapeDataString(nonce)}&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            "&code_challenge_method=S256";
        if (intent == GoogleAuthIntent.Reauth)
        {
            url += "&prompt=login&max_age=300";
        }

        return Task.FromResult(new Uri(url));
    }

    public Task<GoogleVerifiedIdentity?> CompleteAsync(
        string code,
        string codeVerifier,
        string nonce,
        CancellationToken ct = default)
    {
        TransactionProbe.Record("Complete");
        _completeCalls.Add(new CompleteCall(code, codeVerifier, nonce));
        if (_outcome is null)
        {
            throw new InvalidOperationException("FakeGoogleIdentityVerifier has no programmed outcome.");
        }

        return Task.FromResult(_outcome());
    }
}

public sealed record CompleteCall(string Code, string CodeVerifier, string Nonce);
