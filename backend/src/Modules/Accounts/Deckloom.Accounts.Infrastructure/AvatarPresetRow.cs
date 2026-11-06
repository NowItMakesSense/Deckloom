namespace Deckloom.Accounts.Infrastructure;

// Linha de Database/tables/accounts.avatar_preset.md. Catalogo gerido fora da aplicacao; aqui so e lido.
internal sealed class AvatarPresetRow
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string ImagePath { get; init; } = string.Empty;

    public bool IsDefault { get; init; }

    public bool IsActive { get; init; }

    public int SortOrder { get; init; }
}
