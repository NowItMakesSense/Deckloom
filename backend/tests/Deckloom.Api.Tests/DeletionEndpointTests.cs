using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;

namespace Deckloom.Api.Tests;

// T-090 (RED): POST /api/v1/me/deletion (design, tabela "Endpoints"; REQ-023 a 027, 042, 047, 048).
// Contrato: corpo {confirm: bool, password?: string}; 204 + cookie de sessao expirado; 400 validation_failed com
// errors.confirm / errors.password = ["required"]; 401 incorrect_password; 401 unauthenticated; 403 reauth_required;
// 403 reauth_expired (> 5 min, nada excluido).
// Lacunas assumidas: (1) "decks purgados" e verificado por RecordingPlayerDecksPurger registrado na fabrica;
// (2) conta sem senha sem `confirm` mas com reautenticacao recente responde 400 validation_failed com
// errors.confirm = ["required"] (design: "confirm required"); (3) o atraso progressivo e verificado pelo
// RecordingDelayer (ADR-0005) e a acao account_deletion pela chave de conta em InMemoryAccessAttemptStore;
// (4) o log AccountDeleted e capturado por um ILoggerProvider e deve conter o playerId e nenhum email/senha;
// (5) o host deve compor DeleteMyAccount a partir das portas registradas aqui (incl. IPlayerDecksPurger e
// IAvatarStorage); (6) relogio controlavel via TimeProvider (MutableClock), como em LoginSessionLogoutTests.
public sealed class DeletionEndpointTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string DeletionPath = "/api/v1/me/deletion";
    private const string LoginPath = "/api/v1/auth/login";
    private const string RegisterPath = "/api/v1/auth/register";
    private const string SessionPath = "/api/v1/auth/session";
    private const string AnaEmail = "ana@example.com";
    private const string GoogleEmail = "gui@example.com";
    private const string Password = "senha-valida-123";
    private const string GoogleSessionToken = "gui-session-token";
    private const string Terms = "2026-10";

    private readonly DeletionFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ023_password_account_with_confirm_and_correct_password_returns_204_and_expires_the_session_cookie()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await PostDeletion(client, Body(confirm: true, password: Password), cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var expired = Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.StartsWith(SessionCookie + "=;", expired, StringComparison.Ordinal);
        Assert.Contains("max-age=0", expired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", expired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ024_REQ025_deletion_removes_the_player_purges_decks_and_ends_all_sessions()
    {
        using var client = NewClient();
        var first = await LoginCookieAsync(client);
        var second = await LoginCookieAsync(client);
        var playerId = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!.Id;

        var response = await PostDeletion(client, Body(confirm: true, password: Password), first);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _factory.Players.GetById(playerId));
        Assert.Null(await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value));
        Assert.Contains(playerId, _factory.Purger.Purged);
        await AssertUnauthenticated(await Get(client, SessionPath, first));
        await AssertUnauthenticated(await Get(client, SessionPath, second));
    }

    [Theory]
    [InlineData("no-confirm")]
    [InlineData("confirm-false")]
    public async Task REQ026_password_account_without_confirm_returns_400_with_confirm_required_and_keeps_the_account(
        string scenario)
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        var body = scenario == "no-confirm"
            ? new Dictionary<string, object?> { ["password"] = Password }
            : Body(confirm: false, password: Password);

        var response = await PostDeletion(client, body, cookie);

        await AssertValidationRequired(response, "confirm");
        await AssertAnaIntact(client, cookie);
    }

    [Theory]
    [InlineData("no-password")]
    [InlineData("empty-password")]
    [InlineData("null-password")]
    public async Task REQ026_password_account_without_password_returns_400_with_password_required_and_keeps_the_account(
        string scenario)
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        var body = scenario switch
        {
            "no-password" => new Dictionary<string, object?> { ["confirm"] = true },
            "empty-password" => Body(confirm: true, password: string.Empty),
            _ => new Dictionary<string, object?> { ["confirm"] = true, ["password"] = null },
        };

        var response = await PostDeletion(client, body, cookie);

        await AssertValidationRequired(response, "password");
        await AssertAnaIntact(client, cookie);
    }

    [Fact]
    public async Task REQ026_wrong_password_returns_401_incorrect_password_and_leaves_the_account_intact()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await PostDeletion(client, Body(confirm: true, password: "senha-errada-123"), cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("incorrect_password", doc.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(AnaEmail, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie, StringComparison.Ordinal));
        await AssertAnaIntact(client, cookie);
        Assert.Empty(_factory.Purger.Purged);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("__Host-dl_session=token-desconhecido")]
    public async Task REQ023_deletion_without_a_valid_session_returns_401_unauthenticated(string? cookiePair)
    {
        using var client = NewClient();

        var response = await PostDeletion(client, Body(confirm: true, password: Password), cookiePair);

        await AssertUnauthenticated(response);
        Assert.NotNull(await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value));
    }

    [Fact]
    public async Task REQ042_google_only_account_without_reauthentication_returns_403_reauth_required()
    {
        using var client = NewClient();

        var response = await PostDeletion(client, Body(confirm: true), GoogleCookie);

        await AssertProblem(response, HttpStatusCode.Forbidden, "reauth_required");
        await AssertGuiIntact();
    }

    [Fact]
    public async Task REQ042_google_only_account_with_reauthentication_older_than_five_minutes_returns_403_reauth_expired()
    {
        using var client = NewClient();
        await ReauthenticateGuiAsync();
        _factory.Clock.Advance(TimeSpan.FromMinutes(6));

        var response = await PostDeletion(client, Body(confirm: true), GoogleCookie);

        await AssertProblem(response, HttpStatusCode.Forbidden, "reauth_expired");
        await AssertGuiIntact();
    }

    [Fact]
    public async Task REQ042_google_only_account_with_recent_reauthentication_and_confirm_returns_204_and_deletes()
    {
        using var client = NewClient();
        await ReauthenticateGuiAsync();
        _factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var guiId = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(GoogleEmail).Value))!.Id;

        var response = await PostDeletion(client, Body(confirm: true), GoogleCookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("max-age=0", Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal)), StringComparison.OrdinalIgnoreCase);
        Assert.Null(await _factory.Players.GetById(guiId));
        Assert.Contains(guiId, _factory.Purger.Purged);
    }

    [Fact]
    public async Task REQ042_google_only_account_with_recent_reauthentication_but_without_confirm_returns_400_and_keeps_the_account()
    {
        using var client = NewClient();
        await ReauthenticateGuiAsync();

        var response = await PostDeletion(client, new Dictionary<string, object?> { ["confirm"] = false }, GoogleCookie);

        await AssertValidationRequired(response, "confirm");
        await AssertGuiIntact();
    }

    [Fact]
    public async Task REQ027_after_deletion_repeating_the_request_returns_401_and_the_old_login_is_refused()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await PostDeletion(client, Body(confirm: true, password: Password), cookie)).StatusCode);

        var repeated = await PostDeletion(client, Body(confirm: true, password: Password), cookie);
        var login = await PostJson(client, LoginPath, new Dictionary<string, object?>
        {
            ["email"] = AnaEmail,
            ["password"] = Password,
            ["rememberMe"] = false,
        });

        await AssertUnauthenticated(repeated);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal("invalid_credentials", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task REQ027_registering_the_same_email_after_deletion_creates_a_new_player_without_previous_data()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        var oldId = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!.Id;
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await PostDeletion(client, Body(confirm: true, password: Password), cookie)).StatusCode);

        var register = await PostJson(client, RegisterPath, new Dictionary<string, object?>
        {
            ["email"] = AnaEmail,
            ["password"] = Password,
            ["displayName"] = "Ana Nova",
            ["acceptTerms"] = true,
            ["termsVersion"] = Terms,
            ["adultDeclaration"] = true,
        });

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var recreated = await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value);
        Assert.NotNull(recreated);
        Assert.NotEqual(oldId, recreated.Id);
        Assert.Equal("Ana Nova", recreated.DisplayName.Value);
    }

    [Fact]
    public async Task REQ047_REQ027_wrong_passwords_apply_progressive_delay_without_blocking_and_deletion_clears_account_attempts()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        for (var i = 0; i < 5; i++)
        {
            var wrong = await PostDeletion(client, Body(confirm: true, password: "senha-errada-123"), cookie);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var delays = _factory.Delayer.Delays.ToArray();
        Assert.NotEmpty(delays);
        Assert.Equal(delays.Order().ToArray(), delays);
        Assert.True(delays.Max() > TimeSpan.Zero);
        var accountKey = new FakeKeyedHasher().Hash(AnaEmail);
        Assert.True(await _factory.Attempts.CountByAccountAsync(accountKey, DateTimeOffset.MinValue) >= 5);

        var success = await PostDeletion(client, Body(confirm: true, password: Password), cookie);

        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Equal(0, await _factory.Attempts.CountByAccountAsync(accountKey, DateTimeOffset.MinValue));
    }

    [Fact]
    public async Task SEC01_deletion_over_the_account_budget_is_429_too_many_attempts_and_keeps_the_account()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostDeletion(client, Body(confirm: true, password: "senha-errada-123"), cookie)).StatusCode);
        }

        var refused = await PostDeletion(client, Body(confirm: true, password: Password), cookie);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.False(refused.Headers.Contains("Retry-After"));
        using var doc = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("too_many_attempts", doc.RootElement.GetProperty("code").GetString());
        await AssertAnaIntact(client, cookie);
    }

    [Fact]
    public async Task REQ024_deletion_logs_account_deleted_with_only_the_player_id()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        var playerId = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!.Id;

        var response = await PostDeletion(client, Body(confirm: true, password: Password), cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var line = Assert.Single(_factory.LogLines, l => l.Contains("Account deleted", StringComparison.Ordinal));
        Assert.Contains(playerId.Value.ToString(), line, StringComparison.OrdinalIgnoreCase);
        foreach (var line2 in _factory.LogLines)
        {
            Assert.DoesNotContain(AnaEmail, line2, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Password, line2, StringComparison.Ordinal);
        }
    }

    private static string GoogleCookie => $"{SessionCookie}={GoogleSessionToken}";

    private static Dictionary<string, object?> Body(bool confirm, string? password = null)
    {
        var body = new Dictionary<string, object?> { ["confirm"] = confirm };
        if (password is not null || confirm)
        {
            body["password"] = password;
        }

        if (password is null && confirm)
        {
            body.Remove("password");
        }

        return body;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private async Task ReauthenticateGuiAsync()
    {
        var session = await _factory.Sessions.FindByTokenHash(new Sha256TokenHasher().Hash(GoogleSessionToken));
        Assert.NotNull(session);
        var identity = ExternalIdentity.Create("google", "sub-gui").Value;
        Assert.True(session.MarkReauthenticated(identity, identity, _factory.Clock.GetUtcNow()).IsSuccess);
        await _factory.Sessions.Save(session);
        await _factory.Uow.CommitAsync();
    }

    private async Task AssertAnaIntact(HttpClient client, string cookie)
    {
        Assert.NotNull(await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value));
        Assert.Equal(HttpStatusCode.OK, (await Get(client, SessionPath, cookie)).StatusCode);
        Assert.Empty(_factory.Purger.Purged);
    }

    private async Task AssertGuiIntact()
    {
        Assert.NotNull(await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(GoogleEmail).Value));
        Assert.Empty(_factory.Purger.Purged);
    }

    private static async Task<string> LoginCookieAsync(HttpClient client)
    {
        var response = await PostJson(client, LoginPath, new Dictionary<string, object?>
        {
            ["email"] = AnaEmail,
            ["password"] = Password,
            ["rememberMe"] = false,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static Task<HttpResponseMessage> PostDeletion(
        HttpClient client, Dictionary<string, object?> body, string? cookiePair) =>
        PostJson(client, DeletionPath, body, cookiePair);

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

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertUnauthenticated(HttpResponseMessage response) =>
        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");

    private static async Task AssertValidationRequired(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        var codes = doc.RootElement.GetProperty("errors").GetProperty(field).EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains("required", codes);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie, StringComparison.Ordinal));
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class DeletionFactory : WebApplicationFactory<Program>
    {
        private readonly ConcurrentQueue<string> _logs = new();

        public DeletionFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Attempts = new InMemoryAccessAttemptStore(Uow);
            Purger = new RecordingPlayerDecksPurger(Uow);
            AvatarStorage = new InMemoryAvatarStorage(Uow);
            Seed().GetAwaiter().GetResult();
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public InMemoryAccessAttemptStore Attempts { get; }

        public RecordingPlayerDecksPurger Purger { get; }

        public InMemoryAvatarStorage AvatarStorage { get; }

        public FakePasswordHasher Hasher { get; } = new(600000);

        public RecordingDelayer Delayer { get; } = new();

        public MutableClock Clock { get; } = new();

        public IReadOnlyCollection<string> LogLines => _logs.ToArray();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(new CapturingProvider(_logs));
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IUnitOfWork>();
                services.RemoveDatabaseRoleCheck();
                services.RemoveAll<IPlayerRepository>();
                services.RemoveAll<ISessionRepository>();
                services.RemoveAll<IAccessAttemptStore>();
                services.RemoveAll<IPlayerDecksPurger>();
                services.RemoveAll<IAvatarStorage>();
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
                services.AddSingleton<IPlayerDecksPurger>(Purger);
                services.AddSingleton<IAvatarStorage>(AvatarStorage);
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
            var password = await Hasher.HashAsync(Deckloom.Accounts.Domain.Password.Create(Password).Value);
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
            await Sessions.Add(Session.Start(gui.Id, new Sha256TokenHasher().Hash(GoogleSessionToken), false, now));
            await Uow.CommitAsync();
        }
    }

    private sealed class CapturingProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(formatter(state, exception));
    }
}
