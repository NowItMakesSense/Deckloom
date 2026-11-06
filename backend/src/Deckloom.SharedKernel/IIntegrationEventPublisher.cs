namespace Deckloom.SharedKernel;

// Porta de publicacao no broker. Entrega at-least-once: o consumidor deduplica pelo OutboxMessage.Id.
public interface IIntegrationEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}
