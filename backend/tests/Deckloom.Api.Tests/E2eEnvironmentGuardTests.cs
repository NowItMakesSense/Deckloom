extern alias E2eHost;
using System.Net;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;
using Deckloom.Api.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using E2eHostMarker = E2eHost::Deckloom.Api.E2E.Host.E2eHostMarker;

namespace Deckloom.Api.Tests;

// T-121 (RED): ambiente E2E (design, "Estrategia de testes" > E2E; tasks.md, T-121).
// Lacunas assumidas (ver relatorio): (1) o host de teste e o projeto backend/tests/Deckloom.Api.E2E.Host, com tipo
// de entrada publico E2eHostMarker (WebApplicationFactory); (2) rotas: GET /__test/password-reset/last-link?email=
// (200 + JSON {"link": "..."}) e POST /__test/clock/advance com corpo {"seconds": N} (2xx); (3) "sem ambiente" =
// ambiente vazio (o padrao do ASP.NET vira Production); (4) so ha valores FICTICIOS nos arquivos de configuracao E2E.
public sealed class E2eEnvironmentGuardTests
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string Terms = "2026-10";
    private const string LastLinkPath = "/__test/password-reset/last-link?email=ana@example.com";
    private const string AdvancePath = "/__test/clock/advance";
    private const string ResetPath = "/__test/clock/reset";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("")]
    public async Task T121_test_endpoints_return_404_outside_the_E2E_environment(string environment)
    {
        using var factory = new E2eHostFactory(environment);
        using var client = NewClient(factory);

        var lastLink = await client.GetAsync(LastLinkPath);
        var advance = await client.PostAsync(AdvancePath, Json(new { seconds = 60 }));
        var reset = await client.PostAsync(ResetPath, null);

        Assert.Equal(HttpStatusCode.NotFound, lastLink.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, advance.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("")]
    public void T121_clock_is_not_a_fake_outside_the_E2E_environment(string environment)
    {
        using var factory = new E2eHostFactory(environment);

        using var scope = factory.Services.CreateScope();

        Assert.IsNotType<FakeTimeProvider>(scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task T121_clock_advance_moves_the_FakeTimeProvider_in_E2E()
    {
        using var factory = new E2eHostFactory("E2E");
        using var client = NewClient(factory);
        var clock = Assert.IsType<FakeTimeProvider>(factory.Services.GetRequiredService<TimeProvider>());
        var before = clock.GetUtcNow();

        var response = await client.PostAsync(AdvancePath, Json(new { seconds = 601 }));

        Assert.True(response.IsSuccessStatusCode, $"status {(int)response.StatusCode}");
        Assert.Equal(before.AddSeconds(601), clock.GetUtcNow());
    }

    // T-126: o reset devolve o relogio ao ponto inicial do host E2E (isola cenarios que avancam o tempo).
    [Fact]
    public async Task T126_clock_reset_returns_the_FakeTimeProvider_to_the_initial_point_in_E2E()
    {
        using var factory = new E2eHostFactory("E2E");
        using var client = NewClient(factory);
        var clock = Assert.IsType<FakeTimeProvider>(factory.Services.GetRequiredService<TimeProvider>());
        var initial = clock.GetUtcNow();
        await client.PostAsync(AdvancePath, Json(new { seconds = 360 }));

        var response = await client.PostAsync(ResetPath, null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.InRange(clock.GetUtcNow(), initial, initial.AddSeconds(1));
    }

    [Fact]
    public async Task T121_clock_advance_expires_the_session_in_E2E()
    {
        using var factory = new E2eHostFactory("E2E");
        using var client = NewClient(factory);
        var cookie = await RegisterAndGetSessionCookieAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await GetWithCookie(client, "/api/v1/auth/session", cookie)).StatusCode);

        var advance = await client.PostAsync(AdvancePath, Json(new { seconds = 365 * 24 * 3600 }));

        Assert.True(advance.IsSuccessStatusCode, $"status {(int)advance.StatusCode}");
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await GetWithCookie(client, "/api/v1/auth/session", cookie)).StatusCode);
    }

    [Fact]
    public async Task T121_last_reset_link_endpoint_returns_the_link_sent_to_the_player_in_E2E()
    {
        using var factory = new E2eHostFactory("E2E");
        using var client = NewClient(factory);
        await RegisterAndGetSessionCookieAsync(client);
        var request = await PostJson(client, "/api/v1/auth/password-reset/request", new { email = "ana@example.com" });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);

        var response = await PollLastLink(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("link")
            .GetString();
        Assert.StartsWith($"{PublicOrigin}/redefinir-senha#token=", link, StringComparison.Ordinal);
    }

    [Fact]
    public void T121_E2E_uses_in_memory_persistence()
    {
        using var factory = new E2eHostFactory("E2E");
        using var scope = factory.Services.CreateScope();

        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();

        Assert.IsType<InMemoryPlayerRepository>(players);
        Assert.IsType<InMemorySessionRepository>(sessions);
        Assert.Null(scope.ServiceProvider.GetService<IDatabaseRoleInspector>());
    }

    [Fact]
    public void T121_E2E_uses_the_StubGoogleOidcServer_as_the_Google_authority()
    {
        using var factory = new E2eHostFactory("E2E");

        var authority = new Uri(factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.Google.Authority);

        Assert.True(authority.IsLoopback, $"Authority {authority} deve apontar para o StubGoogleOidcServer local.");
        Assert.NotEqual("accounts.google.com", authority.Host);
    }

    [Fact]
    public void T121_E2E_uses_the_real_SkiaSharp_avatar_processor()
    {
        using var factory = new E2eHostFactory("E2E");

        var processor = factory.Services.GetRequiredService<IAvatarImageProcessor>();

        Assert.IsType<SkiaAvatarImageProcessor>(processor);
    }

    [Fact]
    public void T121_E2E_host_project_is_part_of_the_solution_and_outside_production_artifact()
    {
        var backend = BackendDirectory();
        var solution = File.ReadAllText(Path.Combine(backend, "Deckloom.sln"));
        var hostCsproj = Path.Combine(backend, "tests", "Deckloom.Api.E2E.Host", "Deckloom.Api.E2E.Host.csproj");
        var production = File.ReadAllText(Path.Combine(backend, "src", "Deckloom.Api", "Deckloom.Api.csproj"));

        Assert.Contains("Deckloom.Api.E2E.Host.csproj", solution, StringComparison.Ordinal);
        var host = File.ReadAllText(hostCsproj);
        Assert.Contains("Deckloom.Api.csproj", host, StringComparison.Ordinal);
        Assert.Contains("Deckloom.Accounts.Testing.csproj", host, StringComparison.Ordinal);
        Assert.DoesNotContain("E2E.Host", production, StringComparison.Ordinal);
        Assert.DoesNotContain(".Testing", production, StringComparison.Ordinal);
    }

    [Fact]
    public void T121_E2E_configuration_files_contain_no_real_secrets()
    {
        var hostDir = Path.Combine(BackendDirectory(), "tests", "Deckloom.Api.E2E.Host");
        var settings = Path.Combine(hostDir, "appsettings.E2E.json");

        Assert.True(File.Exists(settings), "appsettings.E2E.json deve existir no host de teste.");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(hostDir, "appsettings*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Collect(doc.RootElement, string.Empty, Path.GetFileName(file), offenders);
        }

        Assert.True(offenders.Count == 0, "Valores que nao parecem ficticios: " + string.Join("; ", offenders));
    }

    private static void Collect(JsonElement element, string path, string file, List<string> offenders)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, path + "/" + property.Name, file, offenders);
                }

                break;
            case JsonValueKind.String:
                var value = element.GetString()!;
                var sensitive = System.Text.RegularExpressions.Regex.IsMatch(
                    path, "secret|password|key|token|connection", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var synthetic = value.Length == 0
                    || value.StartsWith("e2e-", StringComparison.Ordinal)
                    || value.StartsWith("test-", StringComparison.Ordinal);
                if (sensitive && !synthetic)
                {
                    offenders.Add($"{file}:{path}");
                }

                break;
        }
    }

    private static string BackendDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Deckloom.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("backend/Deckloom.sln nao encontrada.");
    }

    private static HttpClient NewClient(E2eHostFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> PollLastLink(HttpClient client)
    {
        // O notificador entrega em segundo plano (canal): espera curta e limitada.
        HttpResponseMessage response = await client.GetAsync(LastLinkPath);
        for (var i = 0; i < 20 && response.StatusCode != HttpStatusCode.OK; i++)
        {
            await Task.Delay(100);
            response = await client.GetAsync(LastLinkPath);
        }

        return response;
    }

    private static async Task<string> RegisterAndGetSessionCookieAsync(HttpClient client)
    {
        var response = await PostJson(client, "/api/v1/auth/register", new
        {
            email = "ana@example.com",
            password = "senha-valida-123",
            displayName = "Ana",
            acceptTerms = true,
            termsVersion = Terms,
            adultDeclaration = true,
        });
        Assert.True(response.IsSuccessStatusCode, $"register status {(int)response.StatusCode}");
        return response.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, object body)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync()).RootElement.GetProperty("token")
            .GetString()!;
        var csrfPair = csrf.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = Json(body) };
        request.Headers.Add("Cookie", csrfPair);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetWithCookie(HttpClient client, string path, string cookiePair)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookiePair);
        return await client.SendAsync(request);
    }

    // Sobe o host de teste (Deckloom.Api.E2E.Host) no ambiente pedido, com opcoes sinteticas e sem banco.
    private sealed class E2eHostFactory(string environment) : WebApplicationFactory<E2eHostMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
            builder.UseSetting("Legal:TermsVersion", Terms);
        }
    }
}
