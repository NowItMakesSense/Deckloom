using System.Collections.Specialized;
using System.Web;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-067 (REQ-006, REQ-042): URL de autorizacao do GoogleOidcClient contra o StubGoogleOidcServer (loopback, sem Google real).
// Contrato esperado (a implementar na fase GREEN):
//   Testing:        StubGoogleOidcServer.Start() : StubGoogleOidcServer (IAsyncDisposable) com Authority (Uri loopback, discovery em
//                   {Authority}/.well-known/openid-configuration), ClientId, ClientSecret, AuthorizationEndpoint.
//   Infrastructure: sealed class GoogleOidcOptions { Uri Authority; string ClientId; string ClientSecret; string PublicOrigin }
//                   enum GoogleAuthorizationIntent { SignIn, Reauth }
//                   sealed class GoogleOidcClient(HttpClient http, GoogleOidcOptions options)
//                       Uri BuildAuthorizationUrl(string state, string nonce, string codeChallenge,
//                                                 GoogleAuthorizationIntent intent = GoogleAuthorizationIntent.SignIn)
//   state, nonce e codeChallenge vem do caso de uso; o cliente NAO os gera (aleatoriedade e do caso de uso, T-038/T-039).
//   Nenhum teste chama host externo: a unica URL base usada e a do stub em 127.0.0.1.
public sealed class GoogleOidcAuthorizationUrlTests : IAsyncLifetime, IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string State = "state-abc123";
    private const string Nonce = "nonce-xyz789";
    private const string CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string ExpectedRedirectUri = PublicOrigin + "/api/v1/auth/google/callback";

    private StubGoogleOidcServer _stub = null!;
    private HttpClient _http = null!;

    public Task InitializeAsync()
    {
        _stub = StubGoogleOidcServer.Start();
        _http = new HttpClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _stub.DisposeAsync();

    public void Dispose() => _http.Dispose();

    // Setup: o endpoint de autorizacao vem do discovery (async); BuildAuthorizationUrl e sincrono e usa o cache.
    private GoogleOidcClient NewClient(string publicOrigin = PublicOrigin)
    {
        var client = new GoogleOidcClient(_http, new GoogleOidcOptions
        {
            Authority = _stub.Authority,
            ClientId = _stub.ClientId,
            ClientSecret = _stub.ClientSecret,
            PublicOrigin = publicOrigin,
        });
        client.LoadDiscoveryAsync().GetAwaiter().GetResult();
        return client;
    }

    private Uri Build(GoogleAuthorizationIntent intent = GoogleAuthorizationIntent.SignIn, string publicOrigin = PublicOrigin) =>
        NewClient(publicOrigin).BuildAuthorizationUrl(State, Nonce, CodeChallenge, intent);

    private static NameValueCollection Query(Uri url) => HttpUtility.ParseQueryString(url.Query);

    [Fact]
    public void Stub_runs_only_on_loopback_so_no_external_host_is_contacted()
    {
        Assert.True(_stub.Authority.IsLoopback);
        Assert.Equal("http", _stub.Authority.Scheme);
    }

    [Fact]
    public void Req006_signin_url_has_code_flow_openid_scopes_state_nonce_and_client_id()
    {
        var q = Query(Build());

        Assert.Equal("code", q["response_type"]);
        Assert.Equal("openid email profile", q["scope"]);
        Assert.Equal(_stub.ClientId, q["client_id"]);
        Assert.Equal(State, q["state"]);
        Assert.Equal(Nonce, q["nonce"]);
    }

    [Fact]
    public void Req006_authorization_endpoint_comes_from_the_stub_discovery_document()
    {
        var url = Build();

        Assert.Equal(_stub.AuthorizationEndpoint.GetLeftPart(UriPartial.Path), url.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public void Req006_signin_url_uses_the_given_pkce_s256_code_challenge()
    {
        var q = Query(Build());

        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal(CodeChallenge, q["code_challenge"]);
    }

    [Fact]
    public void Req006_redirect_uri_is_fixed_from_auth_public_origin()
    {
        Assert.Equal(ExpectedRedirectUri, Query(Build())["redirect_uri"]);

        var other = Build(publicOrigin: "https://outra.example.test/");
        Assert.Equal("https://outra.example.test/api/v1/auth/google/callback", Query(other)["redirect_uri"]);
    }

    [Fact]
    public void Req006_default_intent_is_signin_without_login_prompt_nor_max_age()
    {
        var implicitDefault = Query(NewClient().BuildAuthorizationUrl(State, Nonce, CodeChallenge));
        var explicitSignIn = Query(Build(GoogleAuthorizationIntent.SignIn));

        Assert.Null(implicitDefault["prompt"]);
        Assert.Null(implicitDefault["max_age"]);
        Assert.Null(explicitSignIn["prompt"]);
        Assert.Null(explicitSignIn["max_age"]);
    }

    [Fact]
    public void Req042_reauth_intent_adds_prompt_login_and_max_age_300()
    {
        var q = Query(Build(GoogleAuthorizationIntent.Reauth));

        Assert.Equal("login", q["prompt"]);
        Assert.Equal("300", q["max_age"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal(State, q["state"]);
        Assert.Equal(Nonce, q["nonce"]);
        Assert.Equal("S256", q["code_challenge_method"]);
    }

    [Fact]
    public void Req006_client_secret_never_appears_in_the_authorization_url()
    {
        Assert.DoesNotContain(_stub.ClientSecret, Build().ToString());
        Assert.DoesNotContain(_stub.ClientSecret, Build(GoogleAuthorizationIntent.Reauth).ToString());
    }
}
