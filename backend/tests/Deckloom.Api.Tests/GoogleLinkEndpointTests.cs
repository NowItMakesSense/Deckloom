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

// T-087 (RED): POST /api/v1/auth/google/link (design, tabela "Endpoints" e "Forma de LinkGoogleWithPassword";
// REQ-008, 032, 036, 038). Corpo so {password}; a pendencia de vinculo vem do cookie __Host-dl_pending.
// Lacunas assumidas: (1) mutacoes exigem CSRF como no register; (2) 401/404/409/400 saem como
// application/problem+json com "code" (400 com "errors"; 401 sem "errors"); (3) o fake de Player nao simula a
// unicidade do subject no commit, entao o teste do 409 usa um IUnitOfWork decorador que lanca
// UniqueConstraintViolationException quando dois jogadores terminam com o mesmo subject (como o indice unico real).
public sealed class GoogleLinkEndpointTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string PendingCookie = "__Host-dl_pending";
    private const string SessionCookie = "__Host-dl_session";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string LinkPath = "/api/v1/auth/google/link";
    private const string Terms = "2026-10";
    private const string AnaEmail = "ana@example.com";
    private const string AnaPassword = "senha-valida-123";
    private const string LinkToken = "pend-link-1";
    private const string SignupToken = "pend-signup-1";
    private const string LinkSubject = "sub-ana";
    private const string TakenSubject = "sub-gui";

    private readonly LinkFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ036_correct_password_returns_200_session_summary_links_google_and_consumes_the_pending()
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();

        var response = await Send(client, LinkToken, Body(AnaPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(["avatar", "displayName", "hasGoogle", "hasPassword"], PropertyNames(doc.RootElement));
        Assert.Equal("Ana", doc.RootElement.GetProperty("displayName").GetString());
        Assert.True(doc.RootElement.GetProperty("hasGoogle").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("hasPassword").GetBoolean());

        var session = Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await _factory.Sessions.FindByTokenHash(HashOf(CookieValue(session))));
        AssertPendingCookieExpired(response);

        var linked = await _factory.Players.FindByGoogleSubject(LinkSubject);
        Assert.NotNull(linked);
        Assert.Equal(AnaEmail, linked!.Email.Value);
        Assert.Null(await _factory.Pending.FindAsync(HashOf(LinkToken)));
        AssertNoStore(response);
        AssertNoSensitiveData(text, AnaEmail, LinkSubject, LinkToken, AnaPassword, CookieValue(session));
    }

    [Fact]
    public async Task SEC01_link_over_the_account_budget_is_429_too_many_attempts_and_keeps_the_pending()
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await Send(client, LinkToken, Body("senha-errada-999"))).StatusCode);
        }

        var refused = await Send(client, LinkToken, Body(AnaPassword));

        await AssertProblem(refused, HttpStatusCode.TooManyRequests, "too_many_attempts");
        Assert.False(refused.Headers.Contains("Retry-After"));
        Assert.Null(await _factory.Players.FindByGoogleSubject(LinkSubject));
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(LinkToken)));
        Assert.DoesNotContain(SetCookies(refused), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ036_link_pending_is_single_use()
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await Send(client, LinkToken, Body(AnaPassword))).StatusCode);

        var second = await Send(client, LinkToken, Body(AnaPassword));

        await AssertProblem(second, HttpStatusCode.NotFound, "pending_not_found");
        Assert.DoesNotContain(SetCookies(second), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ032_wrong_password_returns_401_incorrect_password_without_errors_key_and_keeps_everything()
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();

        var response = await Send(client, LinkToken, Body("senha-errada-999"));

        await AssertProblem(response, HttpStatusCode.Unauthorized, "incorrect_password");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.TryGetProperty("errors", out _));
        Assert.Null(await _factory.Players.FindByGoogleSubject(LinkSubject));
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(LinkToken)));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ032_failed_attempts_apply_a_progressive_delay_and_never_block_the_link()
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await Send(client, LinkToken, Body("senha-errada-999"))).StatusCode);
        }

        var delays = _factory.Delayer.Delays.ToArray();
        Assert.NotEmpty(delays);
        Assert.Equal(delays.Order().ToArray(), delays);
        Assert.True(delays.Max() > TimeSpan.Zero);
        Assert.True(delays.Max() <= TimeSpan.FromSeconds(10));

        var success = await Send(client, LinkToken, Body(AnaPassword));

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    [Fact]
    public async Task REQ036_without_pending_cookie_returns_404_pending_not_found_and_touches_nothing()
    {
        using var client = NewClient();

        var response = await Send(client, pendingToken: null, Body(AnaPassword));

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Empty(_factory.Delayer.Delays);
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ036_with_a_signup_pending_returns_404_and_does_not_consume_it()
    {
        await _factory.Pending.AddAsync(PendingGoogleSignup.Create(
            HashOf(SignupToken), "sub-novo", Email.Create("novo@example.com").Value, "Nova", _factory.Clock.GetUtcNow()));
        using var client = NewClient();

        var response = await Send(client, SignupToken, Body(AnaPassword));

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(SignupToken)));
        Assert.Null(await _factory.Players.FindByGoogleSubject("sub-novo"));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ036_with_an_expired_pending_returns_404_pending_not_found()
    {
        await SeedLink(LinkSubject);
        _factory.Clock.Advance(PendingGoogleAuth.Ttl + TimeSpan.FromSeconds(1));
        using var client = NewClient();

        var response = await Send(client, LinkToken, Body(AnaPassword));

        await AssertProblem(response, HttpStatusCode.NotFound, "pending_not_found");
        Assert.Null(await _factory.Players.FindByGoogleSubject(LinkSubject));
    }

    [Fact]
    public async Task REQ036_subject_already_linked_to_another_player_returns_409_google_not_completed_without_linking()
    {
        await SeedLink(TakenSubject);
        using var client = NewClient();

        var response = await Send(client, LinkToken, Body(AnaPassword));

        await AssertProblem(response, HttpStatusCode.Conflict, "google_not_completed");
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
        var ana = await _factory.Players.FindByEmail(Email.Create(AnaEmail).Value);
        Assert.Null(ana!.ExternalIdentity);
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(LinkToken)));
    }

    [Fact]
    public async Task REQ038_body_without_password_returns_400_validation_failed_with_password_required()
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();

        var response = await Send(client, LinkToken, new Dictionary<string, object?>());

        await AssertValidation(response, "required");
        await AssertNothingChanged(response);
    }

    [Theory]
    [InlineData("", "required")]
    [InlineData(null, "required")]
    public async Task REQ038_empty_or_null_password_returns_400_password_required(string? value, string code)
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();

        var response = await Send(client, LinkToken, Body(value));

        await AssertValidation(response, code);
        await AssertNothingChanged(response);
    }

    [Fact]
    public async Task REQ038_password_over_128_characters_returns_400_too_long_without_hashing_or_failure()
    {
        await SeedLink(LinkSubject);
        using var client = NewClient();

        var response = await Send(client, LinkToken, Body(new string('a', 129)));

        await AssertValidation(response, "too_long");
        await AssertNothingChanged(response);
    }

    private async Task AssertNothingChanged(HttpResponseMessage response)
    {
        Assert.Null(await _factory.Players.FindByGoogleSubject(LinkSubject));
        Assert.NotNull(await _factory.Pending.FindAsync(HashOf(LinkToken)));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
        AssertNoStore(response);
    }

    private static async Task AssertValidation(HttpResponseMessage response, string code)
    {
        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var codes = doc.RootElement.GetProperty("errors").GetProperty("password").EnumerateArray()
            .Select(e => e.GetString());
        Assert.Contains(code, codes);
    }

    private static Dictionary<string, object?> Body(string? password) => new() { ["password"] = password };

    private async Task SeedLink(string subject)
    {
        var ana = await _factory.Players.FindByEmail(Email.Create(AnaEmail).Value);
        await _factory.Pending.AddAsync(PendingGoogleLink.Create(
            HashOf(LinkToken), subject, Email.Create(AnaEmail).Value, ana!.Id, _factory.Clock.GetUtcNow()));
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
        HttpClient client, string? pendingToken, Dictionary<string, object?> body)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var csrfValue = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var cookies = new List<string>
        {
            Assert.Single(SetCookies(csrf), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
                .Split(';')[0],
        };

        if (pendingToken is not null)
        {
            cookies.Add($"{PendingCookie}={pendingToken}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, LinkPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", csrfValue);
        return await client.SendAsync(request);
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    // Simula o indice unico de (provider, subject) que o fake de Player nao aplica no commit.
    private sealed class SubjectUniqueUnitOfWork(IUnitOfWork inner, InMemoryPlayerRepository players) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);

        public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) =>
            inner.ExecuteAsync(work, ct);

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) =>
            inner.ExecuteAsync(async innerCt =>
            {
                var result = await work(innerCt);
                var ana = await players.FindByEmail(Email.Create(AnaEmail).Value, innerCt);
                var gui = await players.FindByEmail(Email.Create("gui@example.com").Value, innerCt);
                if (ana?.ExternalIdentity is { } a && gui?.ExternalIdentity is { } g && a.Subject == g.Subject)
                {
                    throw new UniqueConstraintViolationException("ux_player_external_identity_provider_subject");
                }

                return result;
            }, ct);
    }

    private sealed class LinkFactory : WebApplicationFactory<Program>
    {
        public LinkFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Pending = new InMemoryPendingGoogleAuthStore(Clock, Uow);
            Attempts = new InMemoryAccessAttemptStore(Uow);
            Seed().GetAwaiter().GetResult();
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public InMemoryPendingGoogleAuthStore Pending { get; }

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
                services.RemoveAll<IPendingGoogleAuthStore>();
                services.RemoveAll<IAccessAttemptStore>();
                services.RemoveAll<IPasswordHasher>();
                services.RemoveAll<IKeyedHasher>();
                services.RemoveAll<IDelayer>();
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.RemoveAll<ITokenHasher>();
                services.RemoveAll<ISecureTokenGenerator>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<ITermsVersionProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IUnitOfWork>(new SubjectUniqueUnitOfWork(Uow, Players));
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(Sessions);
                services.AddSingleton<IPendingGoogleAuthStore>(Pending);
                services.AddSingleton<IAccessAttemptStore>(Attempts);
                services.AddSingleton<IPasswordHasher>(Hasher);
                services.AddSingleton<IKeyedHasher>(new FakeKeyedHasher());
                services.AddSingleton<IDelayer>(Delayer);
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
            var stored = await Hasher.HashAsync(Deckloom.Accounts.Domain.Password.Create(AnaPassword).Value);
            var ana = Player.RegisterWithPassword(
                Email.Create(AnaEmail).Value, stored, DisplayName.Create("Ana").Value,
                Terms, Terms, true, true, now, "preset-01").Value;
            var gui = Player.RegisterWithGoogle(
                Email.Create("gui@example.com").Value, ExternalIdentity.Create("google", TakenSubject).Value,
                DisplayName.Create("Gui").Value, Terms, Terms, true, true, now, "preset-01").Value;
            await Players.Add(ana);
            await Players.Add(gui);
            await Uow.CommitAsync();
        }
    }
}
