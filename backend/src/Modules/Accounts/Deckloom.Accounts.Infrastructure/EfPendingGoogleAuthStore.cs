using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// AddAsync so enfileira no contexto (grava no commit). ConsumeAsync e o claim atomico: um DELETE condicional por id
// que ocorre na hora; so quem apagou a linha recebe a pendencia, o perdedor recebe nulo (nunca excecao). O "agora"
// vem so do TimeProvider; a limpeza periodica (job) chega com T-075.
public sealed class EfPendingGoogleAuthStore(AccountsDbContext context, TimeProvider clock) : IPendingGoogleAuthStore
{
    public Task AddAsync(PendingGoogleAuth pending, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pending);

        context.Set<PendingGoogleAuthRow>().Add(ToRow(pending));
        return Task.CompletedTask;
    }

    public async Task<PendingGoogleAuth?> FindAsync(TokenHash tokenHash, CancellationToken ct = default)
    {
        var row = await FindLiveRow(tokenHash, ct);
        return row is null ? null : ToPending(row);
    }

    public async Task<PendingGoogleAuth?> ConsumeAsync(TokenHash tokenHash, CancellationToken ct = default)
    {
        var row = await FindLiveRow(tokenHash, ct);
        if (row is null)
        {
            return null;
        }

        var claimed = await context.Set<PendingGoogleAuthRow>()
            .Where(p => p.Id == row.Id)
            .ExecuteDeleteAsync(ct);

        return claimed == 1 ? ToPending(row) : null;
    }

    // No maximo um lote por chamada, em transacao propria. O filtro de expiracao roda no banco (DELETE com predicado e
    // LIMIT, sem ler linhas).
    public Task<int> DeleteExpiredAsync(int batchSize, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return BatchedDelete.RunIfAnyAsync(
            context,
            context.Set<PendingGoogleAuthRow>().Where(p => p.ExpiresAt <= now),
            () => context.Set<PendingGoogleAuthRow>()
                .Where(p => p.ExpiresAt <= now)
                .OrderBy(p => p.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct),
            ct);
    }

    private async Task<PendingGoogleAuthRow?> FindLiveRow(TokenHash tokenHash, CancellationToken ct)
    {
        var row = await context.Set<PendingGoogleAuthRow>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TokenHash == tokenHash, ct);

        return row is not null && row.ExpiresAt > clock.GetUtcNow() ? row : null;
    }

    private static PendingGoogleAuthRow ToRow(PendingGoogleAuth pending) => pending switch
    {
        PendingGoogleSignup signup => NewRow(signup, PendingGoogleAuthRow.SignupKind, signup.SuggestedDisplayName, null),
        PendingGoogleLink link => NewRow(link, PendingGoogleAuthRow.LinkKind, null, link.TargetPlayerId),
        _ => throw new ArgumentException("Unknown pending Google authentication kind.", nameof(pending)),
    };

    private static PendingGoogleAuthRow NewRow(
        PendingGoogleAuth pending,
        string kind,
        string? suggestedDisplayName,
        PlayerId? targetPlayerId) =>
        new()
        {
            Id = pending.Id,
            TokenHash = pending.TokenHash,
            Kind = kind,
            GoogleSubject = pending.GoogleSubject,
            Email = pending.Email,
            SuggestedDisplayName = suggestedDisplayName,
            TargetPlayerId = targetPlayerId,
            CreatedAt = pending.CreatedAt,
            ExpiresAt = pending.ExpiresAt,
        };

    private static PendingGoogleAuth ToPending(PendingGoogleAuthRow row) => row.Kind switch
    {
        PendingGoogleAuthRow.SignupKind => new PendingGoogleSignup(
            row.Id,
            row.TokenHash,
            row.GoogleSubject,
            row.Email,
            row.CreatedAt,
            row.ExpiresAt,
            row.SuggestedDisplayName),
        PendingGoogleAuthRow.LinkKind => new PendingGoogleLink(
            row.Id,
            row.TokenHash,
            row.GoogleSubject,
            row.Email,
            row.CreatedAt,
            row.ExpiresAt,
            row.TargetPlayerId!.Value),
        _ => throw new InvalidOperationException($"Unknown pending Google authentication kind '{row.Kind}'."),
    };
}
