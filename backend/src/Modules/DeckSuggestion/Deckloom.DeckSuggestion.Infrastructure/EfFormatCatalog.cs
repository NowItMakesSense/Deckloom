using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.DeckSuggestion.Infrastructure;

// Adapter da porta IFormatCatalog de Contas sobre a tabela format de Sugestao de Deck (ADR-0001/0007).
public sealed class EfFormatCatalog(FormatCatalogDbContext context) : IFormatCatalog
{
    public async Task<IReadOnlyList<FormatItem>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await context.Formats.Where(f => f.IsActive).ToListAsync(ct);
        return rows
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => new FormatItem(new FormatId(f.Id), f.Name))
            .ToList();
    }

    public Task<bool> ContainsAsync(FormatId id, CancellationToken ct = default) =>
        context.Formats.AnyAsync(f => f.IsActive && f.Id == id.Value, ct);
}
