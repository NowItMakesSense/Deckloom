using System.Reflection;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// Add/Save/Remove so enfileiram no contexto (T-024); Save assume Jogador rastreado (carregado pelo repositorio) e o EF gera UPDATE so das colunas alteradas (T-133): unicidade e concorrencia estouram em EfUnitOfWork.CommitAsync.
// As consultas devolvem o Jogador com ExternalIdentity (owned) e Formats (linhas de player_preferred_format).
// A consulta por token de redefinicao nao filtra por expiracao: a validade e decidida pelo agregado.
public sealed class EfPlayerRepository(AccountsDbContext context) : IPlayerRepository
{
    private static readonly PropertyInfo FormatsProperty = typeof(Player).GetProperty(nameof(Player.Formats))!;

    public async Task<Player?> GetById(PlayerId id, CancellationToken ct = default) =>
        await WithFormats(await context.Players.FindAsync([id], ct), ct);

    public async Task<Player?> FindByEmail(Email email, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        return await WithFormats(await context.Players.FirstOrDefaultAsync(p => p.Email == normalized, ct), ct);
    }

    public async Task<Player?> FindByGoogleSubject(string subject, CancellationToken ct = default) =>
        await WithFormats(
            await context.Players.FirstOrDefaultAsync(
                p => p.ExternalIdentities.Any(
                    i => i.Provider == ExternalIdentity.GoogleProvider && i.Subject == subject),
                ct),
            ct);

    public async Task<Player?> FindByResetTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
        await WithFormats(
            await context.Players.FirstOrDefaultAsync(p => p.PasswordResetToken!.TokenHash == tokenHash, ct),
            ct);

    public async Task<bool> ExistsByEmail(Email email, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        return await context.Players.AnyAsync(p => p.Email == normalized, ct);
    }

    public Task Add(Player player, CancellationToken ct = default)
    {
        context.Players.Add(player);
        context.MarkFormatsLoaded(player.Id);
        return Task.CompletedTask;
    }

    public Task Save(Player player, CancellationToken ct = default)
    {
        context.EnsureTracked(player);
        context.MarkFormatsLoaded(player.Id);
        return Task.CompletedTask;
    }

    public Task Remove(Player player, CancellationToken ct = default)
    {
        context.Players.Remove(player);
        return Task.CompletedTask;
    }

    // IMEDIATO (fora do EfUnitOfWork). O filtro de expiracao roda no banco (DELETE com predicado e LIMIT, sem ler
    // linhas); o Jogador permanece.
    public Task<int> DeleteExpiredResetTokens(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
        BatchedDelete.RunIfAnyAsync(
            context,
            context.Players.Where(p => p.PasswordResetToken != null && p.PasswordResetToken.ExpiresAt <= now),
            () => context.Players
                .Where(p => p.PasswordResetToken != null && p.PasswordResetToken.ExpiresAt <= now)
                .OrderBy(p => p.Id)
                .Take(batchSize)
                .Select(p => p.PasswordResetToken!)
                .ExecuteDeleteAsync(ct),
            ct);

    private static Email Normalize(Email email) =>
        Email.Create(email.Value.Trim().ToLowerInvariant()).Value;

    // Carrega os formatos uma unica vez por Jogador e contexto: reler sobrescreveria alteracoes ainda nao salvas.
    private async Task<Player?> WithFormats(Player? player, CancellationToken ct)
    {
        if (player is null || context.AreFormatsLoaded(player.Id))
        {
            return player;
        }

        var ids = await context.Set<PlayerPreferredFormat>()
            .Where(r => r.PlayerId == player.Id)
            .Select(r => r.FormatId)
            .ToListAsync(ct);
        var formatIds = ids.Select(id => new FormatId(id)).ToList();

        var restored = FormatPreferences.Empty.Update(formatIds, formatIds).Value;
        FormatsProperty.SetValue(player, restored);
        context.MarkFormatsLoaded(player.Id);
        return player;
    }
}
