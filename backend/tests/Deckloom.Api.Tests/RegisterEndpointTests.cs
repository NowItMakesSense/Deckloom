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

// T-082 (RED): POST /api/v1/auth/register (design, tabela "Endpoints"):
//   201 SessionSummary + cookie __Host-dl_session sem Max-Age (sem lembrar-me), sem email/hash   (REQ-001, 038, 040)
//   400 validation_failed com codigos de campo do contrato                                       (REQ-003, 022, 033, 034, 043, 044, 048)
//   409 email_unavailable com errors.email=["unavailable"], contando para o atraso por IP        (REQ-002, 041)
// Lacunas assumidas: (1) a spec nao fixa o status do corpo > 16 KB (design: "limite de tamanho de corpo 16 KB em JSON";
// T-ConcurrencyLimiter: "recusado"), entao o teste aceita 400 ou 413 e exige que nada seja criado; (2) o host deve
// compor RegisterWithPassword, AccessThrottle e ProgressiveDelayPolicy a partir das portas registradas aqui;
// (3) o 409 e emitido pelo endpoint com code email_unavailable e errors.email (o mapper hoje converteria erro com
// campo em 400); (4) o IP do cliente de teste e desconhecido, por isso o atraso por IP e verificado pelo registro
// da tentativa (acao Registration, sem chave de conta), nao por hash de IP fixo.
public sealed class RegisterEndpointTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string Terms = "2026-10";
    private const string RoutePath = "/api/v1/auth/register";
    private const int MaxJsonBodyBytes = 16 * 1024;

    private readonly RegisterFactory _factory = new();

    public static TheoryData<string, object?, string, string> InvalidFields => new()
    {
        { "email", "", "email", "required" },
        { "email", null, "email", "required" },
        { "email", "nao-e-um-email", "email", "invalid_format" },
        { "password", "", "password", "required" },
        { "password", null, "password", "required" },
        { "password", "1234567", "password", "too_short" },
        { "password", new string('a', 129), "password", "too_long" },
        { "displayName", "ab", "displayName", "length" },
        { "displayName", new string('n', 31), "displayName", "length" },
        { "displayName", "Ana\u0007", "displayName", "invalid_characters" },
        { "acceptTerms", false, "acceptTerms", "required" },
        { "termsVersion", "1999-01", "acceptTerms", "version_mismatch" },
        { "adultDeclaration", false, "adultDeclaration", "required" },
    };

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ001_register_returns_201_session_summary_and_session_cookie_without_remember_me()
    {
        using var client = NewClient();

        var response = await PostRegister(client, ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Ana", root.GetProperty("displayName").GetString());
        Assert.True(root.GetProperty("hasPassword").GetBoolean());
        Assert.False(root.GetProperty("hasGoogle").GetBoolean());
        var avatar = root.GetProperty("avatar");
        Assert.Equal("preset", avatar.GetProperty("kind").GetString());
        Assert.Equal("preset-01", avatar.GetProperty("presetId").GetString());
        Assert.Equal("/avatars/preset-01.png", avatar.GetProperty("imageUrl").GetString());

        var cookie = Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        Assert.True(cookie.Length > SessionCookie.Length + 2);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await _factory.Players.FindByEmail(Email.Create("ana@example.com").Value));
    }

    [Fact]
    public async Task REQ040_register_response_has_exactly_the_summary_properties_without_email_or_hash()
    {
        using var client = NewClient();

        var response = await PostRegister(client, ValidBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(["avatar", "displayName", "hasGoogle", "hasPassword"], PropertyNames(doc.RootElement));
        Assert.Equal(["alt", "imageUrl", "kind", "presetId"], PropertyNames(doc.RootElement.GetProperty("avatar")));
        Assert.DoesNotContain("ana@example.com", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pbkdf2", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task REQ003_invalid_input_returns_400_validation_failed_with_the_contract_field_code(
        string property, object? value, string field, string code)
    {
        using var client = NewClient();
        var body = ValidBody();
        body[property] = value;

        var response = await PostRegister(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        var codes = doc.RootElement.GetProperty("errors").GetProperty(field).EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains(code, codes);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie, StringComparison.Ordinal));
        Assert.Null(await _factory.Players.FindByEmail(Email.Create("ana@example.com").Value));
    }

    [Fact]
    public async Task REQ002_existing_email_returns_409_email_unavailable_and_counts_for_the_ip_delay()
    {
        using var client = NewClient();
        Assert.Equal(HttpStatusCode.Created, (await PostRegister(client, ValidBody())).StatusCode);
        var before = _factory.Attempts.Recorded.Count;

        var response = await PostRegister(client, ValidBody());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("email_unavailable", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            ["unavailable"],
            doc.RootElement.GetProperty("errors").GetProperty("email").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie, StringComparison.Ordinal));
        var attempt = Assert.Single(_factory.Attempts.Recorded.Skip(before));
        Assert.Equal(AccessAction.Registration, attempt.Action);
        Assert.Null(attempt.AccountKeyHash);
        Assert.NotEmpty(attempt.IpKeyHash);
    }

    [Fact]
    public async Task REQ003_password_of_128_characters_is_accepted()
    {
        using var client = NewClient();
        var body = ValidBody();
        body["password"] = new string('a', 128);

        var response = await PostRegister(client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task REQ003_password_of_129_characters_is_refused_with_too_long()
    {
        using var client = NewClient();
        var body = ValidBody();
        body["password"] = new string('a', 129);

        var response = await PostRegister(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("too_long", doc.RootElement.GetProperty("errors").GetProperty("password")[0].GetString());
        Assert.Empty(_factory.Hasher.HashCalls);
    }

    [Fact]
    public async Task REQ038_json_body_above_16_kb_is_refused_and_creates_nothing()
    {
        using var client = NewClient();
        var body = ValidBody();
        body["displayName"] = new string('x', MaxJsonBodyBytes + 1);

        var response = await PostRegister(client, body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.DoesNotContain(SetCookies(response), c => c.StartsWith(SessionCookie, StringComparison.Ordinal));
        Assert.Null(await _factory.Players.FindByEmail(Email.Create("ana@example.com").Value));
    }

    private static Dictionary<string, object?> ValidBody() => new()
    {
        ["email"] = "ana@example.com",
        ["password"] = "senha-valida-123",
        ["displayName"] = "Ana",
        ["acceptTerms"] = true,
        ["termsVersion"] = Terms,
        ["adultDeclaration"] = true,
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static async Task<HttpResponseMessage> PostRegister(HttpClient client, Dictionary<string, object?> body)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var cookiePair = Assert.Single(SetCookies(csrf), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, RoutePath)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", cookiePair);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private sealed class RecordingAttemptStore(InMemoryAccessAttemptStore inner) : IAccessAttemptStore
    {
        public List<AccessAttempt> Recorded { get; } = [];

        public Task<long> RecordAsync(AccessAttempt attempt, CancellationToken ct = default)
        {
            Recorded.Add(attempt);
            return inner.RecordAsync(attempt, ct);
        }

        public Task<int> DeleteByIdAsync(long id, CancellationToken ct = default) => inner.DeleteByIdAsync(id, ct);

        public Task<int> CountByIpAsync(byte[] ipKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
            inner.CountByIpAsync(ipKeyHash, since, ct);

        public Task<int> CountByAccountAsync(byte[] accountKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
            inner.CountByAccountAsync(accountKeyHash, since, ct);

        public Task<int> DeleteByAccountAsync(byte[] accountKeyHash, CancellationToken ct = default) =>
            inner.DeleteByAccountAsync(accountKeyHash, ct);

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct = default) =>
            inner.DeleteOlderThanAsync(cutoff, batchSize, ct);
    }

    private sealed class RegisterFactory : WebApplicationFactory<Program>
    {
        public RegisterFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Attempts = new RecordingAttemptStore(new InMemoryAccessAttemptStore(Uow));
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public RecordingAttemptStore Attempts { get; }

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
    }
}
