using System.Net;
using System.Web;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// T-085 (RED): GET /api/v1/auth/google/start e GET /api/v1/auth/google/callback
// (design, tabela "Endpoints", tabela "Cookies" e bloco "Forma da reautenticacao Google";
// REQ-006, 007, 008, 009, 023, 042, 043).
// Lacunas assumidas: (1) o Location do retorno e /entrar/google/retorno?resultado=<ramo>, sem token, email, subject
// nem code/state (o returnTo de sucesso nao esta fixado na spec); (2) o cookie __Host-dl_oauth e opaco para o
// teste (cifrado): o teste apenas o devolve ao callback; (3) prompt=login e max_age=300 devem estar na URL de
// autorizacao (a mudanca da porta IGoogleIdentityVerifier e do implementer); (4) PKCE S256 exige
// code_challenge_method=S256 na URL, que o fake do Testing hoje nao emite; (5) verificador = FakeGoogleIdentityVerifier;
// (6) start reauth sem sessao: 302 para o retorno com resultado=falhou (design.md, contrato), nunca redirecionar ao Google nem gravar cookie.
public sealed class GoogleEndpointsTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string OAuthCookie = "__Host-dl_oauth";
    private const string PendingCookie = "__Host-dl_pending";
    private const string SessionCookie = "__Host-dl_session";
    private const string StartPath = "/api/v1/auth/google/start";
    private const string CallbackPath = "/api/v1/auth/google/callback";
    private const string ReturnPath = "/entrar/google/retorno";
    private const string SeedSessionToken = "seed-session-token";
    private const string GuiEmail = "gui@example.com";
    private const string AnaEmail = "ana@example.com";
    private const string NewEmail = "novo@example.com";

    private readonly GoogleFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ006_start_redirects_302_to_google_with_state_nonce_and_pkce_s256()
    {
        using var client = NewClient();

        var response = await client.GetAsync($"{StartPath}?intent=signin");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("accounts.google.test", response.Headers.Location.Host);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Null(query["prompt"]);
        Assert.Null(query["max_age"]);
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ006_start_sets_the_oauth_cookie_httponly_secure_samesite_lax_path_root_max_age_600()
    {
        using var client = NewClient();

        var response = await client.GetAsync($"{StartPath}?intent=signin");

        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=600", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ006_start_does_not_put_the_state_in_clear_inside_the_oauth_cookie()
    {
        using var client = NewClient();

        var response = await client.GetAsync($"{StartPath}?intent=signin");

        var state = HttpUtility.ParseQueryString(response.Headers.Location!.Query)["state"]!;
        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(state, cookie, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("\\evil.example")]
    [InlineData("/rota-que-nao-existe")]
    public async Task REQ006_start_ignores_an_external_or_unknown_returnTo_on_the_server(string returnTo)
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, $"intent=signin&returnTo={Uri.EscapeDataString(returnTo)}");
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith(ReturnPath + "?", location, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rota-que-nao-existe", location, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/perfil/editar")]
    [InlineData("/perfil")]
    public async Task R34_callback_ok_returns_the_known_internal_returnTo_carried_in_the_cookie(string returnTo)
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, $"intent=signin&returnTo={Uri.EscapeDataString(returnTo)}");
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        AssertRedirect(response, "ok");
        var location = response.Headers.Location!.OriginalString;
        var query = HttpUtility.ParseQueryString(location[(location.IndexOf('?', StringComparison.Ordinal) + 1)..]);
        Assert.Equal(returnTo, query["retorno"]);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("\\evil.example")]
    [InlineData("/rota-que-nao-existe")]
    [InlineData("/entrar/google/retorno")]
    public async Task R34_callback_ok_drops_an_unsafe_returnTo_and_leaves_the_default_to_the_front(string returnTo)
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, $"intent=signin&returnTo={Uri.EscapeDataString(returnTo)}");
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        AssertRedirect(response, "ok");
        Assert.DoesNotContain("retorno=", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R34_reauth_start_ignores_the_returnTo()
    {
        using var client = NewClient();
        var sessionCookie = $"{SessionCookie}={SeedSessionToken}";
        var (cookie, state) = await StartAsync(client, "intent=reauth&returnTo=%2Fperfil%2Feditar", sessionCookie);
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail, authTime: _factory.Clock.GetUtcNow()));

        var response = await CallbackAsync(client, "codigo-1", state, cookie, sessionCookie);

        Assert.DoesNotContain("retorno=", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task REQ042_start_with_intent_reauth_without_a_valid_session_is_refused_without_going_to_google()
    {
        using var client = NewClient();

        var response = await client.GetAsync($"{StartPath}?intent=reauth");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"{ReturnPath}?resultado=falhou", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("accounts.google.test", response.Headers.Location?.OriginalString ?? string.Empty);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ042_start_with_intent_reauth_and_a_valid_session_asks_google_for_prompt_login_and_max_age_300()
    {
        using var client = NewClient();

        var response = await GetWithCookie(client, $"{StartPath}?intent=reauth", $"{SessionCookie}={SeedSessionToken}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("accounts.google.test", response.Headers.Location.Host);
        Assert.Equal("login", query["prompt"]);
        Assert.Equal("300", query["max_age"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.Single(SetCookies(response), c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal));
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ009_callback_with_a_diverging_state_redirects_to_falhou_and_creates_nothing()
    {
        using var client = NewClient();
        var (cookie, _) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-novo", NewEmail));

        var response = await CallbackAsync(client, "codigo-1", "state-adulterado", cookie);

        AssertRedirect(response, "falhou");
        Assert.Empty(_factory.Verifier.CompleteCalls);
        AssertNothingCreated(response);
        Assert.Null(await _factory.Players.FindByGoogleSubject("sub-novo"));
    }

    [Fact]
    public async Task REQ009_callback_without_the_oauth_cookie_redirects_to_falhou()
    {
        using var client = NewClient();
        var (_, state) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail));

        var response = await CallbackAsync(client, "codigo-1", state, cookie: null);

        AssertRedirect(response, "falhou");
        Assert.Empty(_factory.Verifier.CompleteCalls);
        AssertNothingCreated(response);
    }

    [Fact]
    public async Task REQ009_callback_with_google_error_redirects_to_falhou()
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, "intent=signin");

        var response = await GetWithCookie(
            client, $"{CallbackPath}?error=access_denied&state={Uri.EscapeDataString(state)}", cookie);

        AssertRedirect(response, "falhou");
        AssertNothingCreated(response);
    }

    [Fact]
    public async Task REQ009_callback_with_unverified_email_redirects_to_the_uniform_falhou_without_pending()
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-novo", NewEmail, emailVerified: false));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        AssertRedirect(response, "falhou");
        AssertNothingCreated(response);
        Assert.Null(await _factory.Players.FindByGoogleSubject("sub-novo"));
    }

    [Fact]
    public async Task REQ007_callback_with_a_new_identity_goes_to_concluir_with_the_pending_cookie_and_no_account_or_session()
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-novo", NewEmail, name: "Nova Pessoa"));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        AssertRedirect(response, "concluir");
        var pending = Assert.Single(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", pending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=600", pending, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Null(await _factory.Players.FindByGoogleSubject("sub-novo"));
        var stored = await _factory.Pending.FindAsync(HashOf(CookieValue(pending)));
        Assert.IsType<PendingGoogleSignup>(stored);
        AssertNoSensitiveData(response, NewEmail, "sub-novo", CookieValue(pending));
    }

    [Fact]
    public async Task REQ008_callback_with_an_email_of_a_password_account_goes_to_vincular_without_session_or_link()
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-ana", AnaEmail));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        AssertRedirect(response, "vincular");
        var pending = Assert.Single(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Null(await _factory.Players.FindByGoogleSubject("sub-ana"));
        Assert.IsType<PendingGoogleLink>(await _factory.Pending.FindAsync(HashOf(CookieValue(pending))));
        AssertNoSensitiveData(response, AnaEmail, "sub-ana", CookieValue(pending));
    }

    [Fact]
    public async Task REQ007_callback_with_an_already_linked_identity_logs_in_and_opens_a_session()
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        AssertRedirect(response, "ok");
        var session = Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await _factory.Sessions.FindByTokenHash(HashOf(CookieValue(session))));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
        AssertNoSensitiveData(response, GuiEmail, "sub-gui", CookieValue(session));
    }

    [Fact]
    public async Task REQ006_callback_always_deletes_the_oauth_cookie_so_it_is_single_use()
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail));

        var response = await CallbackAsync(client, "codigo-1", state, cookie);

        var deleted = Assert.Single(SetCookies(response), c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal));
        Assert.True(
            deleted.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)
            || deleted.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        AssertNoStore(response);
    }

    [Fact]
    public async Task REQ006_callback_forwards_the_code_the_pkce_verifier_and_the_nonce_to_the_verifier()
    {
        using var client = NewClient();
        var (cookie, state) = await StartAsync(client, "intent=signin");
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail));

        await CallbackAsync(client, "codigo-xyz", state, cookie);

        var call = Assert.Single(_factory.Verifier.CompleteCalls);
        Assert.Equal("codigo-xyz", call.Code);
        Assert.False(string.IsNullOrEmpty(call.CodeVerifier));
        Assert.False(string.IsNullOrEmpty(call.Nonce));
    }

    [Fact]
    public async Task REQ042_callback_reauth_marks_reauthenticated_at_and_opens_no_new_session()
    {
        using var client = NewClient();
        var sessionCookie = $"{SessionCookie}={SeedSessionToken}";
        var (cookie, state) = await StartAsync(client, "intent=reauth", sessionCookie);
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail, authTime: _factory.Clock.GetUtcNow()));

        var response = await CallbackAsync(client, "codigo-1", state, cookie, sessionCookie);

        AssertRedirect(response, "reautenticado");
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        var session = await _factory.Sessions.FindByTokenHash(HashOf(SeedSessionToken));
        Assert.Equal(_factory.Clock.GetUtcNow(), session!.ReauthenticatedAt);
        AssertNoSensitiveData(response, GuiEmail, "sub-gui", SeedSessionToken);
    }

    [Fact]
    public async Task R15_callback_reauth_does_not_renew_the_session_window()
    {
        using var client = NewClient();
        var sessionCookie = $"{SessionCookie}={SeedSessionToken}";
        var (cookie, state) = await StartAsync(client, "intent=reauth", sessionCookie);
        var seededAt = _factory.Clock.GetUtcNow();
        _factory.Clock.Advance(TimeSpan.FromMinutes(2));
        _factory.Verifier.Returns(Identity("sub-gui", GuiEmail, authTime: _factory.Clock.GetUtcNow()));

        var response = await CallbackAsync(client, "codigo-1", state, cookie, sessionCookie);

        AssertRedirect(response, "reautenticado");
        var session = await _factory.Sessions.FindByTokenHash(HashOf(SeedSessionToken));
        Assert.Equal(seededAt, session!.LastSeenAt);
    }

    [Fact]
    public async Task R15_a_public_route_with_a_session_cookie_does_not_touch_the_session()
    {
        using var client = NewClient();
        var seededAt = _factory.Clock.GetUtcNow();
        _factory.Clock.Advance(TimeSpan.FromMinutes(2));

        var response = await GetWithCookie(client, "/api/v1/csrf", $"{SessionCookie}={SeedSessionToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await _factory.Sessions.FindByTokenHash(HashOf(SeedSessionToken));
        Assert.Equal(seededAt, session!.LastSeenAt);
    }

    [Fact]
    public async Task REQ042_callback_reauth_with_a_subject_of_another_account_fails_and_marks_nothing()
    {
        using var client = NewClient();
        var sessionCookie = $"{SessionCookie}={SeedSessionToken}";
        var (cookie, state) = await StartAsync(client, "intent=reauth", sessionCookie);
        _factory.Verifier.Returns(Identity("sub-outro", "outro@example.com", authTime: _factory.Clock.GetUtcNow()));

        var response = await CallbackAsync(client, "codigo-1", state, cookie, sessionCookie);

        AssertRedirect(response, "reautenticacao-falhou");
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        var session = await _factory.Sessions.FindByTokenHash(HashOf(SeedSessionToken));
        Assert.Null(session!.ReauthenticatedAt);
    }

    private static void AssertRedirect(HttpResponseMessage response, string resultado)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith(ReturnPath + "?", location, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(location[(location.IndexOf('?', StringComparison.Ordinal) + 1)..]);
        Assert.Equal(resultado, query["resultado"]);
        AssertNoStore(response);
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

    private static void AssertNoSensitiveData(HttpResponseMessage response, params string[] secrets)
    {
        var location = Uri.UnescapeDataString(response.Headers.Location!.OriginalString);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, location, StringComparison.OrdinalIgnoreCase);
        }

        var query = HttpUtility.ParseQueryString(location[(location.IndexOf('?', StringComparison.Ordinal) + 1)..]);
        Assert.DoesNotContain(
            query.AllKeys,
            k => k is "token" or "code" or "state" or "email" or "access_token" or "id_token");
    }

    private static void AssertNothingCreated(HttpResponseMessage response)
    {
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal));
    }

    private static TokenHash HashOf(string token) => new Sha256TokenHasher().Hash(token);

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';')[0][(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

    private static GoogleVerifiedIdentity Identity(
        string subject, string email, bool emailVerified = true, string? name = "Pessoa", DateTimeOffset? authTime = null) =>
        new(subject, Deckloom.Accounts.Domain.Email.Create(email).Value, emailVerified, name, authTime);

    private static async Task<(string Cookie, string State)> StartAsync(
        HttpClient client, string query, string? extraCookie = null)
    {
        var response = extraCookie is null
            ? await client.GetAsync($"{StartPath}?{query}")
            : await GetWithCookie(client, $"{StartPath}?{query}", extraCookie);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var state = HttpUtility.ParseQueryString(response.Headers.Location!.Query)["state"]!;
        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
        return (cookie, state);
    }

    private static Task<HttpResponseMessage> CallbackAsync(
        HttpClient client, string code, string state, string? cookie, string? sessionCookie = null)
    {
        var path = $"{CallbackPath}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
        var cookies = string.Join("; ", new[] { cookie, sessionCookie }.Where(c => c is not null));
        return string.IsNullOrEmpty(cookies) ? client.GetAsync(path) : GetWithCookie(client, path, cookies);
    }

    private static async Task<HttpResponseMessage> GetWithCookie(HttpClient client, string path, string cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookies);
        return await client.SendAsync(request);
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class GoogleFactory : WebApplicationFactory<Program>
    {
        private const string Terms = "2026-10";

        public GoogleFactory()
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

        public FakeGoogleIdentityVerifier Verifier { get; } = new();

        public FixedClock Clock { get; } = new();

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
                services.AddSingleton<IGoogleIdentityVerifier>(Verifier);
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
            var password = await hasher.HashAsync(Deckloom.Accounts.Domain.Password.Create("senha-valida-123").Value);
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
                Deckloom.Accounts.Domain.Email.Create(GuiEmail).Value,
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
            await Sessions.Add(Session.Start(gui.Id, new Sha256TokenHasher().Hash(SeedSessionToken), false, now));
            await Uow.CommitAsync();
        }
    }
}
