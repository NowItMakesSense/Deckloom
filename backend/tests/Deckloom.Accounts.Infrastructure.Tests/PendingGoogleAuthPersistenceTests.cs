using System.Globalization;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.Data.Sqlite;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-059 (REQ-008, 036, 039). Tabela: accounts.pending_google_auth; design.md ("Estado transitorio
// da aplicacao (IPendingGoogleAuthStore)", J7). Contrato para o implementer (GREEN):
//   sealed class EfPendingGoogleAuthStore : IPendingGoogleAuthStore   // namespace Deckloom.Accounts.Infrastructure
//     ctor(AccountsDbContext context, TimeProvider clock)             // "agora" so vem do TimeProvider
//     AddAsync: so ENFILEIRA no contexto (como EfPlayerRepository/EfSessionRepository); nada e gravado ate
//       EfUnitOfWork.CommitAsync. Os casos de uso chamam AddAsync dentro de IUnitOfWork.ExecuteAsync (T-063).
//     FindAsync(hash): le sem consumir; nulo se desconhecida ou expirada (agora >= ExpiresAt), como o
//       InMemoryPendingGoogleAuthStore. Devolve PendingGoogleSignup ou PendingGoogleLink conforme `kind`.
//     ConsumeAsync(hash): claim atomico e IMEDIATO (um unico DELETE condicional, nao ler-e-depois-apagar), como o
//       fake: o pendente some para os concorrentes na hora, sem esperar o commit; o rollback da transacao do caso de
//       uso o restaura. Devolve a pendencia para quem apagou a linha; nulo para desconhecida, ja consumida
//       (perdedor) ou expirada. O contrato de ConsumeAsync (T-042) ja fixa nulo para o perdedor: nunca lanca
//       ConcurrencyConflictException nem excecao do EF/Sqlite.
//     DeleteExpiredAsync: apaga as linhas expiradas (agora >= ExpiresAt) e devolve a contagem.
//   Tabela pending_google_auth (snake_case): id uuid PK pk_pending_google_auth, token_hash (32 bytes, SHA-256)
//     unico ux_pending_google_auth_token_hash, kind text ('signup'|'link'), google_subject, email,
//     suggested_display_name (nulo), target_player_id (nulo; FK fk_pending_google_auth_target_player -> player
//     ON DELETE CASCADE), created_at, expires_at (indice ix_pending_google_auth_expires_at); CHECKs de kind e
//     ck_pending_google_auth_kind_target. Sem token em claro; nenhuma funcao de data do banco.
//   EfUnitOfWork.CommitAsync traduz a violacao de ux_pending_google_auth_token_hash em
//     UniqueConstraintViolationException("ux_pending_google_auth_token_hash").
public sealed class PendingGoogleAuthPersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string TokenHashIndex = "ux_pending_google_auth_token_hash";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    // Data fixa no passado distante: se o banco gravasse a propria data, o valor nao bateria com esta.
    private static readonly DateTimeOffset Now = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();

    private readonly SqliteInMemoryFixture _db = new();
    private readonly MutableClock _clock = new(Now);

    public void Dispose() => _db.Dispose();

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Current;
    }

    private EfPendingGoogleAuthStore Store(AccountsDbContext context) => new(context, _clock);

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

    private static PendingGoogleSignup Signup(string token, DateTimeOffset? at = null, string? name = "Gui Google") =>
        PendingGoogleSignup.Create(Hasher.Hash(token), "sub-" + token, Email.Create("gui@example.com").Value, name, at ?? Now);

    private static PendingGoogleLink Link(string token, PlayerId target, DateTimeOffset? at = null) =>
        PendingGoogleLink.Create(Hasher.Hash(token), "sub-" + token, Email.Create("ana@example.com").Value, target, at ?? Now);

    private async Task<Player> PersistPlayer(string email = "ana@example.com")
    {
        var player = PasswordPlayer(email);
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player;
    }

    private async Task<T> Persist<T>(T pending)
        where T : PendingGoogleAuth
    {
        await using var context = _db.CreateContext();
        await Store(context).AddAsync(pending);
        await new EfUnitOfWork(context).CommitAsync();
        return pending;
    }

    private async Task<PendingGoogleAuth?> Find(string token)
    {
        await using var context = _db.CreateContext();
        return await Store(context).FindAsync(Hasher.Hash(token));
    }

    private async Task<PendingGoogleAuth?> Consume(string token)
    {
        await using var context = _db.CreateContext();
        return await Store(context).ConsumeAsync(Hasher.Hash(token));
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
        command.CommandText = "SELECT token_hash FROM pending_google_auth";
        return (byte[])command.ExecuteScalar()!;
    }

    private int Rows() => Scalar<int>("SELECT COUNT(*) FROM pending_google_auth");

    [Fact]
    public async Task REQ_039_only_the_token_hash_is_stored_and_never_the_plain_token()
    {
        var pending = await Persist(Signup("plain-pending-token"));

        Assert.Equal(pending.TokenHash.ToBytes(), StoredTokenHash());
        Assert.Equal(TokenHash.Length, StoredTokenHash().Length);
        Assert.Equal(
            [
                "created_at", "email", "expires_at", "google_subject", "id", "kind", "suggested_display_name",
                "target_player_id", "token_hash",
            ],
            Column("SELECT name FROM pragma_table_info('pending_google_auth') ORDER BY name"));
        Assert.Equal(
            0,
            Scalar<int>("SELECT COUNT(*) FROM pending_google_auth WHERE CAST(token_hash AS TEXT) LIKE '%plain-pending-token%'"));
    }

    [Fact]
    public async Task REQ_039_token_hash_has_a_unique_index_named_ux_pending_google_auth_token_hash()
    {
        await Persist(Signup("token-a"));

        Assert.Contains(TokenHashIndex, Column("SELECT name FROM pragma_index_list('pending_google_auth') WHERE \"unique\" = 1"));
        Assert.Equal(["token_hash"], Column($"SELECT name FROM pragma_index_info('{TokenHashIndex}')"));
    }

    [Fact]
    public async Task REQ_039_duplicate_token_hash_fails_at_commit_as_a_unique_constraint_violation()
    {
        await Persist(Signup("same-token"));

        await using var context = _db.CreateContext();
        await Store(context).AddAsync(Signup("same-token"));

        var error = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());
        Assert.Equal(TokenHashIndex, error.ConstraintName);
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task REQ_039_add_only_queues_the_pending_until_the_commit()
    {
        await using (var context = _db.CreateContext())
        {
            await Store(context).AddAsync(Signup("queued-token"));
            Assert.Equal(0, Rows());
        }

        Assert.Equal(0, Rows());
    }

    [Fact]
    public async Task REQ_039_signup_is_stored_with_kind_signup_no_target_and_the_suggested_name()
    {
        var saved = await Persist(Signup("token-a", name: "Gui Google"));

        var loaded = Assert.IsType<PendingGoogleSignup>(await Find("token-a"));

        Assert.Equal("signup", Scalar<string>("SELECT kind FROM pending_google_auth"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM pending_google_auth WHERE target_player_id IS NULL"));
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.TokenHash, loaded.TokenHash);
        Assert.Equal("sub-token-a", loaded.GoogleSubject);
        Assert.Equal(Email.Create("gui@example.com").Value, loaded.Email);
        Assert.Equal("Gui Google", loaded.SuggestedDisplayName);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(Now.AddMinutes(10), loaded.ExpiresAt);
    }

    [Fact]
    public async Task REQ_039_signup_without_a_google_name_keeps_the_suggested_name_null()
    {
        await Persist(Signup("token-a", name: null));

        var loaded = Assert.IsType<PendingGoogleSignup>(await Find("token-a"));

        Assert.Null(loaded.SuggestedDisplayName);
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM pending_google_auth WHERE suggested_display_name IS NULL"));
    }

    [Fact]
    public async Task REQ_036_link_is_stored_with_kind_link_and_the_target_player()
    {
        var player = await PersistPlayer();
        var saved = await Persist(Link("token-a", player.Id));

        var loaded = Assert.IsType<PendingGoogleLink>(await Find("token-a"));

        Assert.Equal("link", Scalar<string>("SELECT kind FROM pending_google_auth"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM pending_google_auth WHERE suggested_display_name IS NULL"));
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.TokenHash, loaded.TokenHash);
        Assert.Equal(player.Id, loaded.TargetPlayerId);
        Assert.Equal("sub-token-a", loaded.GoogleSubject);
        Assert.Equal(Email.Create("ana@example.com").Value, loaded.Email);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(Now.AddMinutes(10), loaded.ExpiresAt);
    }

    [Fact]
    public async Task REQ_036_the_database_rejects_an_unknown_kind_and_a_kind_target_mismatch()
    {
        var player = await PersistPlayer();
        await Persist(Signup("token-a"));

        Assert.Throws<SqliteException>(() => Insert("x1", "other", null));
        Assert.Throws<SqliteException>(() => Insert("x2", "link", null));
        Assert.Throws<SqliteException>(() => Insert("x3", "signup", player.Id.Value));
        Assert.Equal(1, Rows());
    }

    private void Insert(string seed, string kind, Guid? target)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText =
            "INSERT INTO pending_google_auth (id, token_hash, kind, google_subject, email, target_player_id, created_at, expires_at) " +
            "VALUES ($id, $hash, $kind, 's', 'e@example.com', $target, '2020-03-04 12:00:00', '2020-03-04 12:10:00')";
        command.Parameters.AddWithValue("$id", Guid.NewGuid());
        command.Parameters.AddWithValue("$hash", Hasher.Hash(seed).ToBytes());
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$target", target is null ? DBNull.Value : target.Value);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task REQ_036_target_player_foreign_key_cascades_on_delete_in_the_schema()
    {
        var player = await PersistPlayer();
        await Persist(Link("token-a", player.Id));

        var action = Scalar<string>(
            "SELECT on_delete FROM pragma_foreign_key_list('pending_google_auth') WHERE \"table\" = 'player'");

        Assert.Equal("CASCADE", action);
    }

    [Fact]
    public async Task REQ_036_deleting_the_target_player_removes_its_link_and_keeps_other_pendings()
    {
        _db.SeedFormats();
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        await Persist(Link("ana-link", ana.Id));
        await Persist(Link("bia-link", bia.Id));
        await Persist(Signup("signup"));

        await using (var context = _db.CreateContext())
        {
            var players = new EfPlayerRepository(context);
            await players.Remove((await players.GetById(ana.Id))!);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(2, Rows());
        Assert.Null(await Find("ana-link"));
        Assert.NotNull(await Find("bia-link"));
        Assert.NotNull(await Find("signup"));
    }

    [Fact]
    public async Task REQ_039_expires_at_has_the_documented_index()
    {
        await Persist(Signup("token-a"));

        Assert.Equal(["expires_at"], Column("SELECT name FROM pragma_index_info('ix_pending_google_auth_expires_at')"));
    }

    [Fact]
    public async Task REQ_039_find_returns_null_for_an_unknown_token()
    {
        await Persist(Signup("token-a"));

        Assert.Null(await Find("another-token"));
    }

    [Fact]
    public async Task REQ_039_find_reads_without_consuming()
    {
        await Persist(Signup("token-a"));

        Assert.NotNull(await Find("token-a"));
        Assert.NotNull(await Find("token-a"));
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task REQ_039_find_returns_null_once_the_ten_minutes_pass_using_the_time_provider()
    {
        await Persist(Signup("token-a"));

        _clock.Current = Now.AddMinutes(10).AddTicks(-1);
        Assert.NotNull(await Find("token-a"));

        _clock.Current = Now.AddMinutes(10);
        Assert.Null(await Find("token-a"));
    }

    [Fact]
    public async Task REQ_039_the_clock_comes_from_the_time_provider_and_the_schema_has_no_database_date_function()
    {
        await Persist(Signup("token-a"));

        var ddl = string.Join('\n', Column("SELECT sql FROM sqlite_master WHERE tbl_name = 'pending_google_auth' AND sql IS NOT NULL"));

        Assert.NotEmpty(ddl);
        Assert.DoesNotContain("datetime(", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current_timestamp", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current_date", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("strftime(", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ_039_consume_returns_the_pending_and_removes_it_at_once_without_a_commit()
    {
        var saved = await Persist(Signup("token-a"));

        var consumed = Assert.IsType<PendingGoogleSignup>(await Consume("token-a"));

        Assert.Equal(saved.Id, consumed.Id);
        Assert.Equal(saved.TokenHash, consumed.TokenHash);
        Assert.Equal(0, Rows());
        Assert.Null(await Find("token-a"));
    }

    [Fact]
    public async Task REQ_036_consume_of_a_link_returns_the_link_with_its_target_player()
    {
        var player = await PersistPlayer();
        await Persist(Link("token-a", player.Id));

        var consumed = Assert.IsType<PendingGoogleLink>(await Consume("token-a"));

        Assert.Equal(player.Id, consumed.TargetPlayerId);
        Assert.Equal(0, Rows());
    }

    [Fact]
    public async Task REQ_039_second_consumption_returns_null_and_no_database_exception()
    {
        await Persist(Signup("token-a"));

        Assert.NotNull(await Consume("token-a"));
        Assert.Null(await Consume("token-a"));
    }

    [Fact]
    public async Task REQ_039_two_consumers_with_their_own_contexts_only_one_claims_the_pending()
    {
        await Persist(Signup("token-a"));

        await using var winnerContext = _db.CreateContext();
        await using var loserContext = _db.CreateContext();
        var winner = await Store(winnerContext).ConsumeAsync(Hasher.Hash("token-a"));
        var loser = await Store(loserContext).ConsumeAsync(Hasher.Hash("token-a"));

        Assert.NotNull(winner);
        Assert.Null(loser);
        Assert.Equal(0, Rows());
    }

    [Fact]
    public async Task REQ_039_consume_leaves_other_pendings_untouched()
    {
        await Persist(Signup("token-a"));
        await Persist(Signup("token-b"));

        await Consume("token-a");

        Assert.Equal(1, Rows());
        Assert.NotNull(await Find("token-b"));
    }

    [Fact]
    public async Task REQ_039_consume_returns_null_for_an_unknown_token()
    {
        await Persist(Signup("token-a"));

        Assert.Null(await Consume("another-token"));
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task REQ_039_an_expired_pending_is_not_consumable()
    {
        await Persist(Signup("token-a"));
        _clock.Current = Now.AddMinutes(10);

        Assert.Null(await Consume("token-a"));
    }

    [Fact]
    public async Task REQ_039_delete_expired_removes_only_the_expired_rows_and_returns_the_count()
    {
        await Persist(Signup("old-1", Now.AddMinutes(-30)));
        await Persist(Signup("old-2", Now.AddMinutes(-11)));
        await Persist(Signup("fresh", Now));

        int deleted;
        await using (var context = _db.CreateContext())
        {
            deleted = await Store(context).DeleteExpiredAsync(1000);
        }

        Assert.Equal(2, deleted);
        Assert.Equal(1, Rows());
        Assert.NotNull(await Find("fresh"));
    }

    [Fact]
    public async Task REQ_036_consume_is_undone_when_the_unit_of_work_is_rolled_back()
    {
        await Persist(Signup("token-a"));
        var failure = new InvalidOperationException("boom");

        await using (var context = _db.CreateContext())
        {
            var unitOfWork = new EfUnitOfWork(context);
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteAsync(async ct =>
            {
                Assert.NotNull(await Store(context).ConsumeAsync(Hasher.Hash("token-a"), ct));
                throw failure; // o caso de uso falha depois de consumir
            }));
            Assert.Same(failure, thrown);
        }

        Assert.Equal(1, Rows());
        Assert.NotNull(await Find("token-a"));
    }

    [Fact]
    public async Task REQ_036_consume_is_applied_when_the_unit_of_work_commits()
    {
        await Persist(Signup("token-a"));

        await using (var context = _db.CreateContext())
        {
            var unitOfWork = new EfUnitOfWork(context);
            await unitOfWork.ExecuteAsync(async ct =>
            {
                Assert.NotNull(await Store(context).ConsumeAsync(Hasher.Hash("token-a"), ct));
                await unitOfWork.CommitAsync(ct);
            });
        }

        Assert.Equal(0, Rows());
        Assert.Null(await Find("token-a"));
    }
}
