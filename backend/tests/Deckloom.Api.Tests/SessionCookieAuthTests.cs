using System.Net;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.Api.Problems;
using Deckloom.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// T-080 (RED): autenticacao do host por cookie de sessao __Host-dl_session (design: Cookies, S2-S4).
// Lacunas assumidas: (1) o endpoint real de login ainda nao esta ligado ao host, entao a rota de teste
// /__test/login cria a sessao pelos repositorios e emite o cookie via SessionCookieService (emissao e
// expiracao); (2) rotas de teste usam GET para nao cair na protecao CSRF (T-078); (3) a rota protegida autentica
// por AuthenticateSession e devolve 401 unauthenticated (R-15: o host nao registra esquema de autenticacao).
public sealed class SessionCookieAuthTests : IDisposable
{
    private const string CookieName = "__Host-dl_session";
    private const string ProtectedPath = "/__test/protected";

    private readonly SessionFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ010_login_sets_host_session_cookie_hardened_without_max_age()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/__test/login?remember=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = SessionSetCookie(response);
        Assert.StartsWith(CookieName + "=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ012_login_with_remember_me_sets_thirty_day_max_age()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/__test/login?remember=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=2592000", SessionSetCookie(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ011_protected_route_accepts_a_valid_session_cookie()
    {
        using var client = NewClient();
        var cookie = await LoginAsync(client);

        var response = await Get(client, ProtectedPath, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task REQ011_protected_route_without_cookie_is_unauthenticated()
    {
        using var client = NewClient();

        var response = await Get(client, ProtectedPath, cookiePair: null);

        await AssertUnauthenticated(response);
    }

    [Theory]
    [InlineData("token-desconhecido")]
    [InlineData("")]
    [InlineData("tok-0001%00")]
    public async Task REQ011_protected_route_with_unknown_token_is_unauthenticated(string token)
    {
        using var client = NewClient();
        await LoginAsync(client);

        var response = await Get(client, ProtectedPath, $"{CookieName}={token}");

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task REQ012_protected_route_with_expired_session_is_unauthenticated()
    {
        using var client = NewClient();
        var cookie = await LoginAsync(client);

        _factory.Clock.Advance(TimeSpan.FromMinutes(31));
        var response = await Get(client, ProtectedPath, cookie);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task REQ012_valid_request_renews_the_sliding_thirty_minute_window()
    {
        using var client = NewClient();
        var cookie = await LoginAsync(client);

        _factory.Clock.Advance(TimeSpan.FromMinutes(20));
        var first = await Get(client, ProtectedPath, cookie);
        _factory.Clock.Advance(TimeSpan.FromMinutes(20));
        var second = await Get(client, ProtectedPath, cookie);
        _factory.Clock.Advance(TimeSpan.FromMinutes(31));
        var third = await Get(client, ProtectedPath, cookie);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await AssertUnauthenticated(third);
    }

    [Fact]
    public async Task REQ035_protected_route_with_revoked_session_is_unauthenticated()
    {
        using var client = NewClient();
        var cookie = await LoginAsync(client);
        await _factory.RevokeAllAsync();

        var response = await Get(client, ProtectedPath, cookie);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task REQ035_only_the_token_hash_is_persisted_never_the_plain_token()
    {
        using var client = NewClient();
        var cookie = await LoginAsync(client);
        var token = cookie[(CookieName.Length + 1)..];

        var stored = await _factory.Sessions.FindByTokenHash(_factory.Hasher.Hash(token));

        Assert.NotNull(stored);
        Assert.Equal(_factory.Hasher.Hash(token), stored.TokenHash);
        Assert.NotEqual(Encoding.UTF8.GetBytes(token), stored.TokenHash.ToBytes());
        Assert.DoesNotContain(token, stored.TokenHash.ToString(), StringComparison.Ordinal);
        Assert.Equal(TokenHash.Length, stored.TokenHash.ToBytes().Length);
    }

    [Fact]
    public async Task REQ035_logout_expires_the_cookie_and_invalidates_it_immediately()
    {
        using var client = NewClient();
        var cookie = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, ProtectedPath, cookie)).StatusCode);

        var logout = await Get(client, "/__test/logout", cookie);
        var after = await Get(client, ProtectedPath, cookie);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        var expired = SessionSetCookie(logout);
        Assert.StartsWith(CookieName + "=;", expired, StringComparison.Ordinal);
        Assert.True(
            expired.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)
            || expired.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase),
            expired);
        Assert.Contains("secure", expired, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", expired, StringComparison.OrdinalIgnoreCase);
        await AssertUnauthenticated(after);
    }

    [Fact]
    public async Task REQ035_authenticated_response_is_not_cacheable()
    {
        using var client = NewClient();
        var cookie = await LoginAsync(client);

        var response = await Get(client, ProtectedPath, cookie);

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.GetAsync("/__test/login?remember=false");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SessionSetCookie(response).Split(';')[0];
    }

    private static string SessionSetCookie(HttpResponseMessage response)
    {
        var values = response.Headers.TryGetValues("Set-Cookie", out var all) ? all : [];
        return Assert.Single(values, c => c.StartsWith(CookieName + "=", StringComparison.Ordinal));
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
        Assert.DoesNotContain("ana@example.com", json, StringComparison.Ordinal);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class SessionFactory : WebApplicationFactory<Program>
    {
        private const string Terms = "2026-10-01";
        private const string Stored =
            "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

        public SessionFactory()
        {
            Sessions = new InMemorySessionRepository(Uow);
            Players = new InMemoryPlayerRepository(Uow);
            Player = Deckloom.Accounts.Domain.Player.RegisterWithPassword(
                Email.Create("ana@example.com").Value,
                PasswordHash.Parse(Stored).Value,
                DisplayName.Create("Ana").Value,
                Terms,
                Terms,
                true,
                true,
                Clock.GetUtcNow(),
                "preset-01").Value;
            Players.Add(Player).GetAwaiter().GetResult();
            Uow.CommitAsync().GetAwaiter().GetResult();
        }

        public TestClock Clock { get; } = new();

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemorySessionRepository Sessions { get; }

        public InMemoryPlayerRepository Players { get; }

        public Sha256TokenHasher Hasher { get; } = new();

        public Player Player { get; }

        public async Task RevokeAllAsync() =>
            await Sessions.RevokeAllFor(Player.Id, SessionRevocationReason.PasswordReset, Clock.GetUtcNow())
                .ContinueWith(_ => Uow.CommitAsync(), TaskScheduler.Default).Unwrap();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", "https://deckloom.example.test");
            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton(Uow);
                services.AddSingleton<IUnitOfWork>(Uow);
                services.AddSingleton<ISessionRepository>(Sessions);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ITokenHasher>(Hasher);
                services.AddSingleton<ISecureTokenGenerator, SequentialTokenGenerator>();
                services.AddSingleton<IAvatarPresetCatalog>(new InMemoryAvatarPresetCatalog(
                    [new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true)]));
                services.AddSingleton<AuthenticateSession>();
                services.AddSingleton<Logout>();
                services.TryAddSingleton<SessionCookieService>();
                services.AddSingleton<IStartupFilter>(new TestEndpointsFilter(this));
            });
        }
    }

