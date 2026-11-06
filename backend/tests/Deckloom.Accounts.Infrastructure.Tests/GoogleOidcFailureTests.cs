using System.Diagnostics;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-069 (REQ-009, REQ-042): falhas, negativos e degradacao do GoogleOidcClient contra o StubGoogleOidcServer
// (loopback em 127.0.0.1, sem Google real, sem rede externa, sem segredo real). Fase RED.
// Contrato esperado (a implementar na fase GREEN; nomes sugeridos, o catalogo e o evento 1010 de design.md):
//   Application:    enum GoogleAuthFailureStage  { Authorize, Callback, Token, IdToken }
//                   enum GoogleAuthFailureReason { Cancelled, StateMismatch, UnverifiedEmail, ProviderError, Timeout, SubjectMismatch }
//                   sealed record GoogleAuthFailed(GoogleAuthFailureStage Stage, GoogleAuthFailureReason Reason)
//                   sealed record GoogleOidcOutcome(GoogleVerifiedIdentity? Identity, GoogleAuthFailed? Failure)
//   Infrastructure: GoogleOidcClient ganha
//                       Task<GoogleOidcOutcome> TryCompleteAsync(string code, string codeVerifier, string nonce,
//                           GoogleAuthorizationIntent intent = GoogleAuthorizationIntent.SignIn, CancellationToken ct = default)
//                   que NUNCA lanca (exceto cancelamento do chamador): toda falha de provedor, de rede ou de validacao do
//                   id_token vira GoogleOutcome.Failure tipado. Estagio: troca do codigo = Token; validacao do id_token e
//                   busca do JWKS = IdToken. Troca do codigo sem retry (uso unico); JWKS com 1 retry (2 tentativas no total).
//   Os tipos acima NAO existem ainda: o RED falha por compilacao (tipo/metodo ausente). A porta
//   IGoogleIdentityVerifier.CompleteAsync (que lanca GoogleVerificationFailedException) fica inalterada.
public sealed class GoogleOidcFailureTests : IAsyncLifetime, IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string Code = "auth-code-123";
    private const string CodeVerifier = "verifier-abcdefghijklmnopqrstuvwxyz0123456789-._~ABC";
    private const string Nonce = "nonce-xyz789";

    private StubGoogleOidcServer _stub = null!;
    private HttpClient _http = null!;

    public Task InitializeAsync()
    {
        _stub = StubGoogleOidcServer.Start();
        _http = new HttpClient();
        _stub.Nonce = Nonce;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _stub.DisposeAsync();

    public void Dispose() => _http.Dispose();

    private GoogleOidcClient NewClient()
    {
        var client = new GoogleOidcClient(
            _http,
            new GoogleOidcOptions
            {
                Authority = _stub.Authority,
                ClientId = _stub.ClientId,
                ClientSecret = _stub.ClientSecret,
                PublicOrigin = PublicOrigin,
            },
            TimeProvider.System);
        client.LoadDiscoveryAsync().GetAwaiter().GetResult();
        return client;
    }

    private static void AssertFailed(
        GoogleOidcOutcome outcome, GoogleAuthFailureStage stage, GoogleAuthFailureReason reason)
    {
        Assert.Null(outcome.Identity);
        Assert.NotNull(outcome.Failure);
        Assert.Equal(stage, outcome.Failure.Stage);
        Assert.Equal(reason, outcome.Failure.Reason);
    }

    [Fact]
    public async Task Req009_invalid_signature_becomes_typed_id_token_failure_without_throwing()
    {
        _stub.CorruptSignature = true;

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
    }

    [Fact]
    public async Task Req009_wrong_nonce_becomes_typed_id_token_failure_without_throwing()
    {
        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, "another-nonce");

        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
    }

    [Fact]
    public async Task Req009_wrong_audience_becomes_typed_id_token_failure_without_throwing()
    {
        _stub.AudienceOverride = "someone-elses-client-id";

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
    }

    [Fact]
    public async Task Req009_wrong_issuer_becomes_typed_id_token_failure_without_throwing()
    {
        _stub.IssuerOverride = "https://evil.example.test";

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
    }

    [Fact]
    public async Task Req009_expired_id_token_becomes_typed_id_token_failure_without_throwing()
    {
        _stub.ExpiresIn = TimeSpan.FromHours(-1);

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
    }

    [Fact]
    public async Task Req009_unverified_email_becomes_typed_id_token_failure_without_throwing()
    {
        _stub.EmailVerified = false;

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.UnverifiedEmail);
    }

    [Fact]
    public async Task Req042_stale_auth_time_on_reauth_becomes_typed_id_token_failure_without_throwing()
    {
        _stub.AuthTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        var outcome = await NewClient().TryCompleteAsync(
            Code, CodeVerifier, Nonce, GoogleAuthorizationIntent.Reauth);

        // Motivo para auth_time antigo nao consta do catalogo; ProviderError e a escolha provisoria (lacuna reportada).
        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
    }

    [Fact]
    public async Task Req042_recent_auth_time_on_reauth_is_accepted()
    {
        _stub.AuthTime = DateTimeOffset.UtcNow.AddSeconds(-30);

        var outcome = await NewClient().TryCompleteAsync(
            Code, CodeVerifier, Nonce, GoogleAuthorizationIntent.Reauth);

        Assert.Null(outcome.Failure);
        Assert.NotNull(outcome.Identity);
    }

    [Fact]
    public async Task Req009_access_denied_from_token_endpoint_becomes_cancelled_without_throwing()
    {
        _stub.TokenError = "access_denied";

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.Token, GoogleAuthFailureReason.Cancelled);
    }

    [Fact]
    public async Task Req009_token_endpoint_5xx_becomes_provider_error_without_retry()
    {
        _stub.TokenStatus = 503;

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.Token, GoogleAuthFailureReason.ProviderError);
        Assert.Single(_stub.TokenRequestBodies);
    }

    [Fact]
    public async Task Req009_token_endpoint_slower_than_five_seconds_becomes_timeout_without_retry()
    {
        _stub.TokenDelay = TimeSpan.FromSeconds(10);
        var client = NewClient();

        var clock = Stopwatch.StartNew();
        var outcome = await client.TryCompleteAsync(Code, CodeVerifier, Nonce);
        clock.Stop();

        AssertFailed(outcome, GoogleAuthFailureStage.Token, GoogleAuthFailureReason.Timeout);
        Assert.InRange(clock.Elapsed.TotalSeconds, 4.5, 7.0);
        Assert.Single(_stub.TokenRequestBodies);
    }

    [Fact]
    public async Task Req009_jwks_failing_once_is_retried_once_and_the_identity_is_returned()
    {
        _stub.JwksFailuresBeforeSuccess = 1;

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        Assert.Null(outcome.Failure);
        Assert.NotNull(outcome.Identity);
        Assert.Equal(2, _stub.JwksRequestCount);
    }

    [Fact]
    public async Task Req009_jwks_failing_twice_gives_up_after_one_retry_as_provider_error()
    {
        _stub.JwksFailuresBeforeSuccess = 5;

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        AssertFailed(outcome, GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
        Assert.Equal(2, _stub.JwksRequestCount);
    }

    [Fact]
    public async Task Req009_code_exchange_is_single_use_and_the_second_exchange_fails_typed_without_retry()
    {
        _stub.EnforceSingleUseCodes = true;
        var client = NewClient();

        var first = await client.TryCompleteAsync(Code, CodeVerifier, Nonce);
        var second = await client.TryCompleteAsync(Code, CodeVerifier, Nonce);

        Assert.NotNull(first.Identity);
        AssertFailed(second, GoogleAuthFailureStage.Token, GoogleAuthFailureReason.ProviderError);
        Assert.Equal(2, _stub.TokenRequestBodies.Count);
    }

    [Fact]
    public void Req009_failure_reasons_follow_the_catalog_enum()
    {
        Assert.Equal(
            ["Cancelled", "StateMismatch", "UnverifiedEmail", "ProviderError", "Timeout", "SubjectMismatch"],
            Enum.GetNames<GoogleAuthFailureReason>());
        Assert.Equal(
            ["Authorize", "Callback", "Token", "IdToken"],
            Enum.GetNames<GoogleAuthFailureStage>());
    }

    // Lacuna: "GoogleOidcClient indisponivel nao afeta LoginWithPassword" (teste de composicao) nao tem nivel onde
    // exista composicao: LoginWithPassword (Application) nao depende de IGoogleIdentityVerifier, e nao ha composition
    // root/DI testavel na Infrastructure ainda. Nao inventado; ver relatorio.
}
