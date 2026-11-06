using System.Globalization;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-056 (REQ-007, 014, 016, 036, 046). Tabelas: accounts.player_preferred_format e
// accounts.player_external_identity. A FK format_id -> deck_suggestion.format e entre schemas (ADR-0001) e nao
// existe no banco SQLite de teste; aqui so se verifica a FK para player.
// Contrato para o implementer (GREEN):
//   player_preferred_format(player_id, format_id) PK composta pk_player_preferred_format, FK cascade para player;
//   player_external_identity(player_id, provider, subject, linked_at) PK pk_player_external_identity,
//     indice unico ux_player_external_identity_provider_subject (provider, subject), FK cascade para player;
//   EfUnitOfWork.CommitAsync traduz a violacao em UniqueConstraintViolationException(nome do indice);
//   a traducao para google_not_completed e do caso de uso, nao deste adaptador.
//   EfPlayerRepository: GetById/FindByEmail/FindByGoogleSubject devolvem o Jogador com Formats e ExternalIdentity.
public sealed class PlayerRelationsPersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string IdentityIndex = "ux_player_external_identity_provider_subject";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly FormatId Commander = new("commander");
    private static readonly FormatId Modern = new("modern");
    private static readonly FormatId Pauper = new("pauper");
    private static readonly FormatId Legacy = new("legacy");

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player PasswordPlayer(string email = "ana@example.com") =>
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

    private static Player GooglePlayer(string email = "gabi@example.com", string subject = "sub-123") =>
        Player.RegisterWithGoogle(
            Email.Create(email).Value,
            ExternalIdentity.Create("google", subject).Value,
            DisplayName.Create("Gabi Lima").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private static Player WithFormats(Player player, params FormatId[] formats)
    {
        Assert.True(player.UpdateProfile("Ana Souza", null, "mtg", formats, formats).IsSuccess);
        return player;
    }

    // O construtor de PasswordProof e internal (so Application o enxerga): o teste o cria por reflexao.
    private static PasswordProof ProofFor(PlayerId id) =>
        (PasswordProof)Activator.CreateInstance(typeof(PasswordProof), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [id], null)!;

    private async Task<Player> Persist(Player player)
    {
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player;
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
    public async Task REQ_007_preferred_formats_are_stored_one_row_per_format_and_reloaded()
    {
        _db.SeedFormats();
        var saved = await Persist(WithFormats(PasswordPlayer(), Commander, Modern));

        await using var context = _db.CreateContext();
        var loaded = (await new EfPlayerRepository(context).GetById(saved.Id))!;

        Assert.Equal(["commander", "modern"], Column("SELECT format_id FROM player_preferred_format ORDER BY format_id"));
        Assert.Equal(saved.Formats, loaded.Formats);
    }

    [Fact]
    public async Task REQ_007_preferred_format_primary_key_is_composite_player_id_and_format_id()
    {
        _db.SeedFormats();
        await Persist(WithFormats(PasswordPlayer(), Commander));

        var pk = Column("SELECT name FROM pragma_table_info('player_preferred_format') WHERE pk > 0 ORDER BY pk");

        Assert.Equal(["player_id", "format_id"], pk);
    }

    [Fact]
    public async Task REQ_007_preferred_format_foreign_key_to_player_cascades_on_delete()
    {
        await Persist(PasswordPlayer());

        var action = Scalar<string>(
            "SELECT on_delete FROM pragma_foreign_key_list('player_preferred_format') WHERE \"table\" = 'player'");

        Assert.Equal("CASCADE", action);
    }

    [Fact]
    public async Task REQ_007_saving_the_profile_keeps_inactive_format_references()
    {
        _db.SeedFormats();
        var saved = await Persist(WithFormats(PasswordPlayer(), Commander, Legacy));
        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;

            // Legacy saiu do catalogo ativo: o salvamento troca Commander por Pauper e preserva Legacy (ADR-0007).
            Assert.True(player.UpdateProfile("Ana Souza", null, "mtg", [Pauper], [Commander, Pauper]).IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(["legacy", "pauper"], Column("SELECT format_id FROM player_preferred_format ORDER BY format_id"));
    }

    [Fact]
    public async Task REQ_007_clearing_active_formats_deletes_only_the_rows_that_were_removed()
    {
        _db.SeedFormats();
        var saved = await Persist(WithFormats(PasswordPlayer(), Commander, Modern));
        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;

            Assert.True(player.UpdateProfile("Ana Souza", null, "mtg", [Modern], [Commander, Modern]).IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(["modern"], Column("SELECT format_id FROM player_preferred_format"));
    }

    [Fact]
    public async Task REQ_014_google_signup_stores_the_external_identity_with_link_time()
    {
        var saved = await Persist(GooglePlayer());

        await using var context = _db.CreateContext();
        var loaded = (await new EfPlayerRepository(context).GetById(saved.Id))!;

        Assert.Equal(saved.ExternalIdentity, loaded.ExternalIdentity);
        Assert.Equal("google", Scalar<string>("SELECT provider FROM player_external_identity"));
        Assert.Equal("sub-123", Scalar<string>("SELECT subject FROM player_external_identity"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player_external_identity WHERE linked_at IS NOT NULL"));
    }

    [Fact]
    public async Task REQ_014_password_player_has_no_external_identity_row()
    {
        var saved = await Persist(PasswordPlayer());

        await using var context = _db.CreateContext();
        var loaded = (await new EfPlayerRepository(context).GetById(saved.Id))!;

        Assert.Null(loaded.ExternalIdentity);
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player_external_identity"));
    }

    [Fact]
    public async Task REQ_014_external_identity_primary_key_is_composite_player_id_and_provider()
    {
        await Persist(GooglePlayer());

        var pk = Column("SELECT name FROM pragma_table_info('player_external_identity') WHERE pk > 0 ORDER BY pk");

        Assert.Equal(["player_id", "provider"], pk);
    }

    [Fact]
    public async Task REQ_014_provider_and_subject_are_unique_through_the_named_index()
    {
        await Persist(GooglePlayer());

        var columns = Column(
            $"SELECT name FROM pragma_index_info('{IdentityIndex}') ORDER BY seqno");
        var unique = Scalar<int>(
            $"SELECT COUNT(*) FROM pragma_index_list('player_external_identity') WHERE name = '{IdentityIndex}' AND \"unique\" = 1");

        Assert.Equal(1, unique);
        Assert.Equal(["provider", "subject"], columns);
    }

    [Fact]
    public async Task REQ_014_same_subject_in_two_players_is_translated_to_unique_violation_without_ef_exception()
    {
        await Persist(GooglePlayer("gabi@example.com", "sub-123"));
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(GooglePlayer("outra@example.com", "sub-123"));

        var thrown = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());

        Assert.Equal(IdentityIndex, thrown.ConstraintName);
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player"));
    }

    [Fact]
    public async Task REQ_036_linking_google_to_an_existing_player_persists_the_identity()
    {
        var saved = await Persist(PasswordPlayer("ana@example.com"));
        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;
            var linked = player.LinkGoogle(
                ExternalIdentity.Create("google", "sub-link").Value, player.Email, ProofFor(player.Id));
            Assert.True(linked.IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal("sub-link", Scalar<string>("SELECT subject FROM player_external_identity"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player_external_identity WHERE linked_at IS NOT NULL"));
    }

    [Fact]
    public async Task REQ_036_find_by_google_subject_returns_the_owner_with_identity_and_formats()
    {
        _db.SeedFormats();
        var saved = await Persist(WithFormats(GooglePlayer(), Commander));

        await using var context = _db.CreateContext();
        var found = await new EfPlayerRepository(context).FindByGoogleSubject("sub-123");

        Assert.NotNull(found);
        Assert.Equal(saved.Id, found.Id);
        Assert.Equal("sub-123", found.ExternalIdentity!.Subject);
        Assert.Equal(saved.Formats, found.Formats);
    }

    [Fact]
    public async Task REQ_036_find_by_google_subject_returns_null_for_unknown_subject()
    {
        await Persist(GooglePlayer());

        await using var context = _db.CreateContext();

        Assert.Null(await new EfPlayerRepository(context).FindByGoogleSubject("sub-other"));
    }

    [Fact]
    public async Task REQ_016_find_by_email_returns_the_player_with_formats_and_identity()
    {
        _db.SeedFormats();
        var saved = await Persist(WithFormats(GooglePlayer("gabi@example.com"), Modern));
        await Persist(PasswordPlayer("ana@example.com"));

        await using var context = _db.CreateContext();
        var found = await new EfPlayerRepository(context).FindByEmail(Email.Create("gabi@example.com").Value);

        Assert.NotNull(found);
        Assert.Equal(saved.Id, found.Id);
        Assert.Equal(saved.Formats, found.Formats);
        Assert.Equal("sub-123", found.ExternalIdentity!.Subject);
    }

    [Fact]
    public async Task REQ_016_find_by_email_matches_the_normalized_address_and_returns_null_when_absent()
    {
        await Persist(PasswordPlayer("ana@example.com"));

        await using var context = _db.CreateContext();
        var repository = new EfPlayerRepository(context);

        Assert.NotNull(await repository.FindByEmail(Email.Create("  ANA@Example.com ").Value));
        Assert.Null(await repository.FindByEmail(Email.Create("ninguem@example.com").Value));
    }

    [Fact]
    public async Task REQ_016_exists_by_email_reflects_stored_players()
    {
        await Persist(PasswordPlayer("ana@example.com"));

        await using var context = _db.CreateContext();
        var repository = new EfPlayerRepository(context);

        Assert.True(await repository.ExistsByEmail(Email.Create("ana@example.com").Value));
        Assert.False(await repository.ExistsByEmail(Email.Create("ninguem@example.com").Value));
    }

    [Fact]
    public async Task REQ_046_removing_the_player_cascades_to_formats_and_external_identity()
    {
        _db.SeedFormats();
        var saved = await Persist(WithFormats(GooglePlayer(), Commander, Modern));
        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;

            await repository.Remove(player);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player_preferred_format"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player_external_identity"));
    }

    [Fact]
    public async Task REQ_046_foreign_keys_to_player_cascade_on_delete_in_the_schema()
    {
        await Persist(GooglePlayer());

        var action = Scalar<string>(
            "SELECT on_delete FROM pragma_foreign_key_list('player_external_identity') WHERE \"table\" = 'player'");

        Assert.Equal("CASCADE", action);
    }
}
