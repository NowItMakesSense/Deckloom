using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// T-138 (REQ-047, DB9): cada lote de limpeza roda numa transacao curta propria, confirmada antes de voltar. O commit
// ignora o cancelamento: o lote ja executado nao pode ficar pela metade.
internal static class BatchedDelete
{
    // Sem candidata (consulta EXISTS no banco, nenhuma linha lida), nao abre transacao nem emite DELETE. O DELETE segue
    // condicional ao predicado, entao uma execucao concorrente que ja apagou as linhas nao e erro.
    public static async Task<int> RunIfAnyAsync<T>(
        DbContext context,
        IQueryable<T> candidates,
        Func<Task<int>> deleteBatch,
        CancellationToken ct)
    {
        if (!await candidates.AnyAsync(ct))
        {
            return 0;
        }

        return await RunAsync(context, deleteBatch, ct);
    }

    public static async Task<int> RunAsync(DbContext context, Func<Task<int>> deleteBatch, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var deleted = await deleteBatch();
            await transaction.CommitAsync(CancellationToken.None);
            return deleted;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
