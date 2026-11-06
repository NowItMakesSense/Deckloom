namespace Deckloom.SharedKernel;

// Leitura das mensagens pendentes e marcacao como processadas, para o despachante.
public interface IOutboxStore
{
    // Mais antigas primeiro.
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default);

    // Condicional (so se ainda pendente): devolve false se outra execucao ja marcou.
    Task<bool> MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken ct = default);
}
