using Deckloom.SharedKernel;

namespace Deckloom.Api.Composition;

// Despachante da outbox (APR-017): le as mensagens pendentes, publica cada uma pela porta e a marca como processada
// (condicionalmente, entao uma reexecucao ou outra instancia nao a publica de novo). A ordem e preservada: a primeira
// falha de publicacao interrompe o lote e a mensagem segue pendente para o proximo ciclo (at-least-once: se o processo
// cair entre publicar e marcar, o consumidor deduplica pelo Id). Nunca loga o payload (LGPD).
public sealed partial class OutboxDispatcher(
    IOutboxStore store, IIntegrationEventPublisher publisher, TimeProvider clock, ILogger<OutboxDispatcher> logger)
{
    public const int BatchSize = 100;

    public async Task<int> DispatchPendingAsync(CancellationToken ct = default)
    {
        var published = 0;
        foreach (var message in await store.GetPendingAsync(BatchSize, ct))
        {
            try
            {
                await publisher.PublishAsync(message, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PublishFailed(logger, message.Id, message.Type, exception.GetType().Name);
                break;
            }

            if (await store.MarkProcessedAsync(message.Id, clock.GetUtcNow(), ct))
            {
                published++;
            }
        }

        return published;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox: falha ao publicar a mensagem {MessageId} ({MessageType}): {ErrorType}.")]
    private static partial void PublishFailed(ILogger logger, Guid messageId, string messageType, string errorType);
}
