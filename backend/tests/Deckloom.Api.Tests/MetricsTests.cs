using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// T-092 (RED): metricas OpenTelemetry (System.Diagnostics.Metrics) dos fluxos de conta
// (design, "Error handling and observability" > "Metricas" e "Alertas").
// Lacunas assumidas (ver perguntas ao orquestrador): (1) o nome do Meter nao e fixado na spec, entao o
// MeterListener filtra pelo nome do instrumento (prefixo "deckloom."); (2) valores de atributo seguem os codigos dos
// eventos de log (login: success/failure; google: failed + stage callback para cancelamento; password_reset:
// token_issued/no_eligible_account; method: password; reason: size); (3) deckloom.auth.processing_duration e um
// histograma em MILISSEGUNDOS medido com o TimeProvider registrado (relogio controlavel), depois do atraso do IDelayer;
// (4) os testes exercitam os fluxos pelo host (HTTP) com fakes em memoria, para nao fixar onde a instrumentacao mora.
[CollectionDefinition(MetricsTests.CollectionName, DisableParallelization = true)]
public sealed class MetricsSerialGroup;

[Collection(MetricsTests.CollectionName)]
public sealed class MetricsTests : IDisposable
{
    public const string CollectionName = "Metrics";

    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string OAuthCookie = "__Host-dl_oauth";
    private const string LoginPath = "/api/v1/auth/login";
    private const string RegisterPath = "/api/v1/auth/register";
    private const string ResetRequestPath = "/api/v1/auth/password-reset/request";
    private const string DeletionPath = "/api/v1/me/deletion";
    private const string AvatarCustomPath = "/api/v1/me/avatar/custom";
    private const string GoogleStartPath = "/api/v1/auth/google/start";
    private const string GoogleCallbackPath = "/api/v1/auth/google/callback";
    private const string AnaEmail = "ana@example.com";
    private const string NewEmail = "novo@example.com";
    private const string Password = "senha-valida-123";
    private const string Terms = "2026-10";

    private static readonly string[] AllowedTagKeys = ["outcome", "stage", "action", "method", "reason"];

    private readonly MetricCollector _metrics = new();
    private readonly MetricsFactory _factory;

    public MetricsTests() => _factory = new MetricsFactory();

    public void Dispose()
    {
        _factory.Dispose();
        _metrics.Dispose();
    }

    [Fact]
    public async Task Login_success_increments_deckloom_auth_login_with_outcome_success()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody(Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.auth.login", ("outcome", "success")));
        Assert.Equal(0, _metrics.Sum("deckloom.auth.login", ("outcome", "failure")));
    }

