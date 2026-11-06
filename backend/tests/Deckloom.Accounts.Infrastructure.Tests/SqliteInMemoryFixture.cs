using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-055 (ADR-0012). Banco SQLite em memoria, criado e descartado por teste (uma instancia por teste).
// Sem connection string real, sem arquivo em disco, sem acesso a banco do sistema.
// Contrato fixado aqui para o implementer (GREEN):
//   namespace Deckloom.Accounts.Infrastructure
//   class AccountsDbContext : DbContext                       // NAO sealed
//     public const string SchemaName = "accounts"
//     public AccountsDbContext(DbContextOptions<AccountsDbContext> options)
//     protected virtual void ConfigureConcurrencyToken(EntityTypeBuilder<Player> player)   // padrao: xmin (Npgsql, no GREEN/composition root)
//     public DbSet<Player> Players
//   Mapeamento: HasDefaultSchema("accounts"), tabela player, colunas snake_case de Database/tables/accounts.player.md,
//   indice unico ux_player_email; value objects via conversores/owned; o SQLite ignora o schema (tabela "player").
//   Os CHECKs com char_length() nao existem no SQLite: aplicar so no provider PostgreSQL (ou usar length()).
//   sealed class EfPlayerRepository : IPlayerRepository       // ctor(AccountsDbContext)
//     Add/Save/Remove so ENFILEIRAM no contexto (decisao T-024); nada e gravado ate o commit.
//   sealed class EfUnitOfWork : IUnitOfWork                   // ctor(AccountsDbContext)
//     CommitAsync faz SaveChangesAsync e traduz:
//       violacao de ux_player_email  -> UniqueConstraintViolationException("ux_player_email")
//       DbUpdateConcurrencyException -> ConcurrencyConflictException
//     nenhuma excecao do EF/Sqlite vaza pela porta.
public sealed class SqliteInMemoryFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AccountsDbContext> _options;

    public SqliteInMemoryFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AccountsDbContext>().UseSqlite(_connection).Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
        SeedAvatarPresets();
    }

    // T-066: a FK fk_player_avatar_preset (RESTRICT) exige que o preset do Jogador exista. Os testes de persistencia usam
    // "preset-1"; ele e inserido de forma idempotente (inativo, nao padrao) sem alterar os presets do seed oficial.
    public static readonly string[] DefaultAvatarPresetIds = ["preset-1"];

    public void SeedAvatarPresets(params string[] ids)
    {
        var presets = ids.Length == 0 ? DefaultAvatarPresetIds : ids;

        foreach (var id in presets)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT OR IGNORE INTO avatar_preset (id, label, image_path, is_default, is_active, sort_order) " +
                "VALUES ($id, $id, $path, 0, 0, 1000)";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$path", $"test/{id}.png");
            command.ExecuteNonQuery();
        }
    }

    public SqliteConnection Connection => _connection;

    // Cada chamada devolve um contexto novo sobre a MESMA conexao em memoria (mesmo banco).
    public AccountsDbContext CreateContext() => new TestAccountsDbContext(_options);

    // T-062 (REQ-046): a FK player_preferred_format.format_id -> format(id) RESTRICT exige que o formato exista antes
    // de gravar a preferencia. A tabela format e do contexto Sugestao de Deck; aqui ela so e criada se o modelo de
    // Accounts ainda nao a criou, e as linhas sao inseridas de forma idempotente (apenas id, o unico campo do ancora).
    public static readonly string[] DefaultFormatIds = ["commander", "modern", "pauper", "legacy"];

    public void SeedFormats(params string[] ids)
    {
        var formats = ids.Length == 0 ? DefaultFormatIds : ids;

        Run(
            "CREATE TABLE IF NOT EXISTS format (id text NOT NULL PRIMARY KEY)");

        foreach (var id in formats)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "INSERT OR IGNORE INTO format (id) VALUES ($id)";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    private void Run(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
