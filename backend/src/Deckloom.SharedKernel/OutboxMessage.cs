namespace Deckloom.SharedKernel;

// Mensagem de integracao gravada na mesma transacao do agregado (outbox transacional, APR-017). O payload e JSON ja
// serializado; o SharedKernel nao conhece infraestrutura nem formato de transporte.
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public static OutboxMessage Create(string type, string payload, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        return new OutboxMessage { Id = Guid.NewGuid(), Type = type, Payload = payload, OccurredAt = occurredAt };
    }

    public void MarkProcessed(DateTimeOffset processedAt) => ProcessedAt = processedAt;
}
