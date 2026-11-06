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

// T-086 (RED): GET/DELETE /api/v1/auth/google/pending e POST /api/v1/auth/google/complete-signup
// (design, tabela "Endpoints"; REQ-006, 033, 034, 039, 040, 043, 044, 048).
// Fora de escopo: POST /auth/google/link e T-087 (incorrect_password, nao invalid_credentials).
// Lacunas assumidas: (1) kind serializa em minusculas "signup"/"link"; (2) mutacoes exigem CSRF como no register;
// (3) pendencia de vinculo em complete-signup vale como pending_not_found e nao e consumida;
// (4) 404/409/400 saem como application/problem+json com "code".
public sealed class GoogleCompletionEndpointsTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string PendingCookie = "__Host-dl_pending";
    private const string SessionCookie = "__Host-dl_session";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string PendingPath = "/api/v1/auth/google/pending";
    private const string CompletePath = "/api/v1/auth/google/complete-signup";
    private const string Terms = "2026-10";
    private const string SignupToken = "pend-signup-1";
    private const string LinkToken = "pend-link-1";
    private const string NewEmail = "novo@example.com";
    private const string GuiEmail = "gui@example.com";
    private const string LongGoogleName = "Nova Pessoa Com Um Nome Bem Longo Demais Para Exibir";

    private readonly CompletionFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ039_get_pending_of_signup_returns_kind_email_and_the_untruncated_suggested_name()
    {
        await SeedSignup();
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Get, PendingPath, SignupToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["email", "kind", "suggestedDisplayName"], PropertyNames(doc.RootElement));
        Assert.Equal("signup", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(NewEmail, doc.RootElement.GetProperty("email").GetString());
        Assert.Equal(LongGoogleName, doc.RootElement.GetProperty("suggestedDisplayName").GetString());
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ039_get_pending_of_signup_without_google_name_returns_null_suggestion()
    {
        await _factory.Pending.AddAsync(
            PendingGoogleSignup.Create(HashOf(SignupToken), "sub-novo", Email.Create(NewEmail).Value, null, _factory.Clock.GetUtcNow()));
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Get, PendingPath, SignupToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("suggestedDisplayName").ValueKind);
    }

    [Fact]
    public async Task REQ008_get_pending_of_link_returns_kind_link_without_name()
    {
        await SeedLink();
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Get, PendingPath, LinkToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("link", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("suggestedDisplayName").ValueKind);
    }

    [Fact]
    public async Task REQ039_get_pending_without_cookie_returns_404_pending_not_found()
    {
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Get, PendingPath, pendingToken: null);

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ039_get_pending_expired_returns_404_pending_not_found()
    {
        await SeedSignup();
        _factory.Clock.Advance(PendingGoogleAuth.Ttl + TimeSpan.FromSeconds(1));
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Get, PendingPath, SignupToken);

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
    }

    [Fact]
    public async Task REQ006_delete_pending_returns_204_expires_the_cookie_and_removes_the_pending()
    {
        await SeedSignup();
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Delete, PendingPath, SignupToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertPendingCookieExpired(response);
        Assert.Null(await _factory.Pending.FindAsync(HashOf(SignupToken)));
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ006_delete_pending_is_idempotent_without_cookie_or_for_unknown_token()
    {
        using var client = NewClient();

        var withoutCookie = await Send(client, HttpMethod.Delete, PendingPath, pendingToken: null);
        var unknown = await Send(client, HttpMethod.Delete, PendingPath, "desconhecido-1");

        Assert.Equal(HttpStatusCode.NoContent, withoutCookie.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
    }

    [Fact]
    public async Task REQ006_complete_signup_returns_201_session_summary_session_cookie_and_expires_the_pending_cookie()
    {
        await SeedSignup();
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Post, CompletePath, SignupToken, ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(["avatar", "displayName", "hasGoogle", "hasPassword"], PropertyNames(doc.RootElement));
        Assert.Equal("Nova Pessoa", doc.RootElement.GetProperty("displayName").GetString());
        Assert.True(doc.RootElement.GetProperty("hasGoogle").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("hasPassword").GetBoolean());

        var session = Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await _factory.Sessions.FindByTokenHash(HashOf(CookieValue(session))));
        AssertPendingCookieExpired(response);
        Assert.NotNull(await _factory.Players.FindByGoogleSubject("sub-novo"));
        Assert.Null(await _factory.Pending.FindAsync(HashOf(SignupToken)));
        AssertNoStore(response);
        AssertNoSensitiveData(text, NewEmail, "sub-novo", SignupToken, CookieValue(session));
    }

    [Fact]
    public async Task REQ006_complete_signup_pending_is_single_use()
    {
        await SeedSignup();
        using var client = NewClient();
        Assert.Equal(HttpStatusCode.Created, (await Send(client, HttpMethod.Post, CompletePath, SignupToken, ValidBody())).StatusCode);

        var second = await Send(client, HttpMethod.Post, CompletePath, SignupToken, ValidBody());

        await AssertProblem(second, HttpStatusCode.NotFound, "pending_not_found");
        Assert.DoesNotContain(SetCookies(second), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task REQ033_complete_signup_invalid_input_returns_400_and_keeps_the_pending(
        string property, object? value, string field, string code)
    {
        await SeedSignup();
        using var client = NewClient();
        var body = ValidBody();
        body[property] = value;

        var response = await Send(client, HttpMethod.Post, CompletePath, SignupToken, body);

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var codes = doc.RootElement.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString());
        Assert.Contains(code, codes);
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(SignupToken)));
        Assert.Null(await _factory.Players.FindByGoogleSubject("sub-novo"));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ039_complete_signup_without_cookie_returns_404_pending_not_found()
    {
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Post, CompletePath, pendingToken: null, ValidBody());

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ039_complete_signup_with_expired_pending_returns_404_and_creates_nothing()
    {
        await SeedSignup();
        _factory.Clock.Advance(PendingGoogleAuth.Ttl + TimeSpan.FromSeconds(1));
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Post, CompletePath, SignupToken, ValidBody());

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
        Assert.Null(await _factory.Players.FindByGoogleSubject("sub-novo"));
    }

    [Fact]
    public async Task REQ008_complete_signup_with_a_link_pending_returns_404_and_does_not_consume_it()
    {
        await SeedLink();
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Post, CompletePath, LinkToken, ValidBody());

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(LinkToken)));
    }

    [Fact]
    public async Task REQ006_complete_signup_when_the_email_now_exists_returns_409_google_not_completed()
    {
        await _factory.Pending.AddAsync(
            PendingGoogleSignup.Create(HashOf(SignupToken), "sub-gui-2", Email.Create(GuiEmail).Value, "Gui", _factory.Clock.GetUtcNow()));
        using var client = NewClient();

        var response = await Send(client, HttpMethod.Post, CompletePath, SignupToken, ValidBody());

        await AssertProblem(response, HttpStatusCode.Conflict, "google_not_completed");
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(SignupToken)));
    }

    public static TheoryData<string, object?, string, string> InvalidFields() => new()
    {
        { "displayName", "ab", "displayName", "length" },
        { "displayName", new string('n', 31), "displayName", "length" },
        { "displayName", "Ana\u0007", "displayName", "invalid_characters" },
        { "acceptTerms", false, "acceptTerms", "required" },
        { "termsVersion", "1999-01", "acceptTerms", "version_mismatch" },
        { "adultDeclaration", false, "adultDeclaration", "required" },
    };

    private static Dictionary<string, object?> ValidBody() => new()
    {
        ["displayName"] = "Nova Pessoa",
        ["acceptTerms"] = true,
        ["termsVersion"] = Terms,
        ["adultDeclaration"] = true,
    };

    private Task SeedSignup() =>
        _factory.Pending.AddAsync(PendingGoogleSignup.Create(
            HashOf(SignupToken), "sub-novo", Email.Create(NewEmail).Value, LongGoogleName, _factory.Clock.GetUtcNow()));

    private async Task SeedLink()
    {
        var ana = await _factory.Players.FindByEmail(Email.Create("ana@example.com").Value);
        await _factory.Pending.AddAsync(PendingGoogleLink.Create(
            HashOf(LinkToken), "sub-ana", Email.Create("ana@example.com").Value, ana!.Id, _factory.Clock.GetUtcNow()));
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private static void AssertPendingCookieExpired(HttpResponseMessage response)
    {
        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
        Assert.True(
            cookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)
            || cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoSensitiveData(string body, params string[] secrets)
    {
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
    }

    private static TokenHash HashOf(string token) => new Sha256TokenHasher().Hash(token);

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';')[0][(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string? pendingToken, Dictionary<string, object?>? body = null)
    {
        var cookies = new List<string>();
        string? csrfValue = null;
        if (method != HttpMethod.Get)
        {
            var csrf = await client.GetAsync("/api/v1/csrf");
            Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            csrfValue = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
            cookies.Add(Assert.Single(SetCookies(csrf), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
                .Split(';')[0]);
        }

        if (pendingToken is not null)
        {
            cookies.Add($"{PendingCookie}={pendingToken}");
        }

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies));
        }

        if (csrfValue is not null)
        {
            request.Headers.Add("Origin", PublicOrigin);
            request.Headers.Add("X-CSRF-Token", csrfValue);
        }

        return await client.SendAsync(request);
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class CompletionFactory : WebApplicationFactory<Program>
    {
        public CompletionFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Pending = new InMemoryPendingGoogleAuthStore(Clock, Uow);
            Seed().GetAwaiter().GetResult();
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public InMemoryPendingGoogleAuthStore Pending { get; }

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
                services.RemoveAll<IPendingGoogleAuthStore>();
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.RemoveAll<ITokenHasher>();
                services.RemoveAll<ISecureTokenGenerator>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<ITermsVersionProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IUnitOfWork>(Uow);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(Sessions);
                services.AddSingleton<IPendingGoogleAuthStore>(Pending);
                services.AddSingleton<IGoogleIdentityVerifier>(new FakeGoogleIdentityVerifier());
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
            var hasher = new FakePasswordHasher(600000);
            var stored = await hasher.HashAsync(Deckloom.Accounts.Domain.Password.Create("senha-valida-123").Value);
            var ana = Player.RegisterWithPassword(
                Email.Create("ana@example.com").Value, stored, DisplayName.Create("Ana").Value,
                Terms, Terms, true, true, now, "preset-01").Value;
            var gui = Player.RegisterWithGoogle(
                Email.Create(GuiEmail).Value, ExternalIdentity.Create("google", "sub-gui").Value,
                DisplayName.Create("Gui").Value, Terms, Terms, true, true, now, "preset-01").Value;
            await Players.Add(ana);
            await Players.Add(gui);
            await Uow.CommitAsync();
        }
    }
}
