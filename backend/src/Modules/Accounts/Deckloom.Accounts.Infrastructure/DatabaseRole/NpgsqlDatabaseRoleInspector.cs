using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

/// <summary>
/// Le do catalogo do PostgreSQL os atributos de <c>current_user</c> (T-135). Somente leitura; nao validavel
/// pelo SQLite, a consulta real e verificada por humano.
/// </summary>
public sealed class NpgsqlDatabaseRoleInspector(IDbContextFactory<AccountsDbContext> contextFactory) : IDatabaseRoleInspector
{
    // Colunas, em ordem: rolname, current_database(), rolsuper, rolcreaterole, rolcreatedb, rolbypassrls,
    // CREATE no banco, CREATE no esquema accounts, CREATE no esquema deck_suggestion, posse de objetos ou
    // esquemas nos dois esquemas. Esquema inexistente conta como "sem privilegio" em vez de falhar.
    private const string Query = """
        SELECT r.rolname,
               current_database(),
               r.rolsuper,
               r.rolcreaterole,
               r.rolcreatedb,
               r.rolbypassrls,
               has_database_privilege(r.rolname, current_database(), 'CREATE'),
               CASE WHEN to_regnamespace('accounts') IS NULL THEN false
                    ELSE has_schema_privilege(r.rolname, 'accounts', 'CREATE') END,
               CASE WHEN to_regnamespace('deck_suggestion') IS NULL THEN false
                    ELSE has_schema_privilege(r.rolname, 'deck_suggestion', 'CREATE') END,
               EXISTS (SELECT 1 FROM pg_namespace n
                       WHERE n.nspname IN ('accounts', 'deck_suggestion') AND n.nspowner = r.oid)
               OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                          WHERE n.nspname IN ('accounts', 'deck_suggestion') AND c.relowner = r.oid)
        FROM pg_roles r
        WHERE r.rolname = current_user
        """;

    public async Task<RoleSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = Query;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Role not found in catalog.");
        }

        return new RoleSnapshot
        {
            RoleName = reader.GetString(0),
            DatabaseName = reader.GetString(1),
            IsSuperuser = reader.GetBoolean(2),
            CanCreateRole = reader.GetBoolean(3),
            CanCreateDatabase = reader.GetBoolean(4),
            BypassesRowLevelSecurity = reader.GetBoolean(5),
            CanCreateInDatabase = reader.GetBoolean(6),
            CanCreateInAccountsSchema = reader.GetBoolean(7),
            CanCreateInDeckSuggestionSchema = reader.GetBoolean(8),
            OwnsObjects = reader.GetBoolean(9),
        };
    }
}
