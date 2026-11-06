namespace Deckloom.DeckSuggestion.Infrastructure;

// Linha de deck_suggestion.format (dono: Sugestao de Deck, ADR-0007).
internal sealed class FormatRow
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public int SortOrder { get; init; }
}