    [Fact]
    public async Task Login_failure_increments_deckloom_auth_login_with_outcome_failure()
    {
        using var client = NewClient();

        var response = await PostJson(client, LoginPath, LoginBody("senha-errada-123"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.auth.login", ("outcome", "failure")));
        Assert.Equal(0, _metrics.Sum("deckloom.auth.login", ("outcome", "success")));
    }

    [Fact]
    public async Task Login_over_the_account_budget_increments_deckloom_auth_login_with_outcome_budget_exceeded()
    {
        using var client = NewClient();
        for (var i = 0; i < 30; i++)
        {
            await PostJson(client, LoginPath, LoginBody("senha-errada-123"));
        }

        var response = await PostJson(client, LoginPath, LoginBody(Password));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.auth.login", ("outcome", "budget_exceeded")));
        Assert.Equal(30, _metrics.Sum("deckloom.auth.login", ("outcome", "failure")));
    }

    [Fact]
    public async Task Progressive_delay_is_recorded_as_a_histogram_of_delay_ms_with_the_action()
    {
        using var client = NewClient();
        for (var i = 0; i < 5; i++)
        {
            await PostJson(client, LoginPath, LoginBody("senha-errada-123"));
        }

        var applied = _factory.Delayer.Delays.Select(d => d.TotalMilliseconds).ToArray();
        Assert.NotEmpty(applied);
        var recorded = _metrics.Values("deckloom.auth.progressive_delay", ("action", "login")).ToArray();
        Assert.Equal(applied.Order().ToArray(), recorded.Order().ToArray());
        Assert.True(_metrics.IsHistogram("deckloom.auth.progressive_delay"));
    }

    [Fact]
    public async Task Processing_duration_is_measured_after_the_deliberate_delay()
    {
        using var client = NewClient();
        for (var i = 0; i < 5; i++)
        {
            await PostJson(client, LoginPath, LoginBody("senha-errada-123"));
        }

        _metrics.Clear();
        _factory.Delayer.Delays.Clear();
        var response = await PostJson(client, LoginPath, LoginBody("senha-errada-123"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var delay = Assert.Single(_factory.Delayer.Delays);
        Assert.True(delay >= TimeSpan.FromSeconds(1));
        var duration = Assert.Single(_metrics.Values("deckloom.auth.processing_duration"));
        Assert.True(_metrics.IsHistogram("deckloom.auth.processing_duration"));
        Assert.InRange(duration, MetricsFactory.VerifyStep.TotalMilliseconds - 1, MetricsFactory.VerifyStep.TotalMilliseconds + 1);
        Assert.True(duration < delay.TotalMilliseconds);
    }

    [Fact]
    public async Task Password_reset_request_counts_token_issued_and_no_eligible_account()
    {
        using var client = NewClient();

        await PostJson(client, ResetRequestPath, new() { ["email"] = AnaEmail });
        await PostJson(client, ResetRequestPath, new() { ["email"] = "ninguem@example.com" });

        Assert.Equal(1, _metrics.Sum("deckloom.auth.password_reset", ("outcome", "token_issued")));
        Assert.Equal(1, _metrics.Sum("deckloom.auth.password_reset", ("outcome", "no_eligible_account")));
    }

    [Fact]
    public async Task Reset_email_failure_increments_deckloom_auth_reset_email_failures()
    {
        using var client = NewClient();
        _factory.Notifier.FailWith(new InvalidOperationException("smtp indisponivel"));

        var response = await PostJson(client, ResetRequestPath, new() { ["email"] = AnaEmail });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.auth.reset_email_failures"));
    }

    [Fact]
    public async Task Password_registration_increments_registrations_with_method_password()
    {
        using var client = NewClient();

        var response = await PostJson(client, RegisterPath, RegisterBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.accounts.registrations", ("method", "password")));
    }

    [Fact]
    public async Task Account_deletion_increments_deckloom_accounts_deletions()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);

        var response = await PostJson(
            client, DeletionPath, new() { ["confirm"] = true, ["password"] = Password }, cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.accounts.deletions"));
    }

    [Fact]
    public async Task Rejected_avatar_upload_increments_avatar_rejections_with_the_reason()
    {
        using var client = NewClient();
        var cookie = await LoginCookieAsync(client);
        _factory.Processor.RejectWith(AvatarRejectionReason.Size);

        var response = await PostFile(client, cookie);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.avatar.rejections", ("reason", "size")));
    }

    [Fact]
    public async Task Google_callback_cancelled_by_the_user_counts_failed_at_the_callback_stage()
    {
        using var client = NewClient();
        var start = await client.GetAsync($"{GoogleStartPath}?intent=signin");
        var state = HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;
        var cookie = Assert.Single(SetCookies(start), c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{GoogleCallbackPath}?error=access_denied&state={Uri.EscapeDataString(state)}");
        request.Headers.Add("Cookie", cookie);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, _metrics.Sum("deckloom.auth.google", ("outcome", "failed"), ("stage", "authorize")));
    }

    [Fact]
    public async Task Metric_attributes_never_carry_email_ip_or_account_id()
    {
        using var client = NewClient();
        await PostJson(client, LoginPath, LoginBody("senha-errada-123"));
        var cookie = await LoginCookieAsync(client);
        await PostJson(client, RegisterPath, RegisterBody());
        await PostJson(client, ResetRequestPath, new() { ["email"] = AnaEmail });
        _factory.Processor.RejectWith(AvatarRejectionReason.Type);
        await PostFile(client, cookie);
        var playerId = (await _factory.Players.FindByEmail(Deckloom.Accounts.Domain.Email.Create(AnaEmail).Value))!.Id;

        var all = _metrics.All().Where(m => m.Name.StartsWith("deckloom.", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(all);
        foreach (var measurement in all)
        {
            foreach (var (key, value) in measurement.Tags)
            {
                Assert.Contains(key, AllowedTagKeys);
                var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                Assert.Matches(new Regex("^[a-z_]+$"), text);
                Assert.DoesNotContain(AnaEmail, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(NewEmail, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(playerId.Value.ToString(), text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static Dictionary<string, object?> LoginBody(string password) => new()
    {
        ["email"] = AnaEmail,
        ["password"] = password,
        ["rememberMe"] = false,
    };

    private static Dictionary<string, object?> RegisterBody() => new()
    {
        ["email"] = NewEmail,
        ["password"] = Password,
        ["displayName"] = "Novo",
        ["acceptTerms"] = true,
        ["termsVersion"] = Terms,
        ["adultDeclaration"] = true,
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static async Task<string> LoginCookieAsync(HttpClient client)
    {
        var response = await PostJson(client, LoginPath, LoginBody(Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(SetCookies(response), c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];
    }

    private static async Task<HttpResponseMessage> PostFile(HttpClient client, string sessionCookie)
    {
        var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var content = new MultipartFormDataContent { { file, "file", "avatar.png" } };
        return await Send(client, HttpMethod.Post, AvatarCustomPath, content, sessionCookie);
    }

    private static async Task<HttpResponseMessage> PostJson(
        HttpClient client, string path, Dictionary<string, object?> body, string? sessionCookie = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await Send(client, HttpMethod.Post, path, content, sessionCookie);
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, HttpContent content, string? sessionCookie)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var csrfPair = Assert.Single(SetCookies(csrf), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
            .Split(';')[0];

        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Cookie", sessionCookie is null ? csrfPair : $"{sessionCookie}; {csrfPair}");
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

    private sealed record Measurement(
        string Name, double Value, bool IsHistogram, IReadOnlyDictionary<string, object?> Tags);

    private sealed class MetricCollector : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<Measurement> _items = [];
        private readonly MeterListener _listener = new();

        public MetricCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name.StartsWith("deckloom.", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
            _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Add(i, v, t));
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
            _listener.Start();
        }

        public void Clear()
        {
            lock (_gate)
            {
                _items.Clear();
            }
        }

        public Measurement[] All()
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }

        public double Sum(string name, params (string Key, string Value)[] tags) => Values(name, tags).Sum();

        public IEnumerable<double> Values(string name, params (string Key, string Value)[] tags) =>
            All().Where(m => m.Name == name && tags.All(t =>
                m.Tags.TryGetValue(t.Key, out var v) && Equals(v?.ToString(), t.Value))).Select(m => m.Value);

        public bool IsHistogram(string name) => All().Any(m => m.Name == name && m.IsHistogram);

        public void Dispose() => _listener.Dispose();

        private void Add<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }

            var isHistogram = instrument.GetType().Name.StartsWith("Histogram", StringComparison.Ordinal);
            lock (_gate)
            {
                _items.Add(new Measurement(
                    instrument.Name, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), isHistogram, dictionary));
            }
        }
    }

    private sealed class SteppingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan by)
        {
            _now += by;
            Interlocked.Add(ref _ticks, by.Ticks);
        }
    }

    private sealed class AdvancingDelayer(SteppingClock clock) : IDelayer
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            Delays.Add(delay);
            clock.Advance(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class SlowVerifyHasher(IPasswordHasher inner, SteppingClock clock, TimeSpan step) : IPasswordHasher
    {
        public ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default) =>
            inner.HashAsync(password, ct);

        public ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default)
        {
            clock.Advance(step);
            return inner.VerifyAsync(password, hash, ct);
        }

        public bool NeedsRehash(PasswordHash hash) => inner.NeedsRehash(hash);
    }

    private sealed class MetricsFactory : WebApplicationFactory<Program>
    {
        public static readonly TimeSpan VerifyStep = TimeSpan.FromMilliseconds(40);

        public MetricsFactory()
        {
            Players = new InMemoryPlayerRepository(Uow);
            Sessions = new InMemorySessionRepository(Uow);
            Attempts = new InMemoryAccessAttemptStore(Uow);
            Pending = new InMemoryPendingGoogleAuthStore(Clock, Uow);
            Purger = new RecordingPlayerDecksPurger(Uow);
            Storage = new InMemoryAvatarStorage(Uow);
            Delayer = new AdvancingDelayer(Clock);
            Seed().GetAwaiter().GetResult();
        }

        public InMemoryUnitOfWork Uow { get; } = new();

        public InMemoryPlayerRepository Players { get; }

        public InMemorySessionRepository Sessions { get; }

        public InMemoryAccessAttemptStore Attempts { get; }

        public InMemoryPendingGoogleAuthStore Pending { get; }

        public RecordingPlayerDecksPurger Purger { get; }

        public InMemoryAvatarStorage Storage { get; }

        public FakeAvatarImageProcessor Processor { get; } = new();

        public FakeGoogleIdentityVerifier Verifier { get; } = new();

        public RecordingPasswordResetNotifier Notifier { get; } = new();

        public FakePasswordHasher Hasher { get; } = new(600000);

        public SteppingClock Clock { get; } = new();

        public AdvancingDelayer Delayer { get; }

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
                services.RemoveAll<IPendingGoogleAuthStore>();
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.RemoveAll<IPlayerDecksPurger>();
                services.RemoveAll<IAvatarStorage>();
                services.RemoveAll<IAvatarImageProcessor>();
                services.RemoveAll<IPasswordResetNotifier>();
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
                services.AddSingleton<IPendingGoogleAuthStore>(Pending);
                services.AddSingleton<IGoogleIdentityVerifier>(Verifier);
                services.AddSingleton<IPlayerDecksPurger>(Purger);
                services.AddSingleton<IAvatarStorage>(Storage);
                services.AddSingleton<IAvatarImageProcessor>(Processor);
                services.AddSingleton<IPasswordResetNotifier>(Notifier);
                services.AddSingleton<IPasswordHasher>(new SlowVerifyHasher(Hasher, Clock, VerifyStep));
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
            await Players.Add(ana);
            await Uow.CommitAsync();
        }
    }
}
