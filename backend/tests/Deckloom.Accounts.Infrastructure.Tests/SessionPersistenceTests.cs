using System.Globalization;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-057 (REQ-010, 012, 030, 035). Tabela: Database/tables/accounts.player_session.md; design.md (Session, S1-S6,
// ISessionRepository). Contrato para o implementer (GREEN):
//   sealed class EfSessionRepository : ISessionRepository    // ctor(AccountsDbContext), namespace Deckloom.Accounts.Infrastructure
//     Add/Save so ENFILEIRAM no contexto; nada e gravado ate EfUnitOfWork.CommitAsync (como EfPlayerRepository).
//     RevokeAllFor(playerId, reason, now): devolve na hora a contagem das sessoes do Jogador ainda nao revogadas
//       (expiradas inclusive), enfileira a revogacao (Session.Revoke(reason, now)) e so o commit a aplica;
//       descartar o contexto sem commit nao revoga nada. Nunca toca sessoes de outro Jogador.
//     FindByTokenHash/FindById devolvem a sessao mesmo revogada ou expirada (a validade e do agregado, IsValid(now)).
//   Tabela player_session (snake_case): id uuid PK pk_player_session, token_hash (32 bytes, SHA-256) unico
//     ux_player_session_token_hash, player_id FK fk_player_session_player ON DELETE CASCADE (indice
//     ix_player_session_player_id), remember_me, created_at, last_seen_at, expires_at (indice
//     ix_player_session_expires_at), revoked_at, reauthenticated_at. Sem IP, sem user agent, sem token em claro.
//   "Agora" sempre chega por parametro (TimeProvider no caso de uso): nenhuma funcao de data do banco em
//     CHECK, default ou consulta.
//   EfUnitOfWork.CommitAsync traduz a violacao de ux_player_session_token_hash em
//     UniqueConstraintViolationException("ux_player_session_token_hash").
public sealed class SessionPersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string TokenHashIndex = "ux_player_session_token_hash";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    // Data fixa no passado distante: se o banco gravasse a propria data, o valor nao bateria com esta.
    private static readonly DateTimeOffset Now = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player PasswordPlayer(string email) =>
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

    private static Session StartSession(Player player, string token, bool rememberMe = false, DateTimeOffset? at = null) =>
        Session.Start(player.Id, Hasher.Hash(token), rememberMe, at ?? Now);

    private async Task<Player> PersistPlayer(string email = "ana@example.com")
    {
        var player = PasswordPlayer(email);
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player;
    }

    private async Task<Session> PersistSession(Session session)
    {
        await using var context = _db.CreateContext();
        await new EfSessionRepository(context).Add(session);
        await new EfUnitOfWork(context).CommitAsync();
        return session;
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
        command.CommandText = "SELECT token_hash FROM player_session";
        return (byte[])command.ExecuteScalar()!;
    }

    private async Task<Session?> Reload(string token)
    {
        await using var context = _db.CreateContext();
        return await new EfSessionRepository(context).FindByTokenHash(Hasher.Hash(token));
    }

    [Fact]
    public async Task REQ_030_session_stores_only_the_token_hash_and_never_the_plain_token()
    {
        var player = await PersistPlayer();
        var session = await PersistSession(StartSession(player, "plain-token-value"));

        Assert.Equal(session.TokenHash.ToBytes(), StoredTokenHash());
        Assert.Equal(TokenHash.Length, StoredTokenHash().Length);
        Assert.Equal(
            ["created_at", "expires_at", "id", "last_seen_at", "player_id", "reauthenticated_at", "remember_me", "revoked_at", "token_hash"],
            Column("SELECT name FROM pragma_table_info('player_session') ORDER BY name"));
        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player_session WHERE CAST(token_hash AS TEXT) LIKE '%plain-token-value%'"));
    }

    [Fact]
    public async Task REQ_030_token_hash_has_a_unique_index_named_ux_player_session_token_hash()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "token-a"));

        var unique = Column("SELECT name FROM pragma_index_list('player_session') WHERE \"unique\" = 1");

        Assert.Contains(TokenHashIndex, unique);
        Assert.Equal(["token_hash"], Column($"SELECT name FROM pragma_index_info('{TokenHashIndex}')"));
    }

    [Fact]
    public async Task REQ_030_duplicate_token_hash_fails_at_commit_as_a_unique_constraint_violation()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "same-token"));

        await using var context = _db.CreateContext();
        await new EfSessionRepository(context).Add(StartSession(player, "same-token"));

        var error = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());
        Assert.Equal(TokenHashIndex, error.ConstraintName);
    }

    [Fact]
    public async Task REQ_010_add_only_queues_the_session_until_the_commit()
    {
        var player = await PersistPlayer();

        await using (var context = _db.CreateContext())
        {
            await new EfSessionRepository(context).Add(StartSession(player, "queued-token"));
            Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player_session"));
        }

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player_session"));
    }

    [Fact]
    public async Task REQ_010_find_by_token_hash_returns_the_stored_session_with_every_attribute()
    {
        var player = await PersistPlayer();
        var saved = await PersistSession(StartSession(player, "token-a", rememberMe: true));

        var loaded = (await Reload("token-a"))!;

        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.TokenHash, loaded.TokenHash);
        Assert.Equal(player.Id, loaded.PlayerId);
        Assert.True(loaded.RememberMe);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(Now, loaded.LastSeenAt);
        Assert.Equal(Now.AddDays(30), loaded.ExpiresAt);
        Assert.Null(loaded.RevokedAt);
        Assert.Null(loaded.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ_010_find_by_token_hash_returns_null_for_an_unknown_token()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "token-a"));

        Assert.Null(await Reload("another-token"));
    }

    [Fact]
    public async Task REQ_012_find_by_token_hash_returns_expired_and_revoked_sessions_too()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "expired-token"));
        var revoked = StartSession(player, "revoked-token", rememberMe: true);
        revoked.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(1));
        await PersistSession(revoked);

        var expired = (await Reload("expired-token"))!;
        var gone = (await Reload("revoked-token"))!;

        Assert.False(expired.IsValid(Now.AddHours(1)));
        Assert.Equal(Now.AddMinutes(1), gone.RevokedAt);
        Assert.False(gone.IsValid(Now.AddMinutes(2)));
    }

    [Fact]
    public async Task REQ_012_find_by_id_returns_the_stored_session()
    {
        var player = await PersistPlayer();
        var saved = await PersistSession(StartSession(player, "token-a"));

        await using var context = _db.CreateContext();
        var loaded = await new EfSessionRepository(context).FindById(saved.Id);

        Assert.Equal(saved.TokenHash, loaded!.TokenHash);
    }

    [Fact]
    public async Task REQ_012_save_persists_the_sliding_renewal_of_a_session_without_remember_me()
    {
        var player = await PersistPlayer();
        var saved = await PersistSession(StartSession(player, "token-a"));
        var later = Now.AddMinutes(10);

        await using (var context = _db.CreateContext())
        {
            var repository = new EfSessionRepository(context);
            var session = (await repository.FindByTokenHash(saved.TokenHash))!;
            Assert.True(session.Touch(later));
            await repository.Save(session);
            await new EfUnitOfWork(context).CommitAsync();
        }

        var loaded = (await Reload("token-a"))!;
        Assert.Equal(later, loaded.LastSeenAt);
        Assert.Equal(later.AddMinutes(30), loaded.ExpiresAt);
        Assert.Equal(Now, loaded.CreatedAt);
    }

    [Fact]
    public async Task REQ_030_save_persists_the_revocation_of_a_single_session()
    {
        var player = await PersistPlayer();
        var first = await PersistSession(StartSession(player, "token-a"));
        await PersistSession(StartSession(player, "token-b"));

        await using (var context = _db.CreateContext())
        {
            var repository = new EfSessionRepository(context);
            var session = (await repository.FindByTokenHash(first.TokenHash))!;
            session.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(5));
            await repository.Save(session);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(Now.AddMinutes(5), (await Reload("token-a"))!.RevokedAt);
        Assert.Null((await Reload("token-b"))!.RevokedAt);
    }

    [Fact]
    public async Task REQ_030_revoke_all_for_marks_every_session_of_the_player_and_none_of_another_at_the_commit()
    {
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        await PersistSession(StartSession(ana, "ana-1"));
        await PersistSession(StartSession(ana, "ana-2", rememberMe: true));
        await PersistSession(StartSession(bia, "bia-1"));

        await using (var context = _db.CreateContext())
        {
            var count = await new EfSessionRepository(context)
                .RevokeAllFor(ana.Id, SessionRevocationReason.PasswordReset, Now.AddMinutes(3));
            Assert.Equal(2, count);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(Now.AddMinutes(3), (await Reload("ana-1"))!.RevokedAt);
        Assert.Equal(Now.AddMinutes(3), (await Reload("ana-2"))!.RevokedAt);
        Assert.Null((await Reload("bia-1"))!.RevokedAt);
    }

    [Fact]
    public async Task REQ_030_revoke_all_for_only_queues_and_discarding_the_context_revokes_nothing()
    {
        var ana = await PersistPlayer();
        await PersistSession(StartSession(ana, "ana-1"));

        await using (var context = _db.CreateContext())
        {
            var count = await new EfSessionRepository(context)
                .RevokeAllFor(ana.Id, SessionRevocationReason.AccountDeletion, Now.AddMinutes(3));
            Assert.Equal(1, count);
            Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM player_session WHERE revoked_at IS NOT NULL"));
        }

        Assert.Null((await Reload("ana-1"))!.RevokedAt);
    }

    [Fact]
    public async Task REQ_030_revoke_all_for_keeps_the_first_revocation_and_counts_only_unrevoked_sessions_expired_included()
    {
        var ana = await PersistPlayer();
        var alreadyRevoked = StartSession(ana, "ana-revoked");
        alreadyRevoked.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(1));
        await PersistSession(alreadyRevoked);
        await PersistSession(StartSession(ana, "ana-expired"));

        await using (var context = _db.CreateContext())
        {
            var count = await new EfSessionRepository(context)
                .RevokeAllFor(ana.Id, SessionRevocationReason.PasswordReset, Now.AddDays(2));
            Assert.Equal(1, count);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(Now.AddMinutes(1), (await Reload("ana-revoked"))!.RevokedAt);
        Assert.Equal(Now.AddDays(2), (await Reload("ana-expired"))!.RevokedAt);
    }

    [Fact]
    public async Task REQ_030_revoke_all_for_a_player_without_sessions_returns_zero()
    {
        var ana = await PersistPlayer();

        await using var context = _db.CreateContext();
        var count = await new EfSessionRepository(context)
            .RevokeAllFor(ana.Id, SessionRevocationReason.Logout, Now);
        await new EfUnitOfWork(context).CommitAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task REQ_035_remember_me_flag_and_expiry_are_stored_per_session()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "short"));
        await PersistSession(StartSession(player, "long", rememberMe: true));

        var short1 = (await Reload("short"))!;
        var long1 = (await Reload("long"))!;

        Assert.False(short1.RememberMe);
        Assert.Equal(Now.AddMinutes(30), short1.ExpiresAt);
        Assert.True(long1.RememberMe);
        Assert.Equal(Now.AddDays(30), long1.ExpiresAt);
    }

    [Fact]
    public async Task REQ_030_player_foreign_key_cascades_on_delete()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "token-a"));

        var action = Scalar<string>(
            "SELECT on_delete FROM pragma_foreign_key_list('player_session') WHERE \"table\" = 'player'");

        Assert.Equal("CASCADE", action);
    }

    [Fact]
    public async Task REQ_030_deleting_the_player_removes_its_sessions_and_keeps_those_of_another_player()
    {
        _db.SeedFormats();
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        await PersistSession(StartSession(ana, "ana-1"));
        await PersistSession(StartSession(ana, "ana-2"));
        await PersistSession(StartSession(bia, "bia-1"));

        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            await players.Remove((await players.GetById(ana.Id))!);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player_session"));
        Assert.Null(await Reload("ana-1"));
        Assert.Null(await Reload("ana-2"));
        Assert.NotNull(await Reload("bia-1"));
    }

    [Fact]
    public async Task REQ_030_player_id_and_expires_at_have_the_documented_indexes()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "token-a"));

        Assert.Equal(["player_id"], Column("SELECT name FROM pragma_index_info('ix_player_session_player_id')"));
        Assert.Equal(["expires_at"], Column("SELECT name FROM pragma_index_info('ix_player_session_expires_at')"));
    }

    [Fact]
    public async Task REQ_012_the_clock_comes_from_the_parameter_and_the_schema_has_no_database_date_function()
    {
        var player = await PersistPlayer();
        await PersistSession(StartSession(player, "token-a"));

        var ddl = string.Join('\n', Column("SELECT sql FROM sqlite_master WHERE tbl_name = 'player_session' AND sql IS NOT NULL"));

        Assert.NotEmpty(ddl);
        Assert.DoesNotContain("datetime(", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current_timestamp", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current_date", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("strftime(", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Now, (await Reload("token-a"))!.CreatedAt);
    }
}
