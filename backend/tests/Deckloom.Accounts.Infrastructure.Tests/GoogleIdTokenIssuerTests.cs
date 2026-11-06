using Deckloom.Accounts.Application;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-150 (RED): R-39 / REQ-006, REQ-007, REQ-009 (design, "S - Ataques ao OAuth"; decisao do usuario de 2026-11-01).
// O `iss` aceito e exatamente o Auth:Google:Authority configurado (sem a barra final) ou o mesmo valor sem o esquema;
// comparacao ordinal exata, nunca prefixo ou Contains. Qualquer outro valor vira falha de validacao do id_token
// (estagio IdToken, motivo ProviderError), sem identidade. aud, assinatura, exp e nonce seguem exigidos
// (cobertos em GoogleOidcFailureTests).
// Lacunas assumidas: (1) o Authority de teste e o endereco do StubGoogleOidcServer (http://127.0.0.1:<porta>/), entao as
// duas formas derivadas sao "http://127.0.0.1:<porta>" e "127.0.0.1:<porta>"; as formas literais do Google
// (accounts.google.com) nao sao exercitaveis contra o stub; (2) `iss` com barra final nao esta entre os dois valores
// derivados e, pela comparacao exata, e recusado.
public sealed class GoogleIdTokenIssuerTests : IAsyncLifetime, IDisposable
{
    private const string Code = "auth-code-123";
    private const string CodeVerifier = "verifier-abcdefghijklmnopqrstuvwxyz0123456789-._~ABC";
    private const string Nonce = "nonce-xyz789";

    private StubGoogleOidcServer _stub = null!;
    private HttpClient _http = null!;

    public static TheoryData<string> RefusedKinds =>
    [
        "other-host",
        "other-host-no-scheme",
        "https-scheme",
        "trailing-slash",
        "prefix-extension",
        "suffix-domain",
        "subpath",
        "empty",
        "scheme-only",
    ];

    public Task InitializeAsync()
    {
        _stub = StubGoogleOidcServer.Start();
        _http = new HttpClient();
        _stub.Nonce = Nonce;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _stub.DisposeAsync();

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task R39_iss_equal_to_the_configured_authority_with_scheme_is_accepted()
    {
        _stub.IssuerOverride = WithScheme();

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        Assert.Null(outcome.Failure);
        Assert.NotNull(outcome.Identity);
    }

    [Fact]
    public async Task R39_iss_equal_to_the_configured_authority_without_the_scheme_is_accepted()
    {
        _stub.IssuerOverride = WithoutScheme();

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        Assert.Null(outcome.Failure);
        Assert.NotNull(outcome.Identity);
    }

    [Theory]
    [MemberData(nameof(RefusedKinds))]
    public async Task R39_any_other_iss_is_refused_as_an_id_token_provider_error(string kind)
    {
        _stub.IssuerOverride = IssuerFor(kind);

        var outcome = await NewClient().TryCompleteAsync(Code, CodeVerifier, Nonce);

        Assert.Null(outcome.Identity);
        Assert.NotNull(outcome.Failure);
        Assert.Equal(GoogleAuthFailureStage.IdToken, outcome.Failure.Stage);
        Assert.Equal(GoogleAuthFailureReason.ProviderError, outcome.Failure.Reason);
    }

    // Funcao pura (ainda inexistente: o RED falha por compilacao): o conjunto exato de `iss` aceitos para um Authority.
    // Cobre o Authority padrao do Google sem depender do Google real.
    [Theory]
    [InlineData("https://accounts.google.com/", "https://accounts.google.com", "accounts.google.com")]
    [InlineData("https://accounts.google.com", "https://accounts.google.com", "accounts.google.com")]
    [InlineData("http://127.0.0.1:5000/", "http://127.0.0.1:5000", "127.0.0.1:5000")]
    public void R39_accepted_issuers_are_exactly_the_authority_without_trailing_slash_and_without_scheme(
        string authority, string withScheme, string withoutScheme)
    {
        var issuers = GoogleIdTokenValidator.AcceptedIssuers(new Uri(authority));

        Assert.Equal(
            new[] { withScheme, withoutScheme }.Order(StringComparer.Ordinal),
            issuers.Order(StringComparer.Ordinal));
    }

    private string WithScheme() => _stub.Authority.AbsoluteUri.TrimEnd('/');

    private string WithoutScheme() => WithScheme()["http://".Length..];

    private string IssuerFor(string kind) => kind switch
    {
        "other-host" => "http://evil.example.test",
        "other-host-no-scheme" => "evil.example.test",
        "https-scheme" => "https://" + WithoutScheme(),
        "trailing-slash" => WithScheme() + "/",
        "prefix-extension" => WithScheme() + "0",
        "suffix-domain" => WithoutScheme() + ".evil.example.test",
        "subpath" => WithScheme() + "/extra",
        "empty" => string.Empty,
        "scheme-only" => "http://",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private GoogleOidcClient NewClient()
    {
        var client = new GoogleOidcClient(
            _http,
            new GoogleOidcOptions
            {
                Authority = _stub.Authority,
                ClientId = _stub.ClientId,
                ClientSecret = _stub.ClientSecret,
                PublicOrigin = "https://deckloom.example.test",
            },
            TimeProvider.System);
        client.LoadDiscoveryAsync().GetAwaiter().GetResult();
        return client;
    }
}
