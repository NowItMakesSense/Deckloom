using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// T-083 (RED): POST /api/v1/auth/login, GET /api/v1/auth/session, POST /api/v1/auth/logout
// (design, tabela "Endpoints"; REQ-004, 005, 010, 011, 012, 027, 032, 035, 038).
// Lacunas assumidas: (1) a spec NAO fixa 429 nem bloqueio: o rate limit e so atraso progressivo (ADR-0005), verificado
// pelo RecordingDelayer e por login correto ainda funcionando apos muitas falhas; (2) o host deve compor
// LoginWithPassword a partir das portas registradas aqui; (3) o IP do cliente de teste e desconhecido ("unknown"),
// entao o atraso por IP e verificado com emails distintos; (4) "conta excluida" equivale a Jogador ausente no
// repositorio (a exclusao remove a linha), coberto pelo caso de email inexistente; (5) cabecalhos comparados sem
// Date e Content-Length, e corpo sem traceId, que variam por requisicao; (6) o host deve usar o TimeProvider
// registrado (relogio controlavel) para expirar a janela deslizante de 30 min.
public sealed class LoginSessionLogoutTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string LoginPath = "/api/v1/auth/login";
    private const string SessionPath = "/api/v1/auth/session";
    private const string LogoutPath = "/api/v1/auth/logout";
    private const string AnaEmail = "ana@example.com";
    private const string GoogleEmail = "gui@example.com";
    private const string AnaPassword = "senha-valida-123";

    private readonly LoginFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ004_login_with_valid_credentials_returns_200_session_summary_and_session_cookie()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("Ana", doc.RootElement.GetProperty("displayName").GetString());
        Assert.True(doc.RootElement.GetProperty("hasPassword").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("hasGoogle").GetBoolean());
        Assert.Equal("preset-01", doc.RootElement.GetProperty("avatar").GetProperty("presetId").GetString());
        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.True(cookie.Length > SessionCookie.Length + 2);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ012_login_without_remember_me_sets_cookie_without_max_age()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody(rememberMe: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("max-age", SessionSetCookie(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ012_login_with_remember_me_sets_thirty_day_max_age()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody(rememberMe: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=2592000", SessionSetCookie(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ040_login_response_has_only_summary_properties_without_email_or_hash_and_is_not_cacheable()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(["avatar", "displayName", "hasGoogle", "hasPassword"], PropertyNames(doc.RootElement));
        AssertNoSecrets(text);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ035_login_stores_the_session_by_token_hash_only()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = SessionSetCookie(response).Split(';')[0][(SessionCookie.Length + 1)..];
        var stored = await _factory.Sessions.FindByTokenHash(new Sha256TokenHasher().Hash(token));
        Assert.NotNull(stored);
        Assert.Equal(TimeSpan.FromMinutes(30), stored.ExpiresAt - stored.CreatedAt);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("unknown-email")]
    [InlineData("google-only")]
    [InlineData("password-over-128")]
    [InlineData("malformed-email")]
    public async Task REQ005_every_credential_failure_returns_the_identical_401_invalid_credentials(string scenario)
    {
        using var client = NewClient();
        var reference = await PostJson(client, LoginPath, LoginBody(password: "outra-senha-qualquer"));

        var response = await PostJson(client, LoginPath, FailureBody(scenario));

        Assert.Equal(HttpStatusCode.Unauthorized, reference.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("invalid_credentials", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.TryGetProperty("errors", out _));
        AssertNoSecrets(body);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie, StringComparison.Ordinal));
        Assert.Equal(await NormalizedBody(reference), NormalizeBody(body));
        Assert.Equal(NormalizedHeaders(reference), NormalizedHeaders(response));
    }

    [Fact]
    public async Task REQ005_credential_failure_is_not_cacheable()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody(password: "errada-errada"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("email", "")]
    [InlineData("email", null)]
    [InlineData("password", "")]
    [InlineData("password", null)]
    public async Task REQ004_empty_fields_return_400_validation_failed_with_required(string property, string? value)
    {
        using var client = NewClient();
        var body = LoginBody();
        body[property] = value;

        var response = await PostJson(client, LoginPath, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        var codes = doc.RootElement.GetProperty("errors").GetProperty(property).EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains("required", codes);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie, StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ027_failed_attempts_apply_a_progressive_delay_per_account_and_never_block_the_account()
    {
        using var client = NewClient();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostJson(client, LoginPath, LoginBody(password: "errada-errada"))).StatusCode);
        }

        var delays = _factory.Delayer.Delays.ToArray();
        Assert.NotEmpty(delays);
        Assert.Equal(delays.Order().ToArray(), delays);
        Assert.True(delays.Max() > TimeSpan.Zero);
        Assert.True(delays.Max() <= TimeSpan.FromSeconds(10));

        var success = await PostJson(client, LoginPath, LoginBody());

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    [Fact]
    public async Task REQ027_unknown_email_gets_the_same_delay_as_an_existing_account()
    {
        using var client = NewClient();
        for (var i = 0; i < 4; i++)
        {
            await PostJson(client, LoginPath, LoginBody(password: "errada-errada"));
        }

        var existing = _factory.Delayer.Delays.ToArray();
        _factory.Delayer.Delays.Clear();
        for (var i = 0; i < 4; i++)
        {
            await PostJson(client, LoginPath, LoginBody(email: "ninguem@example.com", password: "errada-errada"));
        }

        Assert.NotEmpty(existing);
        Assert.Equal(existing, _factory.Delayer.Delays.ToArray());
    }

    [Fact]
    public async Task REQ027_failures_across_many_emails_apply_a_delay_by_ip_without_blocking()
    {
        using var client = NewClient();
        for (var i = 0; i < 12; i++)
        {
            await PostJson(client, LoginPath, LoginBody(email: $"alvo{i}@example.com", password: "errada-errada"));
        }

        Assert.NotEmpty(_factory.Delayer.Delays);
        Assert.Equal(HttpStatusCode.OK, (await PostJson(client, LoginPath, LoginBody())).StatusCode);
    }

    [Fact]
    public async Task SEC01_login_over_the_account_budget_is_429_too_many_attempts_without_verifying_or_a_retry_hint()
    {
        using var client = NewClient();
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostJson(client, LoginPath, LoginBody(password: "errada-errada"))).StatusCode);
        }

        var verifiedBefore = _factory.Hasher.VerifyCalls.Count;
        var refused = await PostJson(client, LoginPath, LoginBody());

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.False(refused.Headers.Contains("Retry-After"));
        var text = await refused.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("too_many_attempts", doc.RootElement.GetProperty("code").GetString());
        AssertNoSecrets(text);
        Assert.Equal(verifiedBefore, _factory.Hasher.VerifyCalls.Count);
    }

    [Fact]
    public async Task SEC01_the_over_budget_answer_is_identical_for_an_existing_and_an_unknown_account()
    {
        using var client = NewClient();
        for (var i = 0; i < 30; i++)
        {
            await PostJson(client, LoginPath, LoginBody(password: "errada-errada"));
            await PostJson(client, LoginPath, LoginBody(email: "ninguem@example.com", password: "errada-errada"));
        }

        var existing = await PostJson(client, LoginPath, LoginBody());
        var unknown = await PostJson(client, LoginPath, LoginBody(email: "ninguem@example.com"));

        Assert.Equal(HttpStatusCode.TooManyRequests, existing.StatusCode);
        Assert.Equal(existing.StatusCode, unknown.StatusCode);
        Assert.Equal(await NormalizedBody(existing), await NormalizedBody(unknown));
        Assert.Equal(NormalizedHeaders(existing), NormalizedHeaders(unknown));
    }

    [Fact]
    public async Task REQ027_successful_login_does_not_delay()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.Delayer.Delays);
    }

    [Fact]
    public async Task REQ011_session_with_valid_cookie_returns_200_summary_without_email_or_hash()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await Get(client, SessionPath, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(["avatar", "displayName", "hasGoogle", "hasPassword"], PropertyNames(doc.RootElement));
        Assert.Equal("Ana", doc.RootElement.GetProperty("displayName").GetString());
        AssertNoSecrets(text);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("__Host-dl_session=token-desconhecido")]
    [InlineData("__Host-dl_session=")]
    public async Task REQ011_session_without_a_valid_cookie_returns_401_unauthenticated(string? cookiePair)
    {
        using var client = NewClient();

        var response = await Get(client, SessionPath, cookiePair);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task REQ012_session_after_the_sliding_window_expires_returns_401_unauthenticated()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, SessionPath, cookie)).StatusCode);

        _factory.Clock.Advance(TimeSpan.FromMinutes(31));

        await AssertUnauthenticated(await Get(client, SessionPath, cookie));
    }

    [Fact]
    public async Task REQ010_logout_returns_204_expires_the_cookie_and_invalidates_the_session()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, SessionPath, cookie)).StatusCode);

        var logout = await PostJson(client, LogoutPath, body: null, cookie);
        var after = await Get(client, SessionPath, cookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var expired = SessionSetCookie(logout);
        Assert.StartsWith(SessionCookie + "=;", expired, StringComparison.Ordinal);
        Assert.Contains("max-age=0", expired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", expired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", logout.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        await AssertUnauthenticated(after);
    }

    [Fact]
    public async Task REQ010_logout_is_idempotent_and_204_without_a_session_or_with_a_reused_token()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var first = await PostJson(client, LogoutPath, body: null, cookie);
        var reused = await PostJson(client, LogoutPath, body: null, cookie);
        var anonymous = await PostJson(client, LogoutPath, body: null, cookiePair: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reused.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, anonymous.StatusCode);
        Assert.Contains("max-age=0", SessionSetCookie(anonymous), StringComparison.OrdinalIgnoreCase);
        await AssertUnauthenticated(await Get(client, SessionPath, cookie));
    }

    [Fact]
    public async Task REQ010_logout_revokes_only_the_presented_session()
    {
        using var client = NewClient();
        var first = await LoginCookieAsync(client);
        var second = await LoginCookieAsync(client);

        await PostJson(client, LogoutPath, body: null, first);

        await AssertUnauthenticated(await Get(client, SessionPath, first));
        Assert.Equal(HttpStatusCode.OK, (await Get(client, SessionPath, second)).StatusCode);
    }

    [Fact]
    public async Task REQ035_login_issues_a_new_token_each_time_without_session_fixation()
    {
        using var client = NewClient();

        var first = await LoginCookieAsync(client);
        var second = await LoginCookieAsync(client);

        Assert.NotEqual(first, second);
    }

    private static Dictionary<string, object?> LoginBody(
        string email = AnaEmail, string password = AnaPassword, bool rememberMe = false) => new()
        {
            ["email"] = email,
            ["password"] = password,
            ["rememberMe"] = rememberMe,
        };

    private static Dictionary<string, object?> FailureBody(string scenario) => scenario switch
    {
        "wrong-password" => LoginBody(password: "senha-errada-123"),
        "unknown-email" => LoginBody(email: "ninguem@example.com"),
        "google-only" => LoginBody(email: GoogleEmail),
        "password-over-128" => LoginBody(password: new string('a', 129)),
        "malformed-email" => LoginBody(email: "nao-e-um-email"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static async Task<string> LoginCookieAsync(HttpClient client)
    {
        var response = await PostJson(client, LoginPath, LoginBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SessionSetCookie(response).Split(';')[0];
    }

    private static async Task<HttpResponseMessage> PostJson(
        HttpClient client, string path, Dictionary<string, object?>? body, string? cookiePair = null)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var csrfPair = Assert.Single(SetCookies(csrf), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        request.Headers.Add("Cookie", cookiePair is null ? csrfPair : $"{csrfPair}; {cookiePair}");
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string? cookiePair)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookiePair is not null)
        {
            request.Headers.Add("Cookie", cookiePair);
        }

        return await client.SendAsync(request);
    }

    private static async Task AssertUnauthenticated(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("unauthenticated", doc.RootElement.GetProperty("code").GetString());
        AssertNoSecrets(json);
    }

    private static void AssertNoSecrets(string text)
    {
        Assert.DoesNotContain(AnaEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(GoogleEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pbkdf2", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private static string SessionSetCookie(HttpResponseMessage response) =>
        Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private static async Task<string> NormalizedBody(HttpResponseMessage response) =>
        NormalizeBody(await response.Content.ReadAsStringAsync());

    private static string NormalizeBody(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return string.Join(
            "|",
            doc.RootElement.EnumerateObject()
                .Where(p => p.Name != "traceId")
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={p.Value.GetRawText()}"));
    }

    private static string NormalizedHeaders(HttpResponseMessage response) =>
        string.Join(
            "|",
            response.Headers.Concat(response.Content.Headers)
                .Where(h => !string.Equals(h.Key, "Date", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => $"{h.Key}:{string.Join(",", h.Value)}"));

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class LoginFactory : WebApplicationFactory<Program>
    {
        private const string Terms = "2026-10";

        public LoginFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Attempts = new InMemoryAccessAttemptStore(Uow);
            Seed().GetAwaiter().GetResult();
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public InMemoryAccessAttemptStore Attempts { get; }

        public FakePasswordHasher Hasher { get; } = new(600000);

        public RecordingDelayer Delayer { get; } = new();

        public MutableClock Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IUnitOfWork>();
                services.RemoveDatabaseRoleCheck();
                services.RemoveAll<IPlayerRepository>();
                services.RemoveAll<ISessionRepository>();
                services.RemoveAll<IAccessAttemptStore>();
                services.RemoveAll<IPasswordHasher>();
                services.RemoveAll<IKeyedHasher>();
                services.RemoveAll<IDelayer>();
                services.RemoveAll<ITokenHasher>();
                services.RemoveAll<ISecureTokenGenerator>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<ITermsVersionProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IUnitOfWork>(Uow);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(Sessions);
                services.AddSingleton<IAccessAttemptStore>(Attempts);
                services.AddSingleton<IPasswordHasher>(Hasher);
                services.AddSingleton<IKeyedHasher>(new FakeKeyedHasher());
                services.AddSingleton<IDelayer>(Delayer);
                services.AddSingleton<ITokenHasher>(new Sha256TokenHasher());
                services.AddSingleton<ISecureTokenGenerator>(new SequentialTokenGenerator());
                services.AddSingleton<IAvatarPresetCatalog>(new InMemoryAvatarPresetCatalog(
                    [new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true)]));
                services.AddSingleton<ITermsVersionProvider>(new FixedTermsVersionProvider(
                    Terms,
                    new Uri("https://deckloom.example.test/termos"),
                    new Uri("https://deckloom.example.test/privacidade")));
            });
        }

        private async Task Seed()
        {
            var now = Clock.GetUtcNow();
            var password = await Hasher.HashAsync(Deckloom.Accounts.Domain.Password.Create(AnaPassword).Value);
            var ana = Player.RegisterWithPassword(
                Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value,
                password,
                DisplayName.Create("Ana").Value,
                Terms,
                Terms,
                true,
                true,
                now,
                "preset-01").Value;
            var gui = Player.RegisterWithGoogle(
                Deckloom.Accounts.Domain.Email.Create(GoogleEmail).Value,
                ExternalIdentity.Create("google", "sub-gui").Value,
                DisplayName.Create("Gui").Value,
                Terms,
                Terms,
                true,
                true,
                now,
                "preset-01").Value;
            await Players.Add(ana);
            await Players.Add(gui);
            await Uow.CommitAsync();
        }
    }
}
