using Deckloom.Accounts.Application;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// RecordAsync, DeleteByIdAsync e DeleteOlderThanAsync agem na hora, fora do EfUnitOfWork: as falhas de login precisam persistir mesmo
// se a operacao falhar (REQ-032) e a retencao e um job. DeleteByAccountAsync devolve a contagem e so enfileira a
// exclusao no contexto; as linhas somem no EfUnitOfWork.CommitAsync, na mesma transacao da exclusao do Jogador
// (REQ-047). Linhas sem chave de conta (so IP) e de outras contas nao sao tocadas.
public sealed class EfAccessAttemptStore(AccountsDbContext context) : IAccessAttemptStore
{
    private DbSet<AccessAttemptRow> Rows => context.Set<AccessAttemptRow>();

    public async Task<long> RecordAsync(AccessAttempt attempt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        EnsureNoOpenUnitOfWork();

        var row = new AccessAttemptRow
        {
            Action = AccessAttemptEntityConfiguration.ToColumnValue(attempt.Action),
            IpKeyHash = attempt.IpKeyHash,
            AccountKeyHash = attempt.AccountKeyHash,
            AttemptedAt = attempt.AttemptedAt,
        };
        Rows.Add(row);
        try
        {
            await context.SaveChangesAsync(ct);
            return row.Id;
        }
        finally
        {
            context.Entry(row).State = EntityState.Detached;
        }
    }

    // Imediato (ExecuteDelete), fora do EfUnitOfWork: o evento provisorio nao pode depender de commit nem de rollback.
    public async Task<int> DeleteByIdAsync(long id, CancellationToken ct = default)
    {
        EnsureNoOpenUnitOfWork();
        return await Rows.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
    }

    public Task<int> CountByIpAsync(byte[] ipKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
        Rows.AsNoTracking().CountAsync(a => a.IpKeyHash == ipKeyHash && a.AttemptedAt >= since, ct);

    public Task<int> CountByAccountAsync(byte[] accountKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
        Rows.AsNoTracking().CountAsync(a => a.AccountKeyHash == accountKeyHash && a.AttemptedAt >= since, ct);

    public async Task<int> DeleteByAccountAsync(byte[] accountKeyHash, CancellationToken ct = default)
    {
        var rows = await Rows.Where(a => a.AccountKeyHash == accountKeyHash).ToListAsync(ct);
        Rows.RemoveRange(rows);
        return rows.Count;
    }

    // No maximo um lote por chamada, em transacao propria; o limite vai no proprio DELETE (subconsulta com LIMIT).
    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct = default)
    {
        EnsureNoOpenUnitOfWork();
        return await BatchedDelete.RunAsync(
            context,
            () => Rows.Where(a => a.AttemptedAt < cutoff).OrderBy(a => a.AttemptedAt).Take(batchSize).ExecuteDeleteAsync(ct),
            ct);
    }

    // As operacoes imediatas nao podem rodar dentro de uma unidade de trabalho: sobreviveriam ao rollback dela (REQ-032).
    private void EnsureNoOpenUnitOfWork()
    {
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Immediate access attempt operations cannot run inside a unit of work.");
        }
    }
}
