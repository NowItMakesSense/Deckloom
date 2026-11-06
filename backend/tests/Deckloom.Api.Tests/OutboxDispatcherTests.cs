using Deckloom.Api.Composition;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Api.Tests;

// T-P03 (APR-017, AC-P03a): o despachante publica cada mensagem pendente uma unica vez, inclusive em reexecucao.
public sealed class OutboxDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeStore _store = new();
    private readonly FakePublisher _publisher = new();

    private OutboxDispatcher Dispatcher() => new(_store, _publisher, _clock, NullLogger<OutboxDispatcher>.Instance);

    [Fact]
    public async Task AC_P03a_running_twice_publishes_each_message_exactly_once()
    {
        var a = _store.Add("PlayerRegistered");
        var b = _store.Add("ProfileUpdated");

        var first = await Dispatcher().DispatchPendingAsync();
        var second = await Dispatcher().DispatchPendingAsync();

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal([a, b], _publisher.Published);
        Assert.All(_store.Messages, m => Assert.Equal(Now, m.ProcessedAt));
    }

    [Fact]
    public async Task AC_P03a_a_failed_publication_stays_pending_and_is_retried_on_the_next_run()
    {
        var a = _store.Add("PlayerRegistered");
        _publisher.FailNext = true;

        var failed = await Dispatcher().DispatchPendingAsync();
        Assert.Equal(0, failed);
        Assert.Null(_store.Messages[0].ProcessedAt);

        var retried = await Dispatcher().DispatchPendingAsync();
        Assert.Equal(1, retried);
        Assert.Equal([a], _publisher.Published);
    }

    [Fact]
    public async Task LGPD_the_default_publisher_logs_only_id_and_type_never_the_payload()
    {
        var provider = new RecordingProvider();
        var logger = new LoggerFactory([provider]).CreateLogger<LoggingIntegrationEventPublisher>();
        var message = OutboxMessage.Create("PlayerRegistered", "{\"email\":\"segredo@example.com\"}", Now);

        await new LoggingIntegrationEventPublisher(logger).PublishAsync(message, CancellationToken.None);

        var text = string.Join('\n', provider.Lines);
        Assert.Contains("PlayerRegistered", text, StringComparison.Ordinal);
        Assert.DoesNotContain("segredo", text, StringComparison.Ordinal);
    }

    private sealed class RecordingProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(Lines);

        public void Dispose()
        {
        }

        private sealed class Recorder(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add(formatter(state, exception));
        }
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<Guid> Published { get; } = [];

        public bool FailNext { get; set; }

        public Task PublishAsync(OutboxMessage message, CancellationToken ct)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("broker indisponivel");
            }

            Published.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IOutboxStore
    {
        public List<OutboxMessage> Messages { get; } = [];

        public Guid Add(string type)
        {
            var message = OutboxMessage.Create(type, "{}", Now);
            Messages.Add(message);
            return message.Id;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(
                Messages.Where(m => m.ProcessedAt is null).Take(batchSize).ToList());

        public Task<bool> MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken ct = default)
        {
            var message = Messages.Single(m => m.Id == id);
            if (message.ProcessedAt is not null)
            {
                return Task.FromResult(false);
            }

            message.MarkProcessed(processedAt);
            return Task.FromResult(true);
        }
    }
}
