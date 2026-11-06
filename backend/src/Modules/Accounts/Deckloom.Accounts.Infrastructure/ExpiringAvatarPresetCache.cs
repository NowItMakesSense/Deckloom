using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// Cache em memoria de todo o catalogo ativo por 5 min contados desde a carga (nao renova no acesso).
// Leituras concorrentes compartilham a mesma carga em andamento; uma carga que falhou nao fica em cache.
internal sealed class ExpiringAvatarPresetCache(IDbContextFactory<AccountsDbContext> contexts, TimeProvider time)
    : IAvatarPresetCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly object _sync = new();
    private Task<IReadOnlyList<AvatarPresetRow>>? _load;
    private DateTimeOffset _loadedAt;

    public Task<IReadOnlyList<AvatarPresetRow>> GetActiveAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            var now = time.GetUtcNow();
            if (_load is null || _load.IsFaulted || _load.IsCanceled || now - _loadedAt >= Lifetime)
            {
                _load = Load();
                _loadedAt = now;
            }

            return _load.WaitAsync(ct);
        }
    }

    // A carga compartilhada nao usa o token de um chamador: cancelar um leitor nao pode derrubar os demais.
    private async Task<IReadOnlyList<AvatarPresetRow>> Load()
    {
        await using var context = await contexts.CreateDbContextAsync();
        var rows = await context.Set<AvatarPresetRow>()
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync();
        return rows
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
    }
}
