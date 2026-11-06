namespace Deckloom.Catalog.Application;

// Item do catalogo de TCGs: id textual estavel (ADR-0007) e nome de exibicao.
public sealed record TcgItem(string Id, string Name);

// Porta somente leitura do catalogo de TCGs (ADR-0007): so TCGs ativos, ordenados por sort_order e depois por id.
public interface ITcgCatalog
{
    Task<IReadOnlyList<TcgItem>> ListActiveAsync(CancellationToken ct = default);

    Task<bool> ContainsAsync(string id, CancellationToken ct = default);
}
