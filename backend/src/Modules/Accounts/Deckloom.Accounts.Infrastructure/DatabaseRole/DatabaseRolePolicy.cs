namespace Deckloom.Accounts.Infrastructure;

/// <summary>Decisao pura (sem I/O) sobre a aceitabilidade do papel de conexao (DB7/UA2).</summary>
public static class DatabaseRolePolicy
{
    public const string SuperuserCode = "DB7_SUPERUSER";
    public const string CreateRoleCode = "DB7_CREATE_ROLE";
    public const string CreateDatabaseCode = "DB7_CREATE_DATABASE";
    public const string BypassRlsCode = "DB7_BYPASS_RLS";
    public const string CreateInDatabaseCode = "DB7_CREATE_IN_DATABASE";
    public const string CreateInAccountsSchemaCode = "DB7_CREATE_IN_ACCOUNTS_SCHEMA";
    public const string CreateInDeckSuggestionSchemaCode = "DB7_CREATE_IN_DECK_SUGGESTION_SCHEMA";
    public const string CreateInCatalogSchemaCode = "DB7_CREATE_IN_CATALOG_SCHEMA";
    public const string OwnsObjectsCode = "DB7_OWNS_OBJECTS";

    private sealed record Condition(string Code, string Message, Func<RoleSnapshot, bool> IsViolated);

    private static readonly Condition[] Conditions =
    [
        new(SuperuserCode, "O papel de conexao e superusuario.", s => s.IsSuperuser),
        new(CreateRoleCode, "O papel de conexao pode criar papeis.", s => s.CanCreateRole),
        new(CreateDatabaseCode, "O papel de conexao pode criar bancos.", s => s.CanCreateDatabase),
        new(BypassRlsCode, "O papel de conexao ignora a seguranca em nivel de linha.", s => s.BypassesRowLevelSecurity),
        new(CreateInDatabaseCode, "O papel de conexao pode criar objetos no banco.", s => s.CanCreateInDatabase),
        new(CreateInAccountsSchemaCode, "O papel de conexao pode criar objetos no esquema de contas.", s => s.CanCreateInAccountsSchema),
        new(CreateInDeckSuggestionSchemaCode, "O papel de conexao pode criar objetos no esquema de sugestao de decks.", s => s.CanCreateInDeckSuggestionSchema),
        new(CreateInCatalogSchemaCode, "O papel de conexao pode criar objetos no esquema do catalogo.", s => s.CanCreateInCatalogSchema),
        new(OwnsObjectsCode, "O papel de conexao e dono de objetos.", s => s.OwnsObjects),
    ];

    public static DatabaseRoleDecision Evaluate(RoleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var reasons = Conditions
            .Where(c => c.IsViolated(snapshot))
            .Select(c => new DatabaseRoleRejection(c.Code, c.Message))
            .ToList();

        return new DatabaseRoleDecision(reasons);
    }
}
