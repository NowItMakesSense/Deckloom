using Deckloom.SharedKernel;

namespace Deckloom.Api.Composition;

// Publicador padrao seguro ate o adapter RabbitMQ existir (APR-017): registra so id e tipo, nunca o payload (LGPD).
public sealed partial class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        Published(logger, message.Id, message.Type);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox: mensagem {MessageId} ({MessageType}) publicada pelo publicador padrao (sem broker).")]
    private static partial void Published(ILogger logger, Guid messageId, string messageType);
}
