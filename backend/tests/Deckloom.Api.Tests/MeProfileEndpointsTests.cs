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

// T-088 (RED): GET /api/v1/me/profile e PUT /api/v1/me/profile (design, tabela "Endpoints";
// REQ-013 a 017, 037, 040, 045, 046).
// Contrato: 200 Profile {displayName, bio, favoriteTcg{id,name}, preferredFormats[{id,name}] so ativos,
// avatar{kind,presetId,imageUrl,alt}, hasPassword, hasGoogle}; nenhuma propriedade email nem hash (varredura
// recursiva); 400 validation_failed com errors.<campo> = lista de codigos; 401 unauthenticated; 403 csrf_failed;
// Cache-Control no-store. Sessao via cookie de login (HandleCookies = false), CSRF com token anonimo + Origin.
// Lacunas assumidas: (1) email enviado no corpo do PUT e ignorado (propriedade desconhecida; a spec nao manda
// recusar): resposta 200 e email do Jogador inalterado; (2) 409 concurrency_conflict nao tem teste aqui (exige
// IUnitOfWork que lance ConcurrencyConflictException no host; coberto no Application); (3) o host compoe
// GetMyProfile/UpdateMyProfile a partir das portas registradas na fabrica.
public sealed class MeProfileEndpointsTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string ProfilePath = "/api/v1/me/profile";
    private const string LoginPath = "/api/v1/auth/login";
    private const string AnaEmail = "ana@example.com";
    private const string BiaEmail = "bia@example.com";
    private const string Password = "senha-valida-123";

    private readonly ProfileFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ013_get_returns_200_with_the_session_players_profile()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);

        var response = await Get(client, ProfilePath, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Ana", root.GetProperty("displayName").GetString());
        Assert.Equal("Bio da Ana", root.GetProperty("bio").GetString());
        Assert.Equal("mtg", root.GetProperty("favoriteTcg").GetProperty("id").GetString());
        Assert.Equal("Magic: The Gathering", root.GetProperty("favoriteTcg").GetProperty("name").GetString());
        Assert.Equal(["modern"], root.GetProperty("preferredFormats").EnumerateArray()
            .Select(f => f.GetProperty("id").GetString()));
        var avatar = root.GetProperty("avatar");
        Assert.Equal("preset", avatar.GetProperty("kind").GetString());
        Assert.Equal("preset-01", avatar.GetProperty("presetId").GetString());
        Assert.True(root.GetProperty("hasPassword").GetBoolean());
        Assert.False(root.GetProperty("hasGoogle").GetBoolean());
    }

    [Fact]
    public async Task REQ045_get_exposes_no_email_and_no_hash_anywhere_in_the_json()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);

        var response = await Get(client, ProfilePath, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        AssertNoEmailOrHash(text);
    }

    [Fact]
    public async Task REQ013_get_sends_cache_control_no_store()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);

        var response = await Get(client, ProfilePath, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("__Host-dl_session=token-desconhecido")]
    public async Task REQ013_get_without_a_valid_session_returns_401_unauthenticated(string? cookiePair)
    {
        using var client = NewClient();

        var response = await Get(client, ProfilePath, cookiePair);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task REQ014_put_with_a_valid_body_returns_200_with_the_updated_profile_and_persists_it()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);

        var response = await Put(client, ProfilePath, ValidBody(), cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var text = await response.Content.ReadAsStringAsync();
        AssertNoEmailOrHash(text);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal("Ana Mage", root.GetProperty("displayName").GetString());
        Assert.Equal("Jogo Commander", root.GetProperty("bio").GetString());
        Assert.Equal("mtg", root.GetProperty("favoriteTcg").GetProperty("id").GetString());
        Assert.Equal(["standard", "commander"], root.GetProperty("preferredFormats").EnumerateArray()
            .Select(f => f.GetProperty("id").GetString()));
        var reread = await Get(client, ProfilePath, cookie);
        using var rereadDoc = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());
        Assert.Equal("Ana Mage", rereadDoc.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task REQ015_put_with_empty_bio_and_no_formats_clears_them()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        var body = ValidBody();
        body["bio"] = null;
        body["formatIds"] = Array.Empty<string>();

        var response = await Put(client, ProfilePath, body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("bio").ValueKind);
        Assert.Empty(doc.RootElement.GetProperty("preferredFormats").EnumerateArray());
    }

    [Fact]
    public async Task REQ015_put_accepts_a_bio_with_line_break()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        var body = ValidBody();
        body["bio"] = "linha um\nlinha dois";

        var response = await Put(client, ProfilePath, body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("displayName", "An", "length")]
    [InlineData("displayName", "Abcdefghijklmnopqrstuvwxyz01234", "length")]
    [InlineData("displayName", "Ana\u0007Mage", "invalid_characters")]
    [InlineData("bio", "TOO_LONG", "too_long")]
    [InlineData("bio", "oi\0tchau", "invalid_characters")]
    [InlineData("favoriteTcg", "Pokemon", "invalid")]
    public async Task REQ037_put_with_an_invalid_field_returns_400_validation_failed_with_the_field_code(
        string field, string value, string expectedCode)
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        var body = ValidBody();
        body[field] = value == "TOO_LONG" ? new string('a', 281) : value;

        var response = await Put(client, ProfilePath, body, cookie);

        await AssertValidationFailed(response, field, expectedCode);
        await AssertProfileUnchanged(client, cookie);
    }

    [Theory]
    [InlineData("no-such-format")]
    [InlineData("pioneer")]
    public async Task REQ037_put_with_a_nonexistent_or_inactive_format_returns_400_invalid_on_formatIds(string formatId)
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        var body = ValidBody();
        body["formatIds"] = new[] { "standard", formatId };

        var response = await Put(client, ProfilePath, body, cookie);

        await AssertValidationFailed(response, "formatIds", "invalid");
        await AssertProfileUnchanged(client, cookie);
    }

    [Fact]
    public async Task REQ037_put_returns_every_field_error_together()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        var body = new Dictionary<string, object?>
        {
            ["displayName"] = "An",
            ["bio"] = new string('a', 281),
            ["favoriteTcg"] = "Pokemon",
            ["formatIds"] = new[] { "no-such-format" },
        };

        var response = await Put(client, ProfilePath, body, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = doc.RootElement.GetProperty("errors");
        Assert.Contains("length", Codes(errors, "displayName"));
        Assert.Contains("too_long", Codes(errors, "bio"));
        Assert.Contains("invalid", Codes(errors, "favoriteTcg"));
        Assert.Contains("invalid", Codes(errors, "formatIds"));
    }

    [Fact]
    public async Task REQ046_put_preserves_an_inactive_format_the_player_had_and_omits_it_from_the_response()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        var body = ValidBody();
        body["formatIds"] = new[] { "standard" };

        var response = await Put(client, ProfilePath, body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["standard"], doc.RootElement.GetProperty("preferredFormats").EnumerateArray()
            .Select(f => f.GetProperty("id").GetString()));
        var stored = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!;
        Assert.Contains("legacy", stored.Formats.Ids.Select(i => i.Value));
    }

    [Fact]
    public async Task REQ045_put_ignores_an_email_sent_in_the_body_and_never_returns_it()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        var body = ValidBody();
        body["email"] = "novo@example.com";

        var response = await Put(client, ProfilePath, body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoEmailOrHash(await response.Content.ReadAsStringAsync());
        Assert.NotNull(await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value));
        Assert.Null(await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create("novo@example.com").Value));
    }

    [Fact]
    public async Task REQ013_put_without_a_valid_session_returns_401_unauthenticated()
    {
        using var client = NewClient();

        var response = await Put(client, ProfilePath, ValidBody(), null);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task T078_put_without_a_csrf_token_returns_403_csrf_failed_and_changes_nothing()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client, AnaEmail);
        using var request = new HttpRequestMessage(HttpMethod.Put, ProfilePath)
        {
            Content = new StringContent(JsonSerializer.Serialize(ValidBody()), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", PublicOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("csrf_failed", doc.RootElement.GetProperty("code").GetString());
        await AssertProfileUnchanged(client, cookie);
    }

    [Fact]
    public async Task REQ013_each_session_reads_and_updates_only_its_own_profile()
    {
        using var client = NewClient();
        var ana = await LoginCookieAsync(client, AnaEmail);
        var bia = await LoginCookieAsync(client, BiaEmail);

        var biaProfile = await Get(client, $"{ProfilePath}?playerId={_factory.BiaId}", bia);
        var anaProfile = await Get(client, $"{ProfilePath}?playerId={_factory.BiaId}", ana);
        await Put(client, ProfilePath, ValidBody(), ana);

        using var biaDoc = JsonDocument.Parse(await biaProfile.Content.ReadAsStringAsync());
        using var anaDoc = JsonDocument.Parse(await anaProfile.Content.ReadAsStringAsync());
        Assert.Equal("Bia", biaDoc.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Ana", anaDoc.RootElement.GetProperty("displayName").GetString());
        var biaAfter = await Get(client, ProfilePath, bia);
        using var biaAfterDoc = JsonDocument.Parse(await biaAfter.Content.ReadAsStringAsync());
        Assert.Equal("Bia", biaAfterDoc.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, biaAfterDoc.RootElement.GetProperty("bio").ValueKind);
    }

    private static Dictionary<string, object?> ValidBody() => new()
    {
        ["displayName"] = "Ana Mage",
        ["bio"] = "Jogo Commander",
        ["favoriteTcg"] = "mtg",
        ["formatIds"] = new[] { "standard", "commander" },
    };

    private static IEnumerable<string?> Codes(JsonElement errors, string field) =>
        errors.GetProperty(field).EnumerateArray().Select(e => e.GetString());

    private static async Task AssertValidationFailed(HttpResponseMessage response, string field, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains(code, Codes(doc.RootElement.GetProperty("errors"), field));
    }

    private static async Task AssertUnauthenticated(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unauthenticated", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertProfileUnchanged(HttpClient client, string cookie)
    {
        var response = await Get(client, ProfilePath, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Ana", doc.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Bio da Ana", doc.RootElement.GetProperty("bio").GetString());
    }

    private static void AssertNoEmailOrHash(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var names = new List<string>();
        CollectNames(doc.RootElement, names);
        Assert.DoesNotContain(names, n => n.Equals("email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("hash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(AnaEmail, json, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectNames(JsonElement element, List<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                names.Add(property.Name);
                CollectNames(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectNames(item, names);
            }
        }
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static async Task<string> LoginCookieAsync(HttpClient client, string email)
    {
        var response = await PostLogin(client, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static async Task<HttpResponseMessage> PostLogin(HttpClient client, string email)
    {
        var (csrfPair, token) = await CsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["email"] = email,
                    ["password"] = Password,
                    ["rememberMe"] = false,
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Cookie", csrfPair);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<(string CsrfPair, string Token)> CsrfAsync(HttpClient client)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var pair = Assert.Single(SetCookies(csrf), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
        return (pair, token);
    }

    private static async Task<HttpResponseMessage> Put(
        HttpClient client, string path, Dictionary<string, object?> body, string? sessionCookiePair)
    {
        var (csrfPair, token) = await CsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", sessionCookiePair is null ? csrfPair : $"{sessionCookiePair}; {csrfPair}");
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

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private sealed class ProfileFactory : WebApplicationFactory<Program>
    {
        private const string Terms = "2026-10";

        public ProfileFactory()
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

        public InMemoryFormatCatalog Formats { get; } = new(
        [
            new FormatEntry(new FormatId("standard"), "Standard", 10, true),
            new FormatEntry(new FormatId("modern"), "Modern", 20, true),
            new FormatEntry(new FormatId("commander"), "Commander", 30, true),
            new FormatEntry(new FormatId("legacy"), "Legacy", 40, true),
            new FormatEntry(new FormatId("pioneer"), "Pioneer", 50, false),
        ]);

        public string BiaId { get; private set; } = string.Empty;

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
                services.RemoveAll<IFormatCatalog>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<ITermsVersionProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock());
                services.AddSingleton<IUnitOfWork>(Uow);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(Sessions);
                services.AddSingleton<IAccessAttemptStore>(Attempts);
                services.AddSingleton<IPasswordHasher>(Hasher);
                services.AddSingleton<IKeyedHasher>(new FakeKeyedHasher());
                services.AddSingleton<IDelayer>(new RecordingDelayer());
                services.AddSingleton<ITokenHasher>(new Sha256TokenHasher());
                services.AddSingleton<ISecureTokenGenerator>(new SequentialTokenGenerator());
                services.AddSingleton<IPasswordResetNotifier>(new RecordingPasswordResetNotifier());
                services.AddSingleton<IFormatCatalog>(Formats);
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
            var now = new FixedClock().GetUtcNow();
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
            var activeIds = (await Formats.ListActiveAsync()).Select(f => f.Id).ToList();
            Assert.True(ana.UpdateProfile(
                "Ana", "Bio da Ana", "mtg", [new FormatId("modern"), new FormatId("legacy")], activeIds).IsSuccess);
            ana.ClearDomainEvents();
            var bia = Player.RegisterWithPassword(
                Deckloom.Accounts.Domain.Email.Create(BiaEmail).Value,
                password,
                DisplayName.Create("Bia").Value,
                Terms,
                Terms,
                true,
                true,
                now,
                "preset-01").Value;
            await Players.Add(ana);
            await Players.Add(bia);
            await Uow.CommitAsync();
            Formats.Deactivate(new FormatId("legacy"));
            BiaId = bia.Id.ToString();
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    }
}
