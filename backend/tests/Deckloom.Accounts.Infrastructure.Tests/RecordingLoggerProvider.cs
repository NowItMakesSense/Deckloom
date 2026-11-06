using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure.Tests;

/// <summary>Evento capturado: EventId, nivel, mensagem e propriedades estruturadas.</summary>
public sealed record RecordedLogEvent(
    string Category,
    EventId EventId,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>ILoggerProvider de teste que grava os eventos emitidos (T-079).</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<RecordedLogEvent> _events = [];

    public IReadOnlyList<RecordedLogEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(RecordedLogEvent e)
    {
        lock (_gate)
        {
            _events.Add(e);
        }
    }

    private sealed class RecordingLogger(RecordingLoggerProvider owner, string category) : ILogger
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

            owner.Add(new RecordedLogEvent(category, eventId, logLevel, formatter(state, exception), props));
        }
    }
}
