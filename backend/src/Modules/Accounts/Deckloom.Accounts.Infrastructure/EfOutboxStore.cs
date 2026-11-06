using Deckloom.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// Adapter de IOutboxStore sobre accounts.outbox_message. A marcacao e um UPDATE condicional (processed_at IS NULL):
// so uma execucao vence a corrida, e o papel da aplicacao precisa apenas de UPDATE(processed_at).
public sealed class EfOutboxStore(AccountsDbContext context) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        return await context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<bool> MarkProcessedAsync(Guid id, DateTimeOffset processedAt, CancellationToken ct = default)
    {
        var updated = await context.Set<OutboxMessage>()
            .Where(m => m.Id == id && m.ProcessedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, processedAt), ct);
        return updated == 1;
    }
}
