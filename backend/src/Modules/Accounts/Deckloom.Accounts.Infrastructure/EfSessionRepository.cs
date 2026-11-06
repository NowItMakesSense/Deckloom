using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// Add/Save so enfileiram no contexto: nada e gravado ate EfUnitOfWork.CommitAsync. RevokeAllFor carrega as sessoes
// ainda nao revogadas do Jogador, aplica Session.Revoke (que preserva a primeira revogacao) e devolve a contagem;
// descartar o contexto sem commit nao grava nada.
public sealed class EfSessionRepository(AccountsDbContext context) : ISessionRepository
{
    public Task Add(Session session, CancellationToken ct = default)
    {
        context.Set<Session>().Add(session);
        return Task.CompletedTask;
    }

    public async Task<Session?> FindById(SessionId id, CancellationToken ct = default) =>
        await context.Set<Session>().FindAsync([id], ct);

    public async Task<Session?> FindByTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
        await context.Set<Session>().FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

    public Task Save(Session session, CancellationToken ct = default)
    {
        context.EnsureTracked(session);
        return Task.CompletedTask;
    }

    public async Task<int> RevokeAllFor(
        PlayerId playerId,
        SessionRevocationReason reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var active = await context.Set<Session>()
            .Where(s => s.PlayerId == playerId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var session in active)
        {
            session.Revoke(reason, now);
        }

        return active.Count;
    }

    // IMEDIATO (fora do EfUnitOfWork). O filtro de data roda no banco (DELETE com predicado e LIMIT, sem ler linhas);
    // uma execucao concorrente que ja apagou a linha nao e erro.
    public Task<int> DeleteExpiredOrRevoked(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
        BatchedDelete.RunIfAnyAsync(
            context,
            context.Set<Session>().Where(s => s.RevokedAt != null || s.ExpiresAt <= now),
            () => context.Set<Session>()
                .Where(s => s.RevokedAt != null || s.ExpiresAt <= now)
                .OrderBy(s => s.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct),
            ct);
}
