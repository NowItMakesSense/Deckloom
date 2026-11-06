using System.Net;
using System.Net.Http.Headers;
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

// T-089 (RED): PUT /api/v1/me/avatar/preset, POST /api/v1/me/avatar/custom (multipart, campo `file`) e
// GET /api/v1/me/avatar/custom?v= (design, tabela "Endpoints" e "Processamento do avatar"; REQ-018 a 021, 045, 049).
// Contrato: preset valido -> 200 {avatar}; preset inativo/inexistente -> 400 validation_failed errors.presetId=[invalid];
// upload valido -> 200 {avatar{kind custom, imageUrl /api/v1/me/avatar/custom?v={key}}}; rejeicoes -> 413/415/422 com
// code avatar_rejected e reason size/type/content; GET -> image/webp, Cache-Control private + max-age=31536000 + immutable,
// nosniff, inline; 404 sem avatar customizado; 401 sem sessao; 403 csrf_failed sem token.
// Lacunas: (1) a spec nao define DELETE de avatar; a remocao do customizado e coberta pela troca para preset;
// (2) assinatura/tamanho/conteudo sao decididos pelo IAvatarImageProcessor (fake com RejectWith), o host so traduz o motivo;
// (3) o `reason` e lido em `reason` do Problem Details (design: `reason: "size"`).
public sealed class AvatarEndpointsTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string PresetPath = "/api/v1/me/avatar/preset";
    private const string CustomPath = "/api/v1/me/avatar/custom";
    private const string LoginPath = "/api/v1/auth/login";
    private const string AnaEmail = "ana@example.com";
    private const string Password = "senha-valida-123";

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] WebpBytes = [0x52, 0x49, 0x46, 0x46, 9, 9, 9, 9, 0x57, 0x45, 0x42, 0x50, 7, 7];

    private readonly AvatarFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ018_put_active_preset_returns_200_with_preset_avatar()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await PutPreset(client, "preset-02", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var avatar = doc.RootElement.GetProperty("avatar");
        Assert.Equal("preset", avatar.GetProperty("kind").GetString());
        Assert.Equal("preset-02", avatar.GetProperty("presetId").GetString());
        Assert.Equal("/avatars/preset-02.png", avatar.GetProperty("imageUrl").GetString());
    }

    [Theory]
    [InlineData("preset-inativo")]
    [InlineData("nao-existe")]
    public async Task REQ018_put_inactive_or_unknown_preset_returns_400_validation_failed_invalid(string presetId)
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await PutPreset(client, presetId, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains("invalid", doc.RootElement.GetProperty("errors").GetProperty("presetId")
            .EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task REQ018_put_preset_without_session_returns_401_unauthenticated()
    {
        using var client = NewClient();

        var response = await PutPreset(client, "preset-02", null);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task T078_put_preset_without_csrf_returns_403_csrf_failed()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, PresetPath)
        {
            Content = new StringContent("{\"presetId\":\"preset-02\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", PublicOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("csrf_failed", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task REQ019_post_valid_image_returns_200_with_versioned_custom_avatar()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.AcceptWith(WebpBytes);

        var response = await PostFile(client, PngBytes, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        AssertNoLeaks(text);
        using var doc = JsonDocument.Parse(text);
        var avatar = doc.RootElement.GetProperty("avatar");
        Assert.Equal("custom", avatar.GetProperty("kind").GetString());
        Assert.StartsWith(CustomPath + "?v=", avatar.GetProperty("imageUrl").GetString(), StringComparison.Ordinal);
        Assert.True(avatar.GetProperty("imageUrl").GetString()!.Length > (CustomPath + "?v=").Length);
    }

    [Fact]
    public async Task REQ020_post_oversized_file_returns_413_size()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.RejectWith(AvatarRejectionReason.Size);

        var response = await PostFile(client, new byte[2_100_000], cookie);

        await AssertRejected(response, HttpStatusCode.RequestEntityTooLarge, "size");
    }

    [Fact]
    public async Task REQ020_post_body_above_endpoint_limit_returns_413_before_reading()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await PostFile(client, new byte[2_700_000], cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_factory.Processor.Received);
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("text")]
    [InlineData("mz")]
    public async Task REQ020_post_invalid_signature_returns_415_type(string kind)
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.RejectWith(AvatarRejectionReason.Type);
        byte[] bytes = kind switch
        {
            "gif" => Encoding.ASCII.GetBytes("GIF89a......"),
            "text" => Encoding.ASCII.GetBytes("isto nao e uma imagem"),
            _ => [0x4D, 0x5A, 0x90, 0x00, 0x03],
        };

        var response = await PostFile(client, bytes, cookie);

        await AssertRejected(response, HttpStatusCode.UnsupportedMediaType, "type");
    }

    [Fact]
    public async Task REQ020_post_content_refused_by_processor_returns_422_content()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.RejectWith(AvatarRejectionReason.Content);

        var response = await PostFile(client, PngBytes, cookie);

        await AssertRejected(response, HttpStatusCode.UnprocessableEntity, "content");
    }

    [Fact]
    public async Task REQ020_rejection_leaves_current_avatar_unchanged()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.RejectWith(AvatarRejectionReason.Content);

        var rejected = await PostFile(client, PngBytes, cookie);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var custom = await Get(client, CustomPath, cookie);
        Assert.Equal(HttpStatusCode.NotFound, custom.StatusCode);
    }

    [Fact]
    public async Task REQ019_post_without_session_returns_401_unauthenticated()
    {
        using var client = NewClient();

        var response = await PostFile(client, PngBytes, null);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task T078_post_without_csrf_returns_403_csrf_failed()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, CustomPath) { Content = Multipart(PngBytes) };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", PublicOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("csrf_failed", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task REQ019_get_custom_returns_image_with_immutable_private_cache_headers()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.AcceptWith(WebpBytes);
        var url = await UploadAndGetUrl(client, cookie);

        var response = await Get(client, url, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(WebpBytes, await response.Content.ReadAsByteArrayAsync());
        var cache = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("private", cache, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=31536000", cache, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("immutable", cache, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task REQ019_get_custom_without_custom_avatar_returns_404()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await Get(client, CustomPath, cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not_found", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task REQ019_get_custom_without_session_returns_401_unauthenticated()
    {
        using var client = NewClient();

        var response = await Get(client, CustomPath + "?v=abc", null);

        await AssertUnauthenticated(response);
    }

    [Fact]
    public async Task REQ021_replacing_custom_avatar_removes_the_previous_image_from_storage()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.AcceptWith(WebpBytes);
        var first = KeyOf(await UploadAndGetUrl(client, cookie));
        Assert.NotNull(await _factory.Storage.ReadAsync(first));

        var second = KeyOf(await UploadAndGetUrl(client, cookie));

        Assert.NotEqual(first, second);
        Assert.Null(await _factory.Storage.ReadAsync(first));
        Assert.NotNull(await _factory.Storage.ReadAsync(second));
    }

    [Fact]
    public async Task REQ021_switching_to_a_preset_removes_the_custom_image_from_storage()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.AcceptWith(WebpBytes);
        var key = KeyOf(await UploadAndGetUrl(client, cookie));

        var response = await PutPreset(client, "preset-02", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await _factory.Storage.ReadAsync(key));
        var custom = await Get(client, CustomPath, cookie);
        Assert.Equal(HttpStatusCode.NotFound, custom.StatusCode);
    }

    [Fact]
    public async Task REQ045_responses_expose_no_email_hash_or_internal_storage_path()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.AcceptWith(WebpBytes);

        var upload = await PostFile(client, PngBytes, cookie);
        var preset = await PutPreset(client, "preset-02", cookie);
        _factory.Processor.RejectWith(AvatarRejectionReason.Content);
        var rejected = await PostFile(client, PngBytes, cookie);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, preset.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        foreach (var response in new[] { upload, preset, rejected })
        {
            AssertNoLeaks(await response.Content.ReadAsStringAsync());
        }
    }

    private static string KeyOf(string imageUrl) => imageUrl[(imageUrl.IndexOf("?v=", StringComparison.Ordinal) + 3)..];

    private static async Task<string> UploadAndGetUrl(HttpClient client, string cookie)
    {
        var response = await PostFile(client, PngBytes, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("avatar").GetProperty("imageUrl").GetString()!;
    }

    private static void AssertNoLeaks(string json)
    {
        Assert.DoesNotContain(AnaEmail, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customAvatarKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accounts.", json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertRejected(HttpResponseMessage response, HttpStatusCode status, string reason)
    {
        Assert.Equal(status, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        AssertNoLeaks(text);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("avatar_rejected", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(reason, doc.RootElement.GetProperty("reason").GetString());
    }

    private static async Task AssertUnauthenticated(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unauthenticated", doc.RootElement.GetProperty("code").GetString());
    }

    private static MultipartFormDataContent Multipart(byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "file", "avatar.png" } };
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static async Task<string> LoginCookieAsync(HttpClient client)
    {
        var (csrfPair, token) = await CsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["email"] = AnaEmail,
                    ["password"] = Password,
                    ["rememberMe"] = false,
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Cookie", csrfPair);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
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

    private static async Task<HttpResponseMessage> PutPreset(HttpClient client, string presetId, string? sessionCookie)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["presetId"] = presetId }),
            Encoding.UTF8,
            "application/json");
        return await SendMutation(client, HttpMethod.Put, PresetPath, content, sessionCookie);
    }

    private static async Task<HttpResponseMessage> PostFile(HttpClient client, byte[] bytes, string? sessionCookie)
    {
        using var content = Multipart(bytes);
        return await SendMutation(client, HttpMethod.Post, CustomPath, content, sessionCookie);
    }

    private static async Task<HttpResponseMessage> SendMutation(
        HttpClient client, HttpMethod method, string path, HttpContent content, string? sessionCookie)
    {
        var (csrfPair, token) = await CsrfAsync(client);
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Cookie", sessionCookie is null ? csrfPair : $"{sessionCookie}; {csrfPair}");
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

    private sealed class AvatarFactory : WebApplicationFactory<Program>
    {
        private const string Terms = "2026-10";

        public AvatarFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Attempts = new InMemoryAccessAttemptStore(Uow);
            Storage = new InMemoryAvatarStorage(Uow);
            Seed().GetAwaiter().GetResult();
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public InMemoryAccessAttemptStore Attempts { get; }

        public InMemoryAvatarStorage Storage { get; }

        public FakeAvatarImageProcessor Processor { get; } = new();

        public FakePasswordHasher Hasher { get; } = new(600000);

        public InMemoryFormatCatalog Formats { get; } = new(
        [
            new FormatEntry(new FormatId("standard"), "Standard", 10, true),
        ]);

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
                services.RemoveAll<IAvatarStorage>();
                services.RemoveAll<IAvatarImageProcessor>();
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
                services.AddSingleton<IAvatarStorage>(Storage);
                services.AddSingleton<IAvatarImageProcessor>(Processor);
                services.AddSingleton<IAvatarPresetCatalog>(new InMemoryAvatarPresetCatalog(
                [
                    new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true),
                    new AvatarPresetEntry("preset-02", "Fenix", "/avatars/preset-02.png", 2, false, true),
                    new AvatarPresetEntry("preset-inativo", "Antigo", "/avatars/preset-inativo.png", 3, false, false),
                ]));
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
            ana.ClearDomainEvents();
            await Players.Add(ana);
            await Uow.CommitAsync();
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    }
}
