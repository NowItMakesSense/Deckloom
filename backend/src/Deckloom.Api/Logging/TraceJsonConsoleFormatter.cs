using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Deckloom.Api.Logging;

// Uma linha JSON por entrada de log, com traceId no nivel raiz (NFR de observabilidade).
// Escreve apenas a mensagem ja formatada: nunca o corpo da requisicao.
internal sealed class TraceJsonConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "deckloom-json";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", DateTimeOffset.UtcNow);
            json.WriteString("level", logEntry.LogLevel.ToString());
            json.WriteString("category", logEntry.Category);
            json.WriteNumber("eventId", logEntry.EventId.Id);
            var traceId = Activity.Current?.TraceId.ToString();
            if (!string.IsNullOrEmpty(traceId))
            {
                json.WriteString("traceId", traceId);
            }

            json.WriteString("message", message);
            if (logEntry.Exception is not null)
            {
                json.WriteString("exception", logEntry.Exception.ToString());
            }

            json.WriteEndObject();
        }

        textWriter.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
