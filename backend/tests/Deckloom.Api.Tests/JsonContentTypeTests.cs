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

// T-148 (RED): SEC-10 (design, "Contracts > Convencoes gerais", bullet "Content-Type dos corpos JSON") e R-29.
// Contrato: todo endpoint que le corpo JSON recusa com 415 Problem Details (code = unsupported_media_type, sem
// errors) um corpo cujo Content-Type nao seja application/json (media type sem diferenciar maiusculas, parametros
// aceitos). "Com corpo" = Content-Length > 0 ou Transfer-Encoding: chunked; sem corpo nunca e 415. O 415 sai antes do
// caso de uso: nenhum evento de acesso gravado, nenhuma conta criada ou excluida.
// Lacunas assumidas: (1) o corpo valido de cada endpoint e o do contrato; os casos "aceito" afirmam apenas "nao 415"
// (o desfecho do caso de uso e coberto pelos testes de cada endpoint); (2) o corpo chunked e enviado por um
// HttpContent sem comprimento conhecido + Transfer-Encoding: chunked; (3) a equivalencia de register com os demais
// endpoints (R-29) e verificada contra login: mesmo status e mesmo Problem Details (sem traceId/instance).
public sealed class JsonContentTypeTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string AnaEmail = "ana@example.com";
    private const string AnaPassword = "senha-valida-123";
    private const string AnaSessionToken = "ana-session-token";
    private const string Terms = "2026-10";
    private const int MaxJsonBodyBytes = 16 * 1024;

    private static readonly Dictionary<string, (HttpMethod Method, string Path, bool NeedsSession, Func<object> Body)> Endpoints =
        new()
        {
            ["register"] = (HttpMethod.Post, "/api/v1/auth/register", false, () => new Dictionary<string, object?>
            {
                ["email"] = "novo@example.com",
                ["password"] = AnaPassword,
                ["displayName"] = "Novo",
                ["acceptTerms"] = true,
                ["termsVersion"] = Terms,
                ["adultDeclaration"] = true,
            }),
            ["login"] = (HttpMethod.Post, "/api/v1/auth/login", false, () => new Dictionary<string, object?>
            {
                ["email"] = AnaEmail,
                ["password"] = AnaPassword,
                ["rememberMe"] = false,
            }),
            ["password-reset-request"] = (HttpMethod.Post, "/api/v1/auth/password-reset/request", false, () =>
                new Dictionary<string, object?> { ["email"] = AnaEmail }),
            ["password-reset-confirm"] = (HttpMethod.Post, "/api/v1/auth/password-reset/confirm", false, () =>
                new Dictionary<string, object?> { ["token"] = "token-qualquer", ["newPassword"] = "outra-senha-123" }),
            ["google-complete-signup"] = (HttpMethod.Post, "/api/v1/auth/google/complete-signup", false, () =>
                new Dictionary<string, object?>
                {
                    ["displayName"] = "Gui",
                    ["acceptTerms"] = true,
                    ["termsVersion"] = Terms,
                    ["adultDeclaration"] = true,
                }),
            ["google-link"] = (HttpMethod.Post, "/api/v1/auth/google/link", false, () =>
                new Dictionary<string, object?> { ["password"] = AnaPassword }),
            ["me-profile"] = (HttpMethod.Put, "/api/v1/me/profile", true, () => new Dictionary<string, object?>
            {
                ["displayName"] = "Ana",
                ["bio"] = "",
                ["favoriteTcg"] = null,
                ["formatIds"] = Array.Empty<string>(),
            }),
            ["me-avatar-preset"] = (HttpMethod.Put, "/api/v1/me/avatar/preset", true, () =>
                new Dictionary<string, object?> { ["presetId"] = "preset-01" }),
            ["me-deletion"] = (HttpMethod.Post, "/api/v1/me/deletion", true, () =>
                new Dictionary<string, object?> { ["confirm"] = true, ["password"] = AnaPassword }),
        };

    private readonly JsonContentTypeFactory _factory = new();

    public static TheoryData<string> EndpointNames => [.. Endpoints.Keys];

    public static TheoryData<string, string> WrongContentTypes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in Endpoints.Keys)
            {
                data.Add(name, "text/plain");
                data.Add(name, "application/x-www-form-urlencoded");
            }

            return data;
        }
    }

    public static TheoryData<string, string> AcceptedContentTypes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in Endpoints.Keys)
            {
                data.Add(name, "application/json; charset=utf-8");
                data.Add(name, "Application/JSON");
                data.Add(name, "APPLICATION/json; charset=utf-8");
            }

            return data;
        }
    }

    public static TheoryData<string> OversizeOrMalformed =>
    [
        "oversize",
        "oversize-chunked",
        "malformed",
        "json-null",
    ];

    public void Dispose() => _factory.Dispose();

    [Theory]
    [MemberData(nameof(WrongContentTypes))]
    public async Task SEC10_body_with_a_non_json_content_type_returns_415_unsupported_media_type_without_running_the_use_case(
        string endpoint, string contentType)
    {
        using var client = NewClient();

        var response = await SendAsync(client, endpoint, contentType, chunked: false);

        await AssertUnsupportedMediaType(response);
        AssertUseCaseNotRun();
    }

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task SEC10_body_without_content_type_returns_415_unsupported_media_type_without_running_the_use_case(
        string endpoint)
    {
        using var client = NewClient();

        var response = await SendAsync(client, endpoint, contentType: null, chunked: false);

        await AssertUnsupportedMediaType(response);
        AssertUseCaseNotRun();
    }

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task SEC10_chunked_body_with_text_plain_returns_415_unsupported_media_type_without_running_the_use_case(
        string endpoint)
    {
        using var client = NewClient();

        var response = await SendAsync(client, endpoint, "text/plain", chunked: true);

        await AssertUnsupportedMediaType(response);
        AssertUseCaseNotRun();
    }

    [Theory]
    [MemberData(nameof(AcceptedContentTypes))]
    public async Task SEC10_json_media_type_is_accepted_case_insensitively_and_with_parameters(
        string endpoint, string contentType)
    {
        using var client = NewClient();

        var response = await SendAsync(client, endpoint, contentType, chunked: false);

        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // QA (fase 6): tipos parecidos com application/json, mas que nao o sao, tambem sao 415 (comparacao exata do media
    // type; nunca prefixo, sufixo +json ou Contains), inclusive valores malformados.
    [Theory]
    [InlineData("login", "application/jsonx")]
    [InlineData("login", "application/json-patch+json")]
    [InlineData("login", "application/vnd.api+json")]
    [InlineData("login", "text/json")]
    [InlineData("login", "application/")]
    [InlineData("login", "application/json, text/plain")]
    [InlineData("login", "text/plain; application/json")]
    [InlineData("login", "text/plain; charset=application/json")]
    [InlineData("login", "json")]
    [InlineData("login", "")]
    [InlineData("login", " ")]
    [InlineData("me-profile", "application/jsonx")]
    [InlineData("me-profile", "multipart/form-data; boundary=x")]
    [InlineData("me-deletion", "application/json-patch+json")]
    public async Task QA_near_miss_or_malformed_content_types_return_415_without_running_the_use_case(
        string endpoint, string contentType)
    {
        using var client = NewClient();

        var response = await SendAsync(client, endpoint, contentType, chunked: false);

        await AssertUnsupportedMediaType(response);
        AssertUseCaseNotRun();
    }

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task SEC10_chunked_body_with_application_json_is_accepted(string endpoint)
    {
        using var client = NewClient();

        var response = await SendAsync(client, endpoint, "application/json", chunked: true);

        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    [InlineData("password-reset-request")]
    [InlineData("password-reset-confirm")]
    public async Task SEC10_post_without_body_is_never_415(string endpoint)
    {
        using var client = NewClient();

        var response = await SendAsync(client, endpoint, contentType: null, chunked: false, withBody: false);

        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SEC10_empty_body_with_text_plain_is_never_415()
    {
        using var client = NewClient();

        var response = await SendAsync(client, "login", "text/plain", chunked: false, withBody: false, emptyContent: true);

        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SEC10_logout_without_body_is_never_415_and_still_returns_204()
    {
        using var client = NewClient();

        var response = await SendRawAsync(client, HttpMethod.Post, "/api/v1/auth/logout", content: null, withSession: true);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SEC10_delete_of_the_google_pending_without_body_is_never_415()
    {
        using var client = NewClient();

        var response = await SendRawAsync(client, HttpMethod.Delete, "/api/v1/auth/google/pending", content: null, withSession: false);

        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task SEC10_the_415_does_not_consume_the_progressive_delay_budget()
    {
        using var client = NewClient();

        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(
                HttpStatusCode.UnsupportedMediaType,
                (await SendAsync(client, "login", "text/plain", chunked: false)).StatusCode);
        }

        Assert.Empty(_factory.Attempts.Recorded);
        var response = await SendAsync(client, "login", "application/json", chunked: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OversizeOrMalformed))]
    public async Task R29_register_answers_exactly_like_login_to_oversize_and_malformed_bodies(string scenario)
    {
        using var client = NewClient();

        var register = await SendBodyAsync(client, "register", scenario);
        var login = await SendBodyAsync(client, "login", scenario);

        Assert.Equal(login.StatusCode, register.StatusCode);
        Assert.Equal(login.Content.Headers.ContentType?.MediaType, register.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await ProblemOf(login), await ProblemOf(register));
        Assert.Null(await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create("novo@example.com").Value));
    }

    private static string BodyFor(string scenario) => scenario switch
    {
        "oversize" or "oversize-chunked" => JsonSerializer.Serialize(new { email = new string('x', MaxJsonBodyBytes + 1) }),
        "malformed" => "{\"email\": ",
        "json-null" => "null",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static async Task<string> ProblemOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (text.Length == 0)
        {
            return string.Empty; // 413 pelo middleware de limite de corpo: sem Problem Details.
        }

        using var doc = JsonDocument.Parse(text);
        var fields = doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value.GetRawText()}");
        return string.Join(";", fields);
    }

    private static async Task AssertUnsupportedMediaType(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unsupported_media_type", doc.RootElement.GetProperty("code").GetString());
        Assert.False(doc.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(415, doc.RootElement.GetProperty("status").GetInt32());
    }

    // Nenhum evento de acesso gravado, nenhuma conta criada, Ana (e a sessao dela) intactas.
    private void AssertUseCaseNotRun()
    {
        Assert.Empty(_factory.Attempts.Recorded);
        Assert.Empty(_factory.Delayer.Delays);
        Assert.NotNull(_factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value).GetAwaiter().GetResult());
        Assert.Null(_factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create("novo@example.com").Value).GetAwaiter().GetResult());
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static Task<HttpResponseMessage> SendBodyAsync(HttpClient client, string endpoint, string scenario)
    {
        var (method, path, _, _) = Endpoints[endpoint];
        var bytes = Encoding.UTF8.GetBytes(BodyFor(scenario));
        HttpContent content = scenario == "oversize-chunked"
            ? new UnknownLengthContent(bytes)
            : new ByteArrayContent(bytes);
        SetContentType(content, "application/json");
        return SendRawAsync(client, method, path, content, withSession: false, chunked: scenario == "oversize-chunked");
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string endpoint,
        string? contentType,
        bool chunked,
        bool withBody = true,
        bool emptyContent = false)
    {
        var (method, path, needsSession, body) = Endpoints[endpoint];
        HttpContent? content = null;
        if (withBody || emptyContent)
        {
            var bytes = emptyContent ? [] : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body()));
            content = chunked ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
            SetContentType(content, contentType);
        }

        return SendRawAsync(client, method, path, content, needsSession, chunked && content is not null);
    }

    private static void SetContentType(HttpContent content, string? contentType)
    {
        content.Headers.Remove("Content-Type");
        if (contentType is not null)
        {
            // Sem validacao nem normalizacao: o servidor recebe o valor exatamente como escrito.
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }
    }

    private static async Task<HttpResponseMessage> SendRawAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpContent? content,
        bool withSession,
        bool chunked = false)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var csrfPair = Assert.Single(SetCookies(csrf), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];

        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }

        request.Headers.Add("Cookie", withSession ? $"{csrfPair}; {SessionCookie}={AnaSessionToken}" : csrfPair);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    // Corpo sem comprimento conhecido: com Transfer-Encoding: chunked nao ha Content-Length.
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    }

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

    private sealed class JsonContentTypeFactory : WebApplicationFactory<Program>
    {
        private readonly FixedClock _clock = new();

        public JsonContentTypeFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Attempts = new RecordingAttemptStore(new InMemoryAccessAttemptStore(Uow));
            Pending = new InMemoryPendingGoogleAuthStore(_clock, Uow);
            Seed().GetAwaiter().GetResult();
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public RecordingAttemptStore Attempts { get; }

        public InMemoryPendingGoogleAuthStore Pending { get; }

        public FakePasswordHasher Hasher { get; } = new(600000);

        public RecordingDelayer Delayer { get; } = new();

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
                services.RemoveAll<IPlayerDecksPurger>();
                services.RemoveAll<IAvatarStorage>();
                services.RemoveAll<IAvatarImageProcessor>();
                services.RemoveAll<IFormatCatalog>();
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.RemoveAll<IPasswordResetNotifier>();
                services.RemoveAll<IPasswordHasher>();
                services.RemoveAll<IKeyedHasher>();
                services.RemoveAll<IDelayer>();
                services.RemoveAll<ITokenHasher>();
                services.RemoveAll<ISecureTokenGenerator>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<ITermsVersionProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                services.AddSingleton<IUnitOfWork>(Uow);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(Sessions);
                services.AddSingleton<IPendingGoogleAuthStore>(Pending);
                services.AddSingleton<IAccessAttemptStore>(Attempts);
                services.AddSingleton<IPlayerDecksPurger>(new RecordingPlayerDecksPurger(Uow));
                services.AddSingleton<IAvatarStorage>(new InMemoryAvatarStorage(Uow));
                services.AddSingleton<IAvatarImageProcessor>(new FakeAvatarImageProcessor());
                services.AddSingleton<IFormatCatalog>(new InMemoryFormatCatalog(
                    [new FormatEntry(new FormatId("standard"), "Standard", 10, true)]));
                services.AddSingleton<IGoogleIdentityVerifier>(new FakeGoogleIdentityVerifier());
                services.AddSingleton<IPasswordResetNotifier>(new RecordingPasswordResetNotifier());
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
            var now = _clock.GetUtcNow();
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
            await Players.Add(ana);
            await Sessions.Add(Session.Start(ana.Id, new Sha256TokenHasher().Hash(AnaSessionToken), false, now));
            await Uow.CommitAsync();
        }
    }
}
