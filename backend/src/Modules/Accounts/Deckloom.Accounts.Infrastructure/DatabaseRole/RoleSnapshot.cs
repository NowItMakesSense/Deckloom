namespace Deckloom.Accounts.Infrastructure;

/// <summary>Atributos do papel de conexao da API, lidos do catalogo (T-135) e avaliados por <see cref="DatabaseRolePolicy"/>.</summary>
public sealed record RoleSnapshot
{
    public string RoleName { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    public bool IsSuperuser { get; init; }

    public bool CanCreateRole { get; init; }

    public bool CanCreateDatabase { get; init; }

    public bool BypassesRowLevelSecurity { get; init; }

    public bool CanCreateInDatabase { get; init; }

    public bool CanCreateInAccountsSchema { get; init; }

    public bool CanCreateInDeckSuggestionSchema { get; init; }

    public bool OwnsObjects { get; init; }
}
