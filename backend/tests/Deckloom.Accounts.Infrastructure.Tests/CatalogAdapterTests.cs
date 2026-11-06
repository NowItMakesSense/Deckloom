using System.Globalization;
using Deckloom.Accounts.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-062 (REQ-018, 022, 037, 046). Tabelas: accounts.avatar_preset,
// accounts.player_preferred_format; ADR-0007; design.md (cache em memoria de 5 min). Contrato para o implementer (GREEN):
//   sealed class EfAvatarPresetCatalog : IAvatarPresetCatalog  // namespace Deckloom.Accounts.Infrastructure
//     ctor(IDbContextFactory<AccountsDbContext> contexts, TimeProvider time)
//       // factory porque o cache vive mais que um escopo de requisicao; cada leitura cria e descarta um contexto.
//     Cache de TODO o catalogo (linhas ativas) por 5 min contados desde a carga (nao renova no acesso); ListActiveAsync,
//       GetDefaultAsync e ContainsActiveAsync compartilham o mesmo cache. Mudanca no banco so aparece 5 min depois.
//     ListActiveAsync: so is_active = true, ordenado por sort_order e depois id (ordinal); o padrao aparece com IsDefault.
//     GetDefaultAsync: o preset is_default; ContainsActiveAsync: true so para id ativo.
//   AccountsDbContext passa a mapear a tabela avatar_preset (snake_case; o SQLite ignora o schema): id text PK
//     pk_avatar_preset, label, image_path, is_default, is_active, sort_order (todas NOT NULL), indice unico parcial
//     ux_avatar_preset_single_default em (is_default) WHERE is_default = true.
//   Tambem (REQ-037/046): player_preferred_format.format_id -> format(id) com ON DELETE RESTRICT
//     (fk_player_preferred_format_format; tabela format e do contexto Sugestao de Deck, ADR-0001/0007).
public sealed class CatalogAdapterTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteInMemoryFixture _db = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly CountingFactory _factory;

    public CatalogAdapterTests()
    {
        _factory = new CountingFactory(_db);

        // T-065: o modelo ja traz a carga inicial (HasData); estes testes controlam o catalogo por completo.
        Execute("DELETE FROM avatar_preset");
    }

    public void Dispose() => _db.Dispose();

    private sealed class CountingFactory(SqliteInMemoryFixture db) : IDbContextFactory<AccountsDbContext>
    {
        public int Created { get; private set; }

        public AccountsDbContext CreateDbContext()
        {
            Created++;
            return db.CreateContext();
        }
    }

    private EfAvatarPresetCatalog NewCatalog() => new(_factory, _time);

    private void Preset(string id, string label, int order, bool isDefault = false, bool isActive = true) =>
        Execute(
            "INSERT INTO avatar_preset (id, label, image_path, is_default, is_active, sort_order) VALUES " +
            $"('{id}', '{label}', '/avatars/presets/{id}.svg', {(isDefault ? 1 : 0)}, {(isActive ? 1 : 0)}, {order})");

    private void SeedCatalog()
    {
        Preset("mago-azul", "Mago azul", 2, isDefault: true);
        Preset("guerreiro", "Guerreiro", 1);
        Preset("arqueira", "Arqueira", 3);
        Preset("antigo", "Antigo", 4, isActive: false);
    }

    private void Execute(string sql)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    private List<string> Column(string sql)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    [Fact]
    public async Task REQ_018_the_gallery_lists_only_active_presets_ordered_by_sort_order()
    {
        SeedCatalog();

        var items = await NewCatalog().ListActiveAsync();

        Assert.Equal(["guerreiro", "mago-azul", "arqueira"], items.Select(i => i.Id));
        var first = items[0];
        Assert.Equal("Guerreiro", first.Label);
        Assert.Equal("/avatars/presets/guerreiro.svg", first.ImagePath);
        Assert.False(first.IsDefault);
    }

    [Fact]
    public async Task REQ_018_presets_with_the_same_sort_order_are_ordered_by_id()
    {
        Preset("b-preset", "B", 1, isDefault: true);
        Preset("a-preset", "A", 1);

        var items = await NewCatalog().ListActiveAsync();

        Assert.Equal(["a-preset", "b-preset"], items.Select(i => i.Id));
    }

    [Fact]
    public async Task REQ_022_the_default_preset_is_exposed_and_flagged_in_the_listing()
    {
        SeedCatalog();
        var catalog = NewCatalog();

        var preset = await catalog.GetDefaultAsync();
        var items = await catalog.ListActiveAsync();

        Assert.Equal("mago-azul", preset.Id);
        Assert.True(preset.IsDefault);
        Assert.Equal(["mago-azul"], items.Where(i => i.IsDefault).Select(i => i.Id));
    }

    [Fact]
    public async Task REQ_018_contains_active_is_true_only_for_active_presets()
    {
        SeedCatalog();
        var catalog = NewCatalog();

        Assert.True(await catalog.ContainsActiveAsync("guerreiro"));
        Assert.False(await catalog.ContainsActiveAsync("antigo"));
        Assert.False(await catalog.ContainsActiveAsync("nao-existe"));
    }

    [Fact]
    public async Task REQ_018_a_change_in_the_database_only_shows_after_five_minutes()
    {
        SeedCatalog();
        var catalog = NewCatalog();
        Assert.Equal(3, (await catalog.ListActiveAsync()).Count);

        Execute("UPDATE avatar_preset SET is_active = 0 WHERE id = 'arqueira'");
        Preset("novo", "Novo", 9);

        _time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.Equal(["guerreiro", "mago-azul", "arqueira"], (await catalog.ListActiveAsync()).Select(i => i.Id));

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(["guerreiro", "mago-azul", "novo"], (await catalog.ListActiveAsync()).Select(i => i.Id));
    }

    [Fact]
    public async Task REQ_018_reads_within_the_window_reuse_the_cache_without_opening_a_new_context()
    {
        SeedCatalog();
        var catalog = NewCatalog();

        await catalog.ListActiveAsync();
        await catalog.GetDefaultAsync();
        await catalog.ContainsActiveAsync("guerreiro");
        await catalog.ListActiveAsync();

        Assert.Equal(1, _factory.Created);
    }

    [Fact]
    public async Task REQ_018_the_window_counts_from_the_load_and_is_not_renewed_by_reads()
    {
        SeedCatalog();
        var catalog = NewCatalog();
        await catalog.ListActiveAsync();

        _time.Advance(TimeSpan.FromMinutes(3));
        await catalog.ListActiveAsync();
        Execute("UPDATE avatar_preset SET is_active = 0 WHERE id = 'arqueira'");

        _time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        Assert.Equal(["guerreiro", "mago-azul"], (await catalog.ListActiveAsync()).Select(i => i.Id));
        Assert.Equal(2, _factory.Created);
    }

    [Fact]
    public async Task REQ_022_contains_active_and_default_share_the_same_five_minute_cache()
    {
        SeedCatalog();
        var catalog = NewCatalog();
        Assert.True(await catalog.ContainsActiveAsync("guerreiro"));

        Execute("UPDATE avatar_preset SET is_active = 0 WHERE id = 'guerreiro'");
        Assert.True(await catalog.ContainsActiveAsync("guerreiro"));

        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.False(await catalog.ContainsActiveAsync("guerreiro"));
        Assert.Equal("mago-azul", (await catalog.GetDefaultAsync()).Id);
    }

    [Fact]
    public async Task REQ_018_the_catalog_reads_do_not_modify_the_table()
    {
        SeedCatalog();

        await NewCatalog().ListActiveAsync();

        Assert.Equal(4, Scalar<int>("SELECT COUNT(*) FROM avatar_preset"));
    }

    [Fact]
    public Task REQ_018_the_schema_has_the_documented_columns_primary_key_and_single_default_index()
    {
        Assert.Equal(
            ["id", "image_path", "is_active", "is_default", "label", "sort_order"],
            Column("SELECT name FROM pragma_table_info('avatar_preset') ORDER BY name"));
        Assert.Equal(
            ["id", "image_path", "is_active", "is_default", "label", "sort_order"],
            Column("SELECT name FROM pragma_table_info('avatar_preset') WHERE \"notnull\" = 1 OR pk > 0 ORDER BY name"));
        Assert.Equal(["id"], Column("SELECT name FROM pragma_table_info('avatar_preset') WHERE pk > 0"));
        Assert.Equal(
            ["is_default"],
            Column("SELECT name FROM pragma_index_info('ux_avatar_preset_single_default')"));
        Assert.Equal(
            1,
            Scalar<int>("SELECT \"unique\" FROM pragma_index_list('avatar_preset') WHERE name = 'ux_avatar_preset_single_default'"));
        Assert.Equal(
            1,
            Scalar<int>("SELECT partial FROM pragma_index_list('avatar_preset') WHERE name = 'ux_avatar_preset_single_default'"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task REQ_022_the_database_rejects_a_second_default_preset()
    {
        Preset("mago-azul", "Mago azul", 1, isDefault: true);

        Assert.Throws<SqliteException>(() => Preset("outro", "Outro", 2, isDefault: true));
        return Task.CompletedTask;
    }

    // REQ-037/046 (ADR-0007): Formato nunca e apagado enquanto referenciado. A tabela format e do contexto Sugestao de
    // Deck; o teste a cria so se o modelo de Accounts ainda nao a criou (IF NOT EXISTS), sem depender de como a FK e mapeada.
    [Fact]
    public async Task REQ_046_the_preferred_format_foreign_key_to_format_is_restrict()
    {
        EnsureFormatTable();
        _db.SeedAvatarPresets(); // T-066: o construtor esvazia avatar_preset; a FK do Jogador exige o preset.
        Execute("INSERT INTO format (id, name, is_active, sort_order) VALUES ('modern', 'Modern', 1, 1)");
        await Persist(PlayerWithFormats(new FormatId("modern")));

        Assert.Equal(
            "RESTRICT",
            Scalar<string>("SELECT on_delete FROM pragma_foreign_key_list('player_preferred_format') WHERE \"from\" = 'format_id' AND \"table\" = 'format'"));
        Assert.Throws<SqliteException>(() => Execute("DELETE FROM format WHERE id = 'modern'"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM format"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player_preferred_format"));
    }

    [Fact]
    public async Task REQ_046_deactivating_a_referenced_format_keeps_the_row_and_the_player_preference()
    {
        EnsureFormatTable();
        _db.SeedAvatarPresets(); // T-066: o construtor esvazia avatar_preset; a FK do Jogador exige o preset.
        Execute("INSERT INTO format (id, name, is_active, sort_order) VALUES ('modern', 'Modern', 1, 1)");
        await Persist(PlayerWithFormats(new FormatId("modern")));

        Execute("UPDATE format SET is_active = 0 WHERE id = 'modern'");

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM format"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player_preferred_format WHERE format_id = 'modern'"));
    }

    private void EnsureFormatTable() =>
        Execute(
            "CREATE TABLE IF NOT EXISTS format (id text NOT NULL PRIMARY KEY, name text NOT NULL, " +
            "is_active integer NOT NULL, sort_order integer NOT NULL)");

    private static Player PlayerWithFormats(params FormatId[] formats)
    {
        var player = Player.RegisterWithPassword(
            Email.Create("ana@example.com").Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;
        Assert.True(player.UpdateProfile("Ana Souza", null, "mtg", formats, formats).IsSuccess);
        return player;
    }

    private async Task Persist(Player player)
    {
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
    }
}
