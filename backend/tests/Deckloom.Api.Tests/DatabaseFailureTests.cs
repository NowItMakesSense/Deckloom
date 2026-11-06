using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Deckloom.Api.Tests;

// T-139 (RED): falha de comando de banco com SqlState conhecido (ADR-0015, DB9) vira 500 internal_error generico,
// sem retry, com evento de log 1021 DatabaseCommandFailed (so sqlState e operation) e a metrica
// deckloom.db.command_failures (so o atributo sqlstate). Lacunas assumidas (ver perguntas ao orquestrador):
// (1) o valor de `operation` e o nome da classe do caso de uso (LoginWithPassword); (2) o nivel do 1021 e Error;
// (3) a excecao de teste e um DbException com SqlState (sem depender de Npgsql); (4) o 1021 e emitido uma vez por
// requisicao falha, por qualquer logger do host.
[Collection(MetricsTests.CollectionName)]
public sealed class DatabaseFailureTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string LoginPath = "/api/v1/auth/login";
    private const string MetricName = "deckloom.db.command_failures";
    private const string ServerMessage = "permission denied for table player (server detail)";

    private readonly FailureFactory _factory = new();
    private readonly MetricCollector _metrics = new();

    public void Dispose()
    {
        _factory.Dispose();
        _metrics.Dispose();
    }

    [Theory]
    [InlineData("42501")]
    [InlineData("57014")]
    [InlineData("55P03")]
    [InlineData("25P03")]
    [InlineData("53300")]
    public async Task Known_sqlstate_returns_generic_500_without_leaking_sqlstate_or_server_message(string sqlState)
    {
        _factory.Players.FailWith(sqlState);
        using var client = NewClient();

        var response = await PostLogin(client);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("internal_error", JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(sqlState, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ServerMessage, raw, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("42501")]
    [InlineData("57014")]
    [InlineData("55P03")]
    [InlineData("25P03")]
    [InlineData("53300")]
    public async Task Known_sqlstate_is_not_retried(string sqlState)
    {
        _factory.Players.FailWith(sqlState);
        using var client = NewClient();

        await PostLogin(client);

        Assert.Equal(1, _factory.Players.Calls);
    }

    [Theory]
    [InlineData("42501")]
    [InlineData("57014")]
    [InlineData("55P03")]
    [InlineData("25P03")]
    [InlineData("53300")]
    public async Task Known_sqlstate_emits_event_1021_with_only_sqlstate_and_operation(string sqlState)
    {
        _factory.Players.FailWith(sqlState);
        using var client = NewClient();

        await PostLogin(client);

        var logged = Assert.Single(_factory.Logs, e => e.Id == 1021);
        Assert.Equal("DatabaseCommandFailed", logged.Name);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Equal(
            ["operation", "sqlState"],
            logged.Properties.Keys.Where(k => k != "{OriginalFormat}").Order().ToArray());
        Assert.Equal(sqlState, logged.Properties["sqlState"]?.ToString());
        Assert.Equal("LoginWithPassword", logged.Properties["operation"]?.ToString());
        Assert.DoesNotContain(ServerMessage, logged.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@example.com", logged.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("42501")]
    [InlineData("57014")]
    [InlineData("55P03")]
    [InlineData("25P03")]
    [InlineData("53300")]
    public async Task Known_sqlstate_increments_the_metric_with_only_the_sqlstate_attribute(string sqlState)
    {
        _factory.Players.FailWith(sqlState);
        using var client = NewClient();

        await PostLogin(client);

        var measurement = Assert.Single(_metrics.All(), m => m.Name == MetricName);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(["sqlstate"], measurement.Tags.Keys.ToArray());
        Assert.Equal(sqlState, measurement.Tags["sqlstate"]?.ToString());
    }

    [Fact]
    public async Task Unknown_sqlstate_falls_to_the_generic_unhandled_handling_without_event_1021_or_metric()
    {
        _factory.Players.FailWith("23505");
        using var client = NewClient();

        var response = await PostLogin(client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(_factory.Logs, e => e.Id == 1021);
        Assert.DoesNotContain(_metrics.All(), m => m.Name == MetricName);
        Assert.Contains(
            _factory.Logs,
            e => e.Level == LogLevel.Error
                && e.Exception is null
                && e.Properties.TryGetValue("exceptionType", out var type)
                && Equals(type, nameof(FakeDbException)));
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static async Task<HttpResponseMessage> PostLogin(HttpClient client)
    {
        var csrf = await client.GetAsync("/api/v1/csrf");
        var token = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
        var pair = csrf.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal)).Split(';')[0];
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["email"] = "ana@example.com",
            ["password"] = "senha-valida-123",
            ["rememberMe"] = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Cookie", pair);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        return await client.SendAsync(request);
    }

    private sealed class FakeDbException(string sqlState) : DbException(ServerMessage)
    {
        public override string? SqlState => sqlState;
    }

    private sealed record LoggedEvent(
        int Id, string? Name, LogLevel Level, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed record Measurement(string Name, double Value, IReadOnlyDictionary<string, object?> Tags);

    private sealed class FailingPlayerRepository : IPlayerRepository
    {
        private string? _sqlState;
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void FailWith(string sqlState) => _sqlState = sqlState;

        public Task<Player?> GetById(PlayerId id, CancellationToken ct = default) => Fail<Player?>();

        public Task<Player?> FindByEmail(Email email, CancellationToken ct = default) => Fail<Player?>();

        public Task<Player?> FindByGoogleSubject(string subject, CancellationToken ct = default) => Fail<Player?>();

        public Task<Player?> FindByResetTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
            Fail<Player?>();

        public Task<bool> ExistsByEmail(Email email, CancellationToken ct = default) => Fail<bool>();

        public Task Add(Player player, CancellationToken ct = default) => Fail<bool>();

        public Task Save(Player player, CancellationToken ct = default) => Fail<bool>();

        public Task Remove(Player player, CancellationToken ct = default) => Fail<bool>();

        public Task<int> DeleteExpiredResetTokens(DateTimeOffset now, int batchSize, CancellationToken ct = default) => Fail<int>();

        private Task<T> Fail<T>()
        {
            Interlocked.Increment(ref _calls);
            throw new FakeDbException(_sqlState ?? "00000");
        }
    }

    private sealed class MetricCollector : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<Measurement> _items = [];
        private readonly MeterListener _listener = new();

        public MetricCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name == MetricName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
            _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Add(i, v, t));
            _listener.Start();
        }

        public Measurement[] All()
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Add<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
            where T : struct
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _items.Add(new Measurement(
                    instrument.Name,
                    Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
                    dictionary));
            }
        }
    }

    private sealed class CapturingProvider(ConcurrentQueue<LoggedEvent> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<LoggedEvent> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    props[pair.Key] = pair.Value;
                }
            }

            sink.Enqueue(new LoggedEvent(eventId.Id, eventId.Name, logLevel, formatter(state, exception), exception, props));
        }
    }

    private sealed class FailureFactory : WebApplicationFactory<Program>
    {
        private readonly ConcurrentQueue<LoggedEvent> _logs = new();

        public FailingPlayerRepository Players { get; } = new();

        public IReadOnlyCollection<LoggedEvent> Logs => _logs;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(new CapturingProvider(_logs));
            });
            builder.ConfigureServices(services =>
            {
                var uow = new InMemoryUnitOfWork();
                var clock = TimeProvider.System;
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
                services.AddSingleton(clock);
                services.AddSingleton<IUnitOfWork>(uow);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(new InMemorySessionRepository(uow));
                services.AddSingleton<IAccessAttemptStore>(new InMemoryAccessAttemptStore(uow));
                services.AddSingleton<IPendingGoogleAuthStore>(new InMemoryPendingGoogleAuthStore(clock, uow));
                services.AddSingleton<IGoogleIdentityVerifier>(new FakeGoogleIdentityVerifier());
                services.AddSingleton<IPlayerDecksPurger>(new RecordingPlayerDecksPurger(uow));
                services.AddSingleton<IAvatarStorage>(new InMemoryAvatarStorage(uow));
                services.AddSingleton<IAvatarImageProcessor>(new FakeAvatarImageProcessor());
                services.AddSingleton<IPasswordResetNotifier>(new RecordingPasswordResetNotifier());
                services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(600000));
                services.AddSingleton<IKeyedHasher>(new FakeKeyedHasher());
                services.AddSingleton<IDelayer>(new RecordingDelayer());
                services.AddSingleton<ITokenHasher>(new Sha256TokenHasher());
                services.AddSingleton<ISecureTokenGenerator>(new SequentialTokenGenerator());
                services.AddSingleton<IAvatarPresetCatalog>(new InMemoryAvatarPresetCatalog(
                    [new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true)]));
                services.AddSingleton<ITermsVersionProvider>(new FixedTermsVersionProvider(
                    "2026-10",
                    new Uri("https://deckloom.example.test/termos"),
                    new Uri("https://deckloom.example.test/privacidade")));
            });
        }
    }
}
