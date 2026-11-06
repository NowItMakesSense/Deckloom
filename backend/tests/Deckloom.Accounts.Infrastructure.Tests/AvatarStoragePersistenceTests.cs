using System.Globalization;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-061 (REQ-019, 021, 024). Tabela: accounts.avatar_image; ADR-0009; design.md (P2). Contrato para
// o implementer (GREEN):
//   sealed class PostgresAvatarStorage : IAvatarStorage      // namespace Deckloom.Accounts.Infrastructure
//     ctor(AccountsDbContext context)                          // mesmo DbContext do EfPlayerRepository/EfUnitOfWork
//     SaveAsync(key, image) / RemoveAsync(key): so ENFILEIRAM no contexto (como EfPlayerRepository.Add/Save/Remove);
//       nada e gravado ate EfUnitOfWork.CommitAsync, que grava tudo num unico SaveChanges (mesma transacao do Jogador).
//       Sem commit (ou commit que falha), nada muda: a imagem anterior permanece.
//     ReadAsync(key): devolve os bytes gravados (copia) ou null se a chave nao existe.
//     DONO (player_id, NOT NULL): a porta nao recebe o PlayerId. Premissa dos testes: o dono e o Jogador RASTREADO no
//       mesmo contexto cujo Avatar e CustomAvatar com StorageKey == key (o caso de uso chama Player.SetCustomAvatar
//       antes de SaveAsync). Ver duvida aberta no relatorio.
//   Tabela avatar_image (snake_case; o SQLite ignora o schema): storage_key text PK pk_avatar_image; player_id NOT NULL,
//     indice unico ux_avatar_image_player_id, FK fk_avatar_image_player para player com ON DELETE CASCADE;
//     content_type text NOT NULL CHECK in ('image/webp'); content blob NOT NULL; created_at NOT NULL.
//     O CHECK octet_length(content) <= 262144 so existe no PostgreSQL: NAO e verificado aqui (limitacao do SQLite,
//     registrada na DoD de T-061). O limite de 256 KB na borda do adapter e o REFACTOR e nao esta neste RED.
public sealed class AvatarStoragePersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string KeyA = "key-a-0000000000000000000";
    private const string KeyB = "key-b-0000000000000000000";
    private const string KeyC = "key-c-0000000000000000000";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] ImageA = [0x52, 0x49, 0x46, 0x46, 0x01, 0x02, 0x03];
    private static readonly byte[] ImageB = [0x52, 0x49, 0x46, 0x46, 0x0A, 0x0B, 0x0C, 0x0D];
    private static readonly byte[] ImageC = [0x52, 0x49, 0x46, 0x46, 0xFF];

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player NewPlayer(string email) =>
        Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private async Task<PlayerId> PersistPlayer(string email = "ana@example.com")
    {
        var player = NewPlayer(email);
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player.Id;
    }

    // Fluxo do caso de uso UploadCustomAvatar: carrega o Jogador, troca o avatar, enfileira Save da imagem nova,
    // Save do Jogador e Remove da anterior, e confirma tudo de uma vez.
    private async Task SetCustomAvatar(PlayerId id, string key, byte[] image, string? previousKey = null)
    {
        await using var context = _db.CreateContext();
        var players = new EfPlayerRepository(context);
        var storage = new PostgresAvatarStorage(context);
        var player = (await players.GetById(id))!;
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        await storage.SaveAsync(key, image);
        await players.Save(player);
        if (previousKey is not null)
        {
            await storage.RemoveAsync(previousKey);
        }

        await new EfUnitOfWork(context).CommitAsync();
    }

    private async Task<byte[]?> Read(string key)
    {
        await using var context = _db.CreateContext();
        return await new PostgresAvatarStorage(context).ReadAsync(key);
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

    private int Rows() => Scalar<int>("SELECT COUNT(*) FROM avatar_image");

    [Fact]
    public async Task REQ_019_saved_image_is_stored_by_storage_key_and_read_back_byte_for_byte()
    {
        var id = await PersistPlayer();

        await SetCustomAvatar(id, KeyA, ImageA);

        Assert.Equal(ImageA, await Read(KeyA));
        Assert.Equal(1, Rows());
        Assert.Equal(KeyA, Scalar<string>("SELECT storage_key FROM avatar_image"));
        Assert.Equal("image/webp", Scalar<string>("SELECT content_type FROM avatar_image"));
    }

    [Fact]
    public async Task REQ_019_read_of_an_unknown_key_returns_null()
    {
        await PersistPlayer();

        Assert.Null(await Read("does-not-exist"));
    }

    [Fact]
    public async Task REQ_019_each_key_reads_back_the_bytes_of_its_own_player()
    {
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        await SetCustomAvatar(ana, KeyA, ImageA);
        await SetCustomAvatar(bia, KeyB, ImageB);

        Assert.Equal(ImageA, await Read(KeyA));
        Assert.Equal(ImageB, await Read(KeyB));
        Assert.Equal(2, Rows());
    }

    [Fact]
    public async Task REQ_019_save_only_queues_until_the_unit_of_work_commit()
    {
        var id = await PersistPlayer();

        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            var player = (await players.GetById(id))!;
            Assert.True(player.SetCustomAvatar(KeyA).IsSuccess);
            await new PostgresAvatarStorage(context).SaveAsync(KeyA, ImageA);
            await players.Save(player);
            Assert.Equal(0, Rows());
        }

        Assert.Equal(0, Rows());
        Assert.Null(await Read(KeyA));
    }

    [Fact]
    public async Task REQ_019_the_stored_copy_is_not_affected_by_later_changes_to_the_caller_array()
    {
        var id = await PersistPlayer();
        var image = ImageA.ToArray();

        await SetCustomAvatar(id, KeyA, image);
        image[0] = 0x00;

        Assert.Equal(ImageA, await Read(KeyA));
    }

    [Fact]
    public async Task REQ_019_the_schema_has_the_documented_columns_and_primary_key()
    {
        Assert.Equal(
            ["content", "content_type", "created_at", "player_id", "storage_key"],
            Column("SELECT name FROM pragma_table_info('avatar_image') ORDER BY name"));
        Assert.Equal(
            ["storage_key"],
            Column("SELECT name FROM pragma_table_info('avatar_image') WHERE pk > 0"));
        Assert.Equal(
            ["content", "content_type", "created_at", "player_id", "storage_key"],
            Column("SELECT name FROM pragma_table_info('avatar_image') WHERE \"notnull\" = 1 ORDER BY name"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task REQ_019_the_database_rejects_a_content_type_other_than_webp()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => Execute(
            $"INSERT INTO avatar_image (storage_key, player_id, content_type, content, created_at) " +
            $"SELECT 'other', player_id, 'image/png', x'AA', '2020-03-04 12:00:00' FROM avatar_image"));
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task REQ_021_the_schema_allows_at_most_one_image_per_player_through_ux_avatar_image_player_id()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        Assert.Equal(
            ["player_id"],
            Column("SELECT name FROM pragma_index_info('ux_avatar_image_player_id')"));
        Assert.Equal(1, Scalar<int>("SELECT \"unique\" FROM pragma_index_list('avatar_image') WHERE name = 'ux_avatar_image_player_id'"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => Execute(
            "INSERT INTO avatar_image (storage_key, player_id, content_type, content, created_at) " +
            "SELECT 'second', player_id, 'image/webp', x'AA', '2020-03-04 12:00:00' FROM avatar_image"));
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task REQ_024_the_foreign_key_to_player_cascades_on_delete()
    {
        await PersistPlayer();

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_list('avatar_image') WHERE \"table\" = 'player' AND \"from\" = 'player_id'"));
        Assert.Equal("CASCADE", Scalar<string>("SELECT on_delete FROM pragma_foreign_key_list('avatar_image') WHERE \"from\" = 'player_id'"));
    }

    [Fact]
    public async Task REQ_021_replacing_the_avatar_stores_the_new_image_and_deletes_the_previous_one_in_one_commit()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        await SetCustomAvatar(id, KeyB, ImageB, previousKey: KeyA);

        Assert.Equal(1, Rows());
        Assert.Null(await Read(KeyA));
        Assert.Equal(ImageB, await Read(KeyB));
    }

    [Fact]
    public async Task REQ_021_replacing_twice_in_a_row_keeps_only_the_latest_image()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);
        await SetCustomAvatar(id, KeyB, ImageB, previousKey: KeyA);

        await SetCustomAvatar(id, KeyC, ImageC, previousKey: KeyB);

        Assert.Equal(1, Rows());
        Assert.Equal(ImageC, await Read(KeyC));
    }

    [Fact]
    public async Task REQ_021_without_a_commit_the_previous_image_is_kept_and_the_new_one_is_not_stored()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            var storage = new PostgresAvatarStorage(context);
            var player = (await players.GetById(id))!;
            Assert.True(player.SetCustomAvatar(KeyB).IsSuccess);
            await storage.SaveAsync(KeyB, ImageB);
            await players.Save(player);
            await storage.RemoveAsync(KeyA);
        }

        Assert.Equal(1, Rows());
        Assert.Equal(ImageA, await Read(KeyA));
        Assert.Null(await Read(KeyB));
    }

    [Fact]
    public async Task REQ_021_a_failing_commit_rolls_back_the_removal_of_the_previous_image()
    {
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        await SetCustomAvatar(ana, KeyA, ImageA);
        await SetCustomAvatar(bia, KeyB, ImageB);

        // Ana tenta trocar para uma chave que ja pertence a Bia (PK duplicada): o commit falha e a remocao de KeyA
        // enfileirada na mesma transacao nao pode ser aplicada.
        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            var storage = new PostgresAvatarStorage(context);
            var player = (await players.GetById(ana))!;
            Assert.True(player.SetCustomAvatar(KeyB).IsSuccess);
            await storage.SaveAsync(KeyB, ImageC);
            await players.Save(player);
            await storage.RemoveAsync(KeyA);

            await Assert.ThrowsAnyAsync<Exception>(() => new EfUnitOfWork(context).CommitAsync());
        }

        Assert.Equal(2, Rows());
        Assert.Equal(ImageA, await Read(KeyA));
        Assert.Equal(ImageB, await Read(KeyB));
    }

    [Fact]
    public async Task REQ_021_remove_of_an_unknown_key_is_a_no_op()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        await using (var context = _db.CreateContext())
        {
            await new PostgresAvatarStorage(context).RemoveAsync("does-not-exist");
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(1, Rows());
        Assert.Equal(ImageA, await Read(KeyA));
    }

    [Fact]
    public async Task REQ_024_deleting_the_player_deletes_the_image_in_cascade_and_keeps_other_players_images()
    {
        _db.SeedFormats();
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        await SetCustomAvatar(ana, KeyA, ImageA);
        await SetCustomAvatar(bia, KeyB, ImageB);

        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            await players.Remove((await players.GetById(ana))!);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(1, Rows());
        Assert.Null(await Read(KeyA));
        Assert.Equal(ImageB, await Read(KeyB));
    }

    [Fact]
    public async Task REQ_024_deleting_a_player_with_the_image_loaded_in_the_same_context_also_removes_it()
    {
        _db.SeedFormats();
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            var storage = new PostgresAvatarStorage(context);
            Assert.Equal(ImageA, await storage.ReadAsync(KeyA));
            await players.Remove((await players.GetById(id))!);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(0, Rows());
    }

    // Contrato da porta IAvatarStorage (design.md): Save em chave existente SUBSTITUI a imagem (como o fake em memoria).
    private async Task SaveAgainOnSameKey(PlayerId id, string key, byte[] image, bool commit)
    {
        await using var context = _db.CreateContext();
        var players = new EfPlayerRepository(context);
        var storage = new PostgresAvatarStorage(context);
        var player = (await players.GetById(id))!; // rastreado, com CustomAvatar(key) ja gravado
        await storage.SaveAsync(key, image);
        if (commit)
        {
            await new EfUnitOfWork(context).CommitAsync();
        }
    }

    [Fact]
    public async Task REQ_019_saving_an_existing_key_replaces_the_image_after_commit()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        await SaveAgainOnSameKey(id, KeyA, ImageB, commit: true);

        Assert.Equal(ImageB, await Read(KeyA));
        Assert.Equal(1, Rows());
        Assert.Equal(1, Scalar<int>($"SELECT COUNT(*) FROM avatar_image WHERE storage_key = '{KeyA}'"));
    }

    [Fact]
    public async Task REQ_019_saving_an_existing_key_without_commit_keeps_the_old_image()
    {
        var id = await PersistPlayer();
        await SetCustomAvatar(id, KeyA, ImageA);

        await SaveAgainOnSameKey(id, KeyA, ImageB, commit: false);

        Assert.Equal(ImageA, await Read(KeyA));
        Assert.Equal(1, Rows());
    }

    // Decisao do usuario (T-061 REFACTOR): chave sem Jogador rastreado e erro de programa.
    [Fact]
    public async Task REQ_019_save_with_a_key_that_no_tracked_player_owns_throws_invalid_operation_and_writes_nothing()
    {
        await PersistPlayer();

        await using (var context = _db.CreateContext())
        {
            var storage = new PostgresAvatarStorage(context);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(KeyA, ImageA));
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(0, Rows());
        Assert.Null(await Read(KeyA));
    }

    // Decisao do usuario (T-061 REFACTOR): limite de 256 KB (Avatar:MaxBytes, padrao 262144) na borda do adapter.
    [Fact]
    public async Task REQ_019_save_of_an_image_above_the_default_limit_throws_argument_exception_and_writes_nothing()
    {
        var id = await PersistPlayer();
        var tooBig = new byte[262144 + 1];

        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            var player = (await players.GetById(id))!;
            Assert.True(player.SetCustomAvatar(KeyA).IsSuccess);
            var storage = new PostgresAvatarStorage(context);
            await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(KeyA, tooBig));
        }

        Assert.Equal(0, Rows());
    }

    [Fact]
    public async Task REQ_019_save_of_an_image_of_exactly_the_default_limit_is_accepted()
    {
        var id = await PersistPlayer();
        var atLimit = new byte[262144];

        await SetCustomAvatar(id, KeyA, atLimit);

        Assert.Equal(1, Rows());
        Assert.Equal(262144, (await Read(KeyA))!.Length);
    }

    [Fact]
    public async Task REQ_019_the_limit_comes_from_the_max_bytes_option_passed_to_the_constructor()
    {
        var id = await PersistPlayer();

        await using var context = _db.CreateContext();
        var players = new EfPlayerRepository(context);
        var player = (await players.GetById(id))!;
        Assert.True(player.SetCustomAvatar(KeyA).IsSuccess);
        var storage = new PostgresAvatarStorage(context, maxBytes: 10);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(KeyA, new byte[11]));
        await storage.SaveAsync(KeyA, new byte[10]);
        await players.Save(player);
        await new EfUnitOfWork(context).CommitAsync();

        Assert.Equal(1, Rows());
    }

    private void Execute(string sql)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
