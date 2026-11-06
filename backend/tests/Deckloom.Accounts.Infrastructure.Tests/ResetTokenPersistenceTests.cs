using System.Globalization;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-058 (REQ-029, 030, 031). Tabela: accounts.password_reset_token; design.md (J8,
// Player.IssuePasswordReset/ResetPassword). Contrato para o implementer (GREEN):
//   Tabela password_reset_token (snake_case): player_id PK pk_password_reset_token (no maximo um token por Jogador),
//     token_hash (32 bytes, SHA-256) unico ux_password_reset_token_token_hash, issued_at, expires_at;
//     FK fk_password_reset_token_player -> player ON DELETE CASCADE. Sem token em claro.
//   EfPlayerRepository.FindByResetTokenHash(hash): devolve o Jogador (com Formats e o PasswordResetToken) ou null.
//     Nao filtra por expiracao: a validade e do agregado (ResetPassword, now >= ExpiresAt).
//   Emitir (Player.IssuePasswordReset + Save + Commit) substitui a linha do Jogador; consumir (Player.ResetPassword
//     + Save + Commit) apaga a linha na mesma transacao que troca o hash da senha.
//   Consumo unico e atomico: o commit que apaga um token ja consumido por outra transacao nao pode gravar a troca de
//     senha; sai como ConcurrencyConflictException (porta de IUnitOfWork), nunca como excecao do EF/Sqlite.
//     Depois do commit vencedor, FindByResetTokenHash devolve null e ResetPassword num Jogador recarregado falha com
//     reset_link_invalid.
//   EfUnitOfWork.CommitAsync traduz a violacao de ux_password_reset_token_token_hash em
//     UniqueConstraintViolationException("ux_password_reset_token_token_hash").
//   DeleteExpiredResetTokens fica para T-075 (job de limpeza); a linha RED da T-058 nao o cobre.
public sealed class ResetTokenPersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string TokenHashIndex = "ux_password_reset_token_token_hash";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string NewStored =
        "v1$pbkdf2-sha256$600000$b3V0cm9zYWx0b3V0cm8=$b3V0cm9oYXNob3V0cm9oYXNob3V0cm9oYXNob3V0cm8=";

    // Data fixa no passado distante: se o banco gravasse a propria data, o valor nao bateria com esta.
    private static readonly DateTimeOffset Now = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();

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

    private static PasswordHash NewPassword() => PasswordHash.Parse(NewStored).Value;

    private async Task<Player> PersistPlayer(string email = "ana@example.com")
    {
        var player = PasswordPlayer(email);
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player;
    }

    private async Task Issue(PlayerId id, string token, DateTimeOffset? at = null)
    {
        await using var context = _db.CreateContext();
        var repository = new EfPlayerRepository(context);
        var player = (await repository.GetById(id))!;
        Assert.True(player.IssuePasswordReset(Hasher.Hash(token), at ?? Now).IsSuccess);
        await repository.Save(player);
        await new EfUnitOfWork(context).CommitAsync();
    }

    private async Task<Player?> Find(string token)
    {
        await using var context = _db.CreateContext();
        return await new EfPlayerRepository(context).FindByResetTokenHash(Hasher.Hash(token));
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

    private byte[] StoredTokenHash()
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT token_hash FROM password_reset_token";
        return (byte[])command.ExecuteScalar()!;
    }

    [Fact]
    public async Task REQ_029_only_the_token_hash_is_stored_and_never_the_plain_token()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "plain-reset-token");

        Assert.Equal(Hasher.Hash("plain-reset-token").ToBytes(), StoredTokenHash());
        Assert.Equal(TokenHash.Length, StoredTokenHash().Length);
        Assert.Equal(
            ["expires_at", "issued_at", "player_id", "token_hash"],
            Column("SELECT name FROM pragma_table_info('password_reset_token') ORDER BY name"));
        Assert.Equal(
            0,
            Scalar<int>("SELECT COUNT(*) FROM password_reset_token WHERE CAST(token_hash AS TEXT) LIKE '%plain-reset-token%'"));
    }

    [Fact]
    public async Task REQ_029_find_by_reset_token_hash_returns_the_player_with_the_token()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        var found = await Find("reset-token");

        Assert.NotNull(found);
        Assert.Equal(player.Id, found.Id);
        Assert.Equal(Hasher.Hash("reset-token"), found.PasswordResetToken!.TokenHash);
        Assert.Equal(Now, found.PasswordResetToken.IssuedAt);
        Assert.Equal(Now.AddMinutes(60), found.PasswordResetToken.ExpiresAt);
    }

    [Fact]
    public async Task REQ_029_find_by_reset_token_hash_returns_null_for_an_unknown_token()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        Assert.Null(await Find("other-token"));
    }

    [Fact]
    public async Task REQ_029_find_by_reset_token_hash_does_not_filter_by_expiry_the_aggregate_decides()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        var found = (await Find("reset-token"))!;
        var late = found.ResetPassword(Hasher.Hash("reset-token"), NewPassword(), Now.AddMinutes(60));

        Assert.False(late.IsSuccess);
        Assert.Equal(Player.ResetLinkInvalidCode, late.Error.Code);
    }

    [Fact]
    public async Task REQ_029_issuing_a_new_token_replaces_the_previous_one_for_the_same_player()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "first-token");
        await Issue(player.Id, "second-token", Now.AddMinutes(5));

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM password_reset_token"));
        Assert.Equal(Hasher.Hash("second-token").ToBytes(), StoredTokenHash());
        Assert.Null(await Find("first-token"));
        var found = await Find("second-token");
        Assert.Equal(player.Id, found!.Id);
        Assert.Equal(Now.AddMinutes(65), found.PasswordResetToken!.ExpiresAt);
    }

    [Fact]
    public async Task REQ_029_primary_key_is_player_id_so_there_is_at_most_one_token_per_player()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        Assert.Equal(
            ["player_id"],
            Column("SELECT name FROM pragma_table_info('password_reset_token') WHERE pk > 0 ORDER BY pk"));
    }

    [Fact]
    public async Task REQ_029_token_hash_has_a_unique_index_named_ux_password_reset_token_token_hash()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        Assert.Contains(TokenHashIndex, Column("SELECT name FROM pragma_index_list('password_reset_token') WHERE \"unique\" = 1"));
        Assert.Equal(["token_hash"], Column($"SELECT name FROM pragma_index_info('{TokenHashIndex}')"));
    }

    [Fact]
    public async Task REQ_029_the_same_token_hash_for_two_players_fails_at_commit_as_a_unique_violation()
    {
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        await Issue(ana.Id, "shared-token");

        await using var context = _db.CreateContext();
        var repository = new EfPlayerRepository(context);
        var other = (await repository.GetById(bia.Id))!;
        Assert.True(other.IssuePasswordReset(Hasher.Hash("shared-token"), Now).IsSuccess);
        await repository.Save(other);

        var error = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());
        Assert.Equal(TokenHashIndex, error.ConstraintName);
    }

    [Fact]
    public async Task REQ_030_successful_reset_consumes_the_token_and_changes_the_password_hash_in_one_commit()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var found = (await repository.FindByResetTokenHash(Hasher.Hash("reset-token")))!;
            Assert.True(found.ResetPassword(Hasher.Hash("reset-token"), NewPassword(), Now.AddMinutes(10)).IsSuccess);
            await repository.Save(found);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM password_reset_token"));
        Assert.Equal(NewStored, Scalar<string>("SELECT password_hash FROM player"));
        Assert.Null(await Find("reset-token"));
    }

    [Fact]
    public async Task REQ_030_second_attempt_after_consumption_fails_with_a_domain_error_and_no_database_exception()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");
        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var found = (await repository.FindByResetTokenHash(Hasher.Hash("reset-token")))!;
            Assert.True(found.ResetPassword(Hasher.Hash("reset-token"), NewPassword(), Now.AddMinutes(10)).IsSuccess);
            await repository.Save(found);
            await new EfUnitOfWork(context).CommitAsync();
        }

        await using var second = _db.CreateContext();
        var reloaded = (await new EfPlayerRepository(second).GetById(player.Id))!;
        var again = reloaded.ResetPassword(Hasher.Hash("reset-token"), NewPassword(), Now.AddMinutes(11));

        Assert.Null(reloaded.PasswordResetToken);
        Assert.False(again.IsSuccess);
        Assert.Equal(Player.ResetLinkInvalidCode, again.Error.Code);
    }

    [Fact]
    public async Task REQ_030_two_concurrent_consumptions_only_one_wins_and_the_loser_gets_a_concurrency_conflict()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        await using var winnerContext = _db.CreateContext();
        await using var loserContext = _db.CreateContext();
        var winnerRepository = new EfPlayerRepository(winnerContext);
        var loserRepository = new EfPlayerRepository(loserContext);
        var winner = (await winnerRepository.FindByResetTokenHash(Hasher.Hash("reset-token")))!;
        var loser = (await loserRepository.FindByResetTokenHash(Hasher.Hash("reset-token")))!;

        Assert.True(winner.ResetPassword(Hasher.Hash("reset-token"), NewPassword(), Now.AddMinutes(10)).IsSuccess);
        Assert.True(loser.ResetPassword(Hasher.Hash("reset-token"), NewPassword(), Now.AddMinutes(10)).IsSuccess);
        await winnerRepository.Save(winner);
        await loserRepository.Save(loser);

        await new EfUnitOfWork(winnerContext).CommitAsync();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(loserContext).CommitAsync());

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM password_reset_token"));
        Assert.Equal(NewStored, Scalar<string>("SELECT password_hash FROM player"));
    }

    [Fact]
    public async Task REQ_029_clearing_an_expired_token_through_the_aggregate_deletes_only_the_token_row()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var found = (await repository.FindByResetTokenHash(Hasher.Hash("reset-token")))!;
            Assert.True(found.ClearExpiredResetToken(Now.AddMinutes(60)));
            await repository.Save(found);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM password_reset_token"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player"));
    }

    [Fact]
    public async Task REQ_029_foreign_key_to_player_cascades_on_delete_in_the_schema()
    {
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        var action = Scalar<string>(
            "SELECT on_delete FROM pragma_foreign_key_list('password_reset_token') WHERE \"table\" = 'player'");

        Assert.Equal("CASCADE", action);
    }

    [Fact]
    public async Task REQ_029_removing_the_player_deletes_the_reset_token_row()
    {
        _db.SeedFormats();
        var player = await PersistPlayer();
        await Issue(player.Id, "reset-token");

        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var found = (await repository.GetById(player.Id))!;
            await repository.Remove(found);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM password_reset_token"));
    }
}