    private sealed class TestEndpointsFilter(SessionFactory factory) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.UseRouting();
                app.UseEndpoints(MapTestEndpoints);
            };

        private void MapTestEndpoints(IEndpointRouteBuilder endpoints)
        {
            // Como os endpoints reais: autentica uma vez pelo caso de uso AuthenticateSession (sem esquema no pipeline).
            endpoints.MapGet(ProtectedPath, async (HttpContext context, AuthenticateSession authenticate) =>
            {
                var result = await authenticate.ExecuteAsync(
                    new AuthenticateSessionCommand(context.Request.Cookies[CookieName]), context.RequestAborted);
                if (result.IsFailure)
                {
                    return DomainErrorProblemMapper.ToResult(result.Errors);
                }

                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new { ok = true });
            });
            endpoints.MapGet("/__test/login", async (HttpContext context, bool remember) =>
            {
                var services = context.RequestServices;
                var raw = services.GetRequiredService<ISecureTokenGenerator>().Generate();
                var session = Session.Start(
                    factory.Player.Id, factory.Hasher.Hash(raw), remember, factory.Clock.GetUtcNow());
                await factory.Uow.ExecuteAsync(ct => factory.Sessions.Add(session, ct));
                var summary = new SessionSummary(
                    "Ana", new AvatarView(AvatarView.PresetKind, "preset-01", "/avatars/preset-01.png", "Avatar: Dragao"),
                    true, false);
                services.GetRequiredService<SessionCookieService>()
                    .Issue(context, new AuthenticatedSession(raw, session.ExpiresAt, summary), remember);
                return Results.Ok(new { ok = true });
            });
            endpoints.MapGet("/__test/logout", async (HttpContext context) =>
            {
                var services = context.RequestServices;
                var token = context.Request.Cookies[CookieName];
                await services.GetRequiredService<Logout>().ExecuteAsync(new LogoutCommand(token));
                services.GetRequiredService<SessionCookieService>().Expire(context);
                return Results.Ok(new { ok = true });
            });
        }
    }
}
