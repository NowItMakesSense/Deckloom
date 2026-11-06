namespace Deckloom.Catalog.Infrastructure;

// Linha de catalog.tcg (dono: Catalogo, ADR-0007).
internal sealed class TcgRow
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int SortOrder { get; init; }
}
