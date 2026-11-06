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

// T-084 (RED): POST /api/v1/auth/password-reset/request e POST /api/v1/auth/password-reset/confirm
// (design, tabela "Endpoints"; REQ-028, 029, 030, 031, 032, 038).
// Lacunas assumidas: (1) a duracao minima de 500 ms e verificada pelo RecordingDelayer: com relogio congelado o
// host deve pedir ao IDelayer a diferenca (500 ms) antes de responder, igual para email existente e inexistente;
// (2) o atraso progressivo por conta/IP (REQ-032) nao tem valor fixado na spec, entao so se verifica que repetir
// o pedido nunca bloqueia (continua 202); (3) o host deve compor RequestPasswordReset/ResetPassword a partir das
// portas registradas aqui, com PasswordResetOptions.PublicBaseUrl = Auth:PublicOrigin; (4) cabecalhos comparados
// sem Date e Content-Length.
public sealed class PasswordResetEndpointsTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string RequestPath = "/api/v1/auth/password-reset/request";
    private const string ConfirmPath = "/api/v1/auth/password-reset/confirm";
    private const string LoginPath = "/api/v1/auth/login";
    private const string SessionPath = "/api/v1/auth/session";
    private const string AnaEmail = "ana@example.com";
    private const string GoogleEmail = "gui@example.com";
    private const string AnaPassword = "senha-valida-123";
    private const string NewPassword = "senha-nova-4567";
    private const string TokenMarker = "#token=";

    private readonly ResetFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData(AnaEmail)]
    [InlineData(GoogleEmail)]
    [InlineData("ninguem@example.com")]
    public async Task REQ028_request_returns_202_with_empty_body_for_any_email(string email)
    {
        using var client = NewClient();

        var response = await PostJson(client, RequestPath, new() { ["email"] = email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SEC01_request_over_the_account_budget_is_still_202_but_sends_no_email_and_looks_the_same()
    {
        using var client = NewClient();
        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await PostJson(client, RequestPath, new() { ["email"] = AnaEmail })).StatusCode);
        }

        var sentBefore = _factory.Notifier.Sent.Count;
        var overBudget = await PostJson(client, RequestPath, new() { ["email"] = AnaEmail });
        var unknown = await PostJson(client, RequestPath, new() { ["email"] = "ninguem@example.com" });

        Assert.Equal(30, sentBefore);
        Assert.Equal(sentBefore, _factory.Notifier.Sent.Count);
        Assert.Equal(HttpStatusCode.Accepted, overBudget.StatusCode);
        Assert.Equal(string.Empty, await overBudget.Content.ReadAsStringAsync());
        Assert.Equal(NormalizedHeaders(unknown), NormalizedHeaders(overBudget));
    }

    [Theory]
    [InlineData(GoogleEmail)]
    [InlineData("ninguem@example.com")]
    public async Task REQ028_request_response_is_identical_for_existing_google_only_and_unknown_emails(string email)
    {
        using var client = NewClient();
        var reference = await PostJson(client, RequestPath, new() { ["email"] = AnaEmail });

        var response = await PostJson(client, RequestPath, new() { ["email"] = email });

        Assert.Equal(HttpStatusCode.Accepted, reference.StatusCode);
        Assert.Equal(reference.StatusCode, response.StatusCode);
        Assert.Equal(await reference.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        Assert.Equal(NormalizedHeaders(reference), NormalizedHeaders(response));
    }

    [Fact]
    public async Task REQ028_request_enqueues_the_link_only_for_an_existing_account_with_local_password()
    {
        using var client = NewClient();

        await PostJson(client, RequestPath, new() { ["email"] = "ninguem@example.com" });
        await PostJson(client, RequestPath, new() { ["email"] = GoogleEmail });
        Assert.Empty(_factory.Notifier.Sent);

        var response = await PostJson(client, RequestPath, new() { ["email"] = AnaEmail });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sent = Assert.Single(_factory.Notifier.Sent);
        Assert.Equal(AnaEmail, sent.Email.Value);
        Assert.StartsWith($"{PublicOrigin}/redefinir-senha{TokenMarker}", sent.Link, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nao-e-um-email")]
    public async Task REQ028_request_with_invalid_email_format_returns_400_validation_failed(string? email)
    {
        using var client = NewClient();

        var response = await PostJson(client, RequestPath, new() { ["email"] = email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("email", out _));
        Assert.Empty(_factory.Notifier.Sent);
    }

    [Theory]
    [InlineData(AnaEmail)]
    [InlineData("ninguem@example.com")]
    public async Task REQ028_request_waits_the_fixed_minimum_duration_of_500ms_for_every_case(string email)
    {
        using var client = NewClient();

        var response = await PostJson(client, RequestPath, new() { ["email"] = email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(TimeSpan.FromMilliseconds(500), _factory.Delayer.Delays);
    }

    [Fact]
    public async Task REQ032_repeated_requests_never_block_and_always_return_202()
    {
        using var client = NewClient();

        for (var i = 0; i < 8; i++)
        {
            var response = await PostJson(client, RequestPath, new() { ["email"] = AnaEmail });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        Assert.NotEmpty(_factory.Delayer.Delays);
    }

    [Fact]
    public async Task REQ029_request_persists_only_the_token_hash_never_the_clear_token()
    {
        using var client = NewClient();

        await PostJson(client, RequestPath, new() { ["email"] = AnaEmail });

        var raw = RawTokenFromLastLink();
        var stored = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!
            .PasswordResetToken;
        Assert.NotNull(stored);
        Assert.Equal(new Sha256TokenHasher().Hash(raw), stored.TokenHash);
        Assert.NotEqual(Encoding.UTF8.GetBytes(raw), stored.TokenHash.ToBytes());
        Assert.DoesNotContain(raw, stored.TokenHash.ToString(), StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(60), stored.ExpiresAt - stored.IssuedAt);
    }

    [Fact]
    public async Task REQ030_confirm_with_valid_token_returns_204_and_the_old_password_stops_working()
    {
        using var client = NewClient();
        var raw = await RequestTokenAsync(client);

        var response = await PostJson(client, ConfirmPath, ConfirmBody(raw));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, AnaPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task REQ030_confirm_does_not_create_a_session()
    {
        using var client = NewClient();
        var raw = await RequestTokenAsync(client);

        var response = await PostJson(client, ConfirmPath, ConfirmBody(raw));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ030_token_is_single_use_and_the_second_confirm_returns_reset_link_invalid()
    {
        using var client = NewClient();
        var raw = await RequestTokenAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await PostJson(client, ConfirmPath, ConfirmBody(raw))).StatusCode);

        var second = await PostJson(client, ConfirmPath, ConfirmBody(raw, "outra-senha-8910"));

        await AssertResetLinkInvalid(second);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task REQ031_expired_token_returns_reset_link_invalid_and_keeps_the_old_password()
    {
        using var client = NewClient();
        var raw = await RequestTokenAsync(client);
        _factory.Clock.Advance(TimeSpan.FromMinutes(60));

        var response = await PostJson(client, ConfirmPath, ConfirmBody(raw));

        await AssertResetLinkInvalid(response);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, AnaPassword)).StatusCode);
    }

    [Theory]
    [InlineData("token-inexistente")]
    [InlineData("")]
    [InlineData(null)]
    public async Task REQ031_unknown_or_empty_token_returns_reset_link_invalid(string? token)
    {
        using var client = NewClient();

        var response = await PostJson(client, ConfirmPath, ConfirmBody(token));

        await AssertResetLinkInvalid(response);
    }

    [Fact]
    public async Task REQ030_a_new_request_replaces_the_previous_token()
    {
        using var client = NewClient();
        var first = await RequestTokenAsync(client);
        var second = await RequestTokenAsync(client);

        await AssertResetLinkInvalid(await PostJson(client, ConfirmPath, ConfirmBody(first)));
        Assert.Equal(HttpStatusCode.NoContent, (await PostJson(client, ConfirmPath, ConfirmBody(second))).StatusCode);
    }

    [Theory]
    [InlineData("curta12", "too_short")]
    [InlineData("", "required")]
    [InlineData("substituida-no-corpo", "too_long")]
    public async Task REQ038_confirm_with_password_outside_8_to_128_returns_400_validation_failed_without_consuming_the_token(
        string password, string expectedCode)
    {
        using var client = NewClient();
        var raw = await RequestTokenAsync(client);
        var body = ConfirmBody(raw, expectedCode == "too_long" ? new string('a', 129) : password);

        var response = await PostJson(client, ConfirmPath, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        var codes = doc.RootElement.GetProperty("errors").GetProperty("newPassword").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains(expectedCode, codes);
        Assert.Equal(HttpStatusCode.NoContent, (await PostJson(client, ConfirmPath, ConfirmBody(raw))).StatusCode);
    }

    [Fact]
    public async Task REQ030_confirm_revokes_every_session_of_the_player()
    {
        using var client = NewClient();
        var first = await LoginCookieAsync(client, AnaEmail, AnaPassword);
        var second = await LoginCookieAsync(client, AnaEmail, AnaPassword);
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, SessionPath, first)).StatusCode);
        var raw = await RequestTokenAsync(client);

        var response = await PostJson(client, ConfirmPath, ConfirmBody(raw));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(client, SessionPath, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(client, SessionPath, second)).StatusCode);
    }

    [Fact]
    public async Task REQ030_confirm_stores_a_new_password_hash_and_removes_the_token()
    {
        using var client = NewClient();
        var raw = await RequestTokenAsync(client);
        var before = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!
            .PasswordHash;

        await PostJson(client, ConfirmPath, ConfirmBody(raw));

        var player = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!;
        Assert.Null(player.PasswordResetToken);
        Assert.NotEqual(before, player.PasswordHash);
    }

    private static Dictionary<string, object?> ConfirmBody(string? token, string password = NewPassword) => new()
    {
        ["token"] = token,
        ["newPassword"] = password,
    };

    private string RawTokenFromLastLink()
    {
        var link = _factory.Notifier.Sent[^1].Link;
        return link[(link.IndexOf(TokenMarker, StringComparison.Ordinal) + TokenMarker.Length)..];
    }

    private async Task<string> RequestTokenAsync(HttpClient client)
    {
        var response = await PostJson(client, RequestPath, new() { ["email"] = AnaEmail });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return RawTokenFromLastLink();
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static Task<HttpResponseMessage> Login(HttpClient client, string password) =>
        PostJson(client, LoginPath, new() { ["email"] = AnaEmail, ["password"] = password, ["rememberMe"] = false });

    private static async Task<string> LoginCookieAsync(HttpClient client, string email, string password)
    {
        var response = await PostJson(
            client,
            LoginPath,
            new() { ["email"] = email, ["password"] = password, ["rememberMe"] = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static async Task<HttpResponseMessage> PostJson(
        HttpClient client, string path, Dictionary<string, object?>? body)
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

        request.Headers.Add("Cookie", csrfPair);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string cookiePair)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookiePair);
        return await client.SendAsync(request);
    }

    private static async Task AssertResetLinkInvalid(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("reset_link_invalid", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

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

    private sealed class ResetFactory : WebApplicationFactory<Program>
    {
        private const string Terms = "2026-10";

        public ResetFactory()
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

        public RecordingPasswordResetNotifier Notifier { get; } = new();

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
                services.RemoveAll<IPasswordResetNotifier>();
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
                services.AddSingleton<IPasswordResetNotifier>(Notifier);
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
