using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// T-078 (RED, habilitadora): protecao CSRF = antiforgery (GET /api/v1/csrf -> {token} + cookie
// __Host-dl_csrf) + cabecalho X-CSRF-Token + Origin igual a Auth:PublicOrigin; falha -> 403 csrf_failed.
public sealed class CsrfTests : IClassFixture<CsrfTests.CsrfFactory>
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string TokenHeader = "X-CSRF-Token";
    private const string MutatePath = "/__test/mutate";

    private readonly CsrfFactory _factory;

    public CsrfTests(CsrfFactory factory) => _factory = factory;

    public static TheoryData<string> MutatingMethods => new() { "POST", "PUT", "DELETE" };

    [Fact]
    public async Task T078_csrf_endpoint_returns_token_and_hardened_antiforgery_cookie()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/api/v1/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await ReadToken(response);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(MutatingMethods))]
    public async Task T078_mutation_without_csrf_token_is_rejected_with_csrf_failed(string method)
    {
        using var client = NewClient();
        var session = await StartSession(client);

        var response = await Send(client, method, session, token: null, origin: PublicOrigin);

        await AssertCsrfFailed(response);
    }

    [Theory]
    [MemberData(nameof(MutatingMethods))]
    public async Task T078_mutation_with_invalid_csrf_token_is_rejected_with_csrf_failed(string method)
    {
        using var client = NewClient();
        var session = await StartSession(client);

        var response = await Send(client, method, session, token: "token-invalido", origin: PublicOrigin);

        await AssertCsrfFailed(response);
    }

    [Theory]
    [MemberData(nameof(MutatingMethods))]
    public async Task T078_mutation_with_valid_token_and_public_origin_is_accepted(string method)
    {
        using var client = NewClient();
        var session = await StartSession(client);

        var response = await Send(client, method, session, session.Token, PublicOrigin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task T078_get_does_not_require_csrf_token()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/__test/ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(MutatingMethods))]
    public async Task T078_mutation_with_foreign_origin_is_rejected_even_with_valid_token(string method)
    {
        using var client = NewClient();
        var session = await StartSession(client);

        var response = await Send(client, method, session, session.Token, "https://evil.example.test");

        await AssertCsrfFailed(response);
    }

    // Emenda 2 do ADR-0002: Origin ausente so e aceito com token antiforgery valido (teste de regressao).
    [Theory]
    [MemberData(nameof(MutatingMethods))]
    public async Task T078_mutation_without_origin_and_with_valid_token_is_accepted(string method)
    {
        using var client = NewClient();
        var session = await StartSession(client);

        var response = await Send(client, method, session, session.Token, origin: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(MutatingMethods))]
    public async Task T078_mutation_without_origin_and_without_valid_token_is_rejected_with_csrf_failed(string method)
    {
        using var client = NewClient();
        var session = await StartSession(client);

        await AssertCsrfFailed(await Send(client, method, session, token: null, origin: null));
        await AssertCsrfFailed(await Send(client, method, session, token: "token-invalido", origin: null));
    }

    [Fact]
    public async Task T078_token_from_one_session_is_not_valid_in_another()
    {
        using var clientA = NewClient();
        using var clientB = NewClient();
        var sessionA = await StartSession(clientA);
        var sessionB = await StartSession(clientB);

        var crossed = await Send(clientB, "POST", sessionB, sessionA.Token, PublicOrigin);
        var own = await Send(clientB, "POST", sessionB, sessionB.Token, PublicOrigin);

        await AssertCsrfFailed(crossed);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    // Decisao aprovada: o token anti-CSRF e anonimo (vinculado so ao cookie __Host-dl_csrf), entao o MESMO par
    // token + cookie obtido antes do login continua valido no login e no logout autenticado.
    [Fact]
    public async Task T078_anonymous_csrf_token_stays_valid_after_login_and_logout()
    {
        using var factory = new LoginCsrfFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        var csrf = await StartSession(client);

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = LoginCsrfFactory.Email, password = LoginCsrfFactory.PlainPassword, rememberMe = false }),
                Encoding.UTF8,
                "application/json"),
        };
        login.Headers.Add("Cookie", csrf.CookiePair);
        login.Headers.Add("Origin", PublicOrigin);
        login.Headers.Add(TokenHeader, csrf.Token);
        var loginResponse = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var sessionPair = Assert.Single(
            SetCookies(loginResponse), c => c.StartsWith("__Host-dl_session=", StringComparison.Ordinal)).Split(';')[0];

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", $"{csrf.CookiePair}; {sessionPair}");
        logout.Headers.Add("Origin", PublicOrigin);
        logout.Headers.Add(TokenHeader, csrf.Token);
        var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var invalid = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        invalid.Headers.Add("Cookie", $"{csrf.CookiePair}; {sessionPair}");
        invalid.Headers.Add("Origin", PublicOrigin);
        invalid.Headers.Add(TokenHeader, "token-invalido");
        await AssertCsrfFailed(await client.SendAsync(invalid));
    }

    // SEC-04: atras do proxy a API recebe HTTP puro (o X-Forwarded-Proto e ignorado, ADR-0016). O cookie antiforgery
    // e Secure pelo esquema de Auth:PublicOrigin, nao pelo esquema da requisicao, e /csrf nao responde 500.
    [Fact]
    public async Task SEC04_behind_the_proxy_a_plain_http_request_gets_the_csrf_token_and_a_secure_cookie_when_the_public_origin_is_https()
    {
        using var factory = new OriginFactory(PublicOrigin);
        using var client = PlainHttpClient(factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync("/api/v1/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SEC04_behind_the_proxy_a_plain_http_mutation_with_the_token_pair_is_accepted()
    {
        using var factory = new OriginFactory(PublicOrigin);
        using var client = PlainHttpClient(factory);
        var session = await StartSession(client);

        var response = await Send(client, "POST", session, session.Token, PublicOrigin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // SEC-18: o prefixo __Host- exige Secure; sem ele o navegador recusa o cookie e toda mutacao vira 403 no compose por
    // http. Como os cookies de sessao e do Google, sai sempre Secure (http://localhost e contexto seguro).
    [Fact]
    public async Task SEC18_with_an_http_public_origin_in_development_the_antiforgery_cookie_is_still_secure()
    {
        using var factory = new OriginFactory("http://localhost:5173");
        using var client = PlainHttpClient(factory);

        var response = await client.GetAsync("/api/v1/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient PlainHttpClient(OriginFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false,
        });

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static async Task<CsrfSession> StartSession(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await ReadToken(response);
        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal));
        return new CsrfSession(token, cookie.Split(';')[0]);
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, string method, CsrfSession session, string? token, string? origin)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), MutatePath);
        request.Headers.Add("Cookie", session.CookiePair);
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (token is not null)
        {
            request.Headers.Add(TokenHeader, token);
        }

        return await client.SendAsync(request);
    }

    private static async Task AssertCsrfFailed(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("csrf_failed", doc.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> ReadToken(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString() ?? string.Empty;
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private sealed record CsrfSession(string Token, string CookiePair);

    public class CsrfFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.AddSingleton<IStartupFilter, TestEndpointsFilter>();
            });
        }
    }

    private sealed class OriginFactory(string publicOrigin) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", publicOrigin);
            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.AddSingleton<IStartupFilter, TestEndpointsFilter>();
            });
        }
    }

    private sealed class LoginCsrfFactory : WebApplicationFactory<Program>
    {
        public const string Email = "ana@example.com";
        public const string PlainPassword = "senha-valida-123";
        private const string Terms = "2026-10";

        public LoginCsrfFactory()
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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
            builder.ConfigureServices(services =>
            {
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
                services.AddSingleton<IUnitOfWork>(Uow);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(Sessions);
                services.AddSingleton<IAccessAttemptStore>(Attempts);
                services.AddSingleton<IPasswordHasher>(Hasher);
                services.AddSingleton<IKeyedHasher>(new FakeKeyedHasher());
                services.AddSingleton<IDelayer>(new RecordingDelayer());
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
            var password = await Hasher.HashAsync(Deckloom.Accounts.Domain.Password.Create(PlainPassword).Value);
            var ana = Player.RegisterWithPassword(
                Deckloom.Accounts.Domain.Email.Create(Email).Value,
                password,
                DisplayName.Create("Ana").Value,
                Terms,
                Terms,
                true,
                true,
                TimeProvider.System.GetUtcNow(),
                "preset-01").Value;
            await Players.Add(ana);
            await Uow.CommitAsync();
        }
    }

    private sealed class TestEndpointsFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.UseRouting();
                app.UseEndpoints(MapTestEndpoints);
            };

        private static void MapTestEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/__test/ok", () => Results.Ok(new { ok = true }));
            endpoints.MapPost(MutatePath, () => Results.Ok(new { ok = true }));
            endpoints.MapPut(MutatePath, () => Results.Ok(new { ok = true }));
            endpoints.MapDelete(MutatePath, () => Results.Ok(new { ok = true }));
        }
    }
}
