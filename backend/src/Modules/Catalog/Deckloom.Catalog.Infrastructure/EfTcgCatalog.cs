using Deckloom.Catalog.Application;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Catalog.Infrastructure;

// Adapter da porta ITcgCatalog sobre a tabela catalog.tcg (ADR-0001/0007).
public sealed class EfTcgCatalog(TcgCatalogDbContext context) : ITcgCatalog
{
    public async Task<IReadOnlyList<TcgItem>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await context.Tcgs.Where(t => t.IsActive).ToListAsync(ct);
        return rows
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => new TcgItem(t.Id, t.Name))
            .ToList();
    }

    public Task<bool> ContainsAsync(string id, CancellationToken ct = default) =>
        context.Tcgs.AnyAsync(t => t.IsActive && t.Id == id, ct);
}
