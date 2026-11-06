using System.Reflection;
using System.Web;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-068 (REQ-006, REQ-007, REQ-042): troca do codigo e validacao positiva do id_token contra o StubGoogleOidcServer
// (loopback em 127.0.0.1, sem Google real, sem rede externa, sem segredo real).
// Contrato esperado (a implementar na fase GREEN):
//   Infrastructure: GoogleOidcClient ganha
//                       GoogleOidcClient(HttpClient http, GoogleOidcOptions options, TimeProvider time, ILogger<GoogleOidcClient> logger)
//                       Task<GoogleVerifiedIdentity?> CompleteAsync(string code, string codeVerifier, string nonce, CancellationToken ct = default)
//                   O construtor ajusta http.Timeout para 5 s. POST no token endpoint do discovery com grant_type=authorization_code,
//                   code, code_verifier, client_id, client_secret e redirect_uri; valida o id_token (assinatura via JWKS, iss, aud, exp, nonce).
//   Application:    GoogleVerifiedIdentity (ja existe) nao tem campo de foto; auth_time vira AuthTime.
//   Testing:        StubGoogleOidcServer ganhou Nonce, Name, Picture, AuthTime (claims opcionais do id_token) e TokenRequestBodies.
// Os negativos de cada campo (assinatura, iss, aud, exp, nonce errados) ficam para T-069.
public sealed class GoogleOidcTokenExchangeTests : IAsyncLifetime, IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string Code = "auth-code-123";
    private const string CodeVerifier = "verifier-abcdefghijklmnopqrstuvwxyz0123456789-._~ABC";
    private const string Nonce = "nonce-xyz789";
    private const string PictureUrl = "https://lh3.googleusercontent.com/a/stub-photo-never-fetched";
    private const string AccessToken = "stub-access-token";

    private StubGoogleOidcServer _stub = null!;
    private HttpClient _http = null!;
    private readonly SpyLogger _log = new();

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
            TimeProvider.System,
            _log);
        client.LoadDiscoveryAsync().GetAwaiter().GetResult();
        return client;
    }

    [Fact]
    public void Stub_runs_only_on_loopback_so_no_external_host_is_contacted()
    {
        Assert.True(_stub.Authority.IsLoopback);
        Assert.Equal("http", _stub.Authority.Scheme);
    }

    [Fact]
    public async Task Req006_valid_code_and_verifier_return_the_verified_identity()
    {
        _stub.Name = "Maria Aparecida da Conceição de Albuquerque e Souza Pereira Nogueira Vasconcelos Ribeiro";
        _stub.AuthTime = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30);

        var identity = await NewClient().CompleteAsync(Code, CodeVerifier, Nonce);

        Assert.NotNull(identity);
        Assert.Equal("stub-subject", identity.Subject);
        Assert.Equal("stub@example.test", identity.Email.Value);
        Assert.True(identity.EmailVerified);
        Assert.Equal(_stub.Name, identity.Name);
        Assert.Equal(_stub.AuthTime, identity.AuthTime);
    }

    [Fact]
    public async Task Req006_token_request_sends_code_verifier_code_and_client_credentials_to_the_stub()
    {
        await NewClient().CompleteAsync(Code, CodeVerifier, Nonce);

        var body = Assert.Single(_stub.TokenRequestBodies);
        var form = HttpUtility.ParseQueryString(body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(Code, form["code"]);
        Assert.Equal(CodeVerifier, form["code_verifier"]);
        Assert.Equal(_stub.ClientId, form["client_id"]);
        Assert.Equal(_stub.ClientSecret, form["client_secret"]);
        Assert.Equal(PublicOrigin + "/api/v1/auth/google/callback", form["redirect_uri"]);
    }

    [Fact]
    public async Task Req042_auth_time_is_carried_and_is_null_when_google_omits_it()
    {
        var authTime = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 120);
        _stub.AuthTime = authTime;
        var withAuthTime = await NewClient().CompleteAsync(Code, CodeVerifier, Nonce);

        _stub.AuthTime = null;
        var withoutAuthTime = await NewClient().CompleteAsync(Code, CodeVerifier, Nonce);

        Assert.Equal(authTime, withAuthTime!.AuthTime);
        Assert.Null(withoutAuthTime!.AuthTime);
    }

    [Fact]
    public async Task Req022_google_photo_is_neither_read_nor_returned()
    {
        _stub.Picture = PictureUrl;
        _stub.Name = "Bia";

        var identity = await NewClient().CompleteAsync(Code, CodeVerifier, Nonce);

        Assert.NotNull(identity);
        var members = typeof(GoogleVerifiedIdentity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();
        Assert.DoesNotContain(members, n => n.Contains("Picture", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Photo", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Avatar", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PictureUrl, identity.ToString());
        Assert.DoesNotContain(_log.Text, text => text.Contains(PictureUrl, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Req006_google_photo_is_never_requested_from_any_host()
    {
        _stub.Picture = PictureUrl;
        var recorder = new RecordingHandler(new HttpClientHandler());
        using var http = new HttpClient(recorder);
        var client = new GoogleOidcClient(
            http,
            new GoogleOidcOptions
            {
                Authority = _stub.Authority,
                ClientId = _stub.ClientId,
                ClientSecret = _stub.ClientSecret,
                PublicOrigin = PublicOrigin,
            },
            TimeProvider.System,
            _log);

        await client.CompleteAsync(Code, CodeVerifier, Nonce);

        Assert.NotEmpty(recorder.Hosts);
        Assert.All(recorder.Hosts, host => Assert.Equal("127.0.0.1", host));
    }

    [Fact]
    public async Task Req007_id_token_access_token_and_client_secret_never_appear_in_logs_or_tostring()
    {
        _stub.Name = "Bia";

        var identity = await NewClient().CompleteAsync(Code, CodeVerifier, Nonce);

        Assert.NotNull(identity);
        var rendered = identity.ToString();
        Assert.DoesNotContain(AccessToken, rendered);
        Assert.DoesNotContain(_stub.ClientSecret, rendered);
        Assert.DoesNotContain("eyJ", rendered);
        Assert.All(_log.Text, text =>
        {
            Assert.DoesNotContain(AccessToken, text);
            Assert.DoesNotContain(_stub.ClientSecret, text);
            Assert.DoesNotContain(CodeVerifier, text);
            Assert.DoesNotContain(Code, text);
            Assert.DoesNotContain("eyJ", text);
        });
    }

    [Fact]
    public void Req006_client_sets_a_five_second_timeout_on_its_http_client()
    {
        using var http = new HttpClient();

        _ = new GoogleOidcClient(
            http,
            new GoogleOidcOptions
            {
                Authority = _stub.Authority,
                ClientId = _stub.ClientId,
                ClientSecret = _stub.ClientSecret,
                PublicOrigin = PublicOrigin,
            },
            TimeProvider.System,
            _log);

        Assert.Equal(TimeSpan.FromSeconds(5), http.Timeout);
    }

    [Fact]
    public async Task Req006_id_token_with_valid_signature_iss_aud_exp_and_nonce_is_accepted()
    {
        // Stub assina com RS256 e publica a chave no JWKS; iss/aud/exp validos e nonce igual ao esperado.
        var identity = await NewClient().CompleteAsync(Code, CodeVerifier, Nonce);

        Assert.NotNull(identity);
        Assert.Equal("stub-subject", identity.Subject);
    }

    private sealed class RecordingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly List<string> _hosts = [];

        public IReadOnlyList<string> Hosts => _hosts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _hosts.Add(request.RequestUri!.Host);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class SpyLogger : ILogger<GoogleOidcClient>
    {
        private readonly List<string> _text = [];

        public IReadOnlyList<string> Text => _text;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _text.Add(formatter(state, exception) + " " + exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                _text.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
            }
        }
    }
}
