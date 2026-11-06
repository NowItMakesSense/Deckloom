using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public sealed record FormatItem(FormatId Id, string Name);

public interface IFormatCatalog
{
    Task<IReadOnlyList<FormatItem>> ListActiveAsync(CancellationToken ct = default);

    Task<bool> ContainsAsync(FormatId id, CancellationToken ct = default);
}
