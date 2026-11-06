using System.Globalization;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.Data.Sqlite;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-060 (REQ-032, 041, 047). Tabela: accounts.access_attempt; design.md (U7, "Forma das portas de
// estado", T-027). Contrato para o implementer (GREEN):
//   sealed class EfAccessAttemptStore : IAccessAttemptStore   // namespace Deckloom.Accounts.Infrastructure
//     ctor(AccountsDbContext context)                          // sem relogio: "agora" e parametro (since, cutoff)
//     RecordAsync: IMEDIATO (grava e salva na hora, fora do EfUnitOfWork): as falhas de login precisam persistir
//       mesmo se a operacao falhar (REQ-032).
//     CountByIpAsync(hash, since): linhas com ip_key_hash = hash e attempted_at >= since (inclusivo), de todas as acoes.
//     CountByAccountAsync(hash, since): idem por account_key_hash; linhas sem chave de conta nunca casam.
//     DeleteByAccountAsync(hash): participa do EfUnitOfWork como no fake: devolve na hora quantas linhas tem aquela
//       chave, so ENFILEIRA a exclusao no contexto; as linhas somem no EfUnitOfWork.CommitAsync (mesma transacao
//       da exclusao do Jogador, REQ-047). Sem commit, nada e apagado.
//     DeleteOlderThanAsync(cutoff, 1000): IMEDIATO; apaga attempted_at < cutoff (exatamente no cutoff e mantido) e devolve
//       a contagem.
//   Tabela access_attempt (snake_case): id PK pk_access_attempt (identidade), action text (CHECK nos 5 valores),
//     ip_key_hash (blob, nao nulo), account_key_hash (blob, nulo), attempted_at; indices ix_access_attempt_ip_key
//     (ip_key_hash, attempted_at), ix_access_attempt_account_key (account_key_hash, attempted_at),
//     ix_access_attempt_attempted_at (attempted_at). SEM FK (anti-enumeracao). Nenhuma funcao de data do banco.
public sealed class AccessAttemptPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] IpA = Key(0xA1);
    private static readonly byte[] IpB = Key(0xB2);
    private static readonly byte[] AccountX = Key(0x11);
    private static readonly byte[] AccountY = Key(0x22);

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static byte[] Key(byte seed) => Enumerable.Repeat(seed, 32).ToArray();

    private static AccessAttempt Attempt(
        byte[] ip,
        byte[]? account,
        DateTimeOffset? at = null,
        AccessAction action = AccessAction.Login) => new(action, ip, account, at ?? Now);

    private async Task Record(params AccessAttempt[] attempts)
    {
        foreach (var attempt in attempts)
        {
            await using var context = _db.CreateContext();
            await new EfAccessAttemptStore(context).RecordAsync(attempt);
        }
    }

    private async Task<int> CountByIp(byte[] ip, DateTimeOffset since)
    {
        await using var context = _db.CreateContext();
        return await new EfAccessAttemptStore(context).CountByIpAsync(ip, since);
    }

    private async Task<int> CountByAccount(byte[] account, DateTimeOffset since)
    {
        await using var context = _db.CreateContext();
        return await new EfAccessAttemptStore(context).CountByAccountAsync(account, since);
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

    private int Rows() => Scalar<int>("SELECT COUNT(*) FROM access_attempt");

    [Fact]
    public async Task REQ_047_only_the_two_key_hashes_are_stored_never_the_plain_ip_or_email()
    {
        await Record(Attempt(IpA, AccountX));

        Assert.Equal(
            ["account_key_hash", "action", "attempted_at", "id", "ip_key_hash"],
            Column("SELECT name FROM pragma_table_info('access_attempt') ORDER BY name"));
        using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT ip_key_hash, account_key_hash FROM access_attempt";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(IpA, (byte[])reader["ip_key_hash"]);
        Assert.Equal(AccountX, (byte[])reader["account_key_hash"]);
    }

    [Fact]
    public async Task REQ_041_registration_is_stored_with_a_null_account_key_hash()
    {
        await Record(Attempt(IpA, null, action: AccessAction.Registration));

        Assert.Equal("registration", Scalar<string>("SELECT action FROM access_attempt"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM access_attempt WHERE account_key_hash IS NULL"));
    }

    [Fact]
    public async Task REQ_032_every_action_is_stored_with_its_snake_case_name()
    {
        foreach (var action in Enum.GetValues<AccessAction>())
        {
            await Record(Attempt(IpA, AccountX, action: action));
        }

        Assert.Equal(
            ["account_deletion", "google_link", "login", "password_reset_request", "registration"],
            Column("SELECT action FROM access_attempt ORDER BY action"));
    }

    [Fact]
    public async Task REQ_032_the_database_rejects_an_unknown_action()
    {
        await Record(Attempt(IpA, AccountX));

        using var command = _db.Connection.CreateCommand();
        command.CommandText =
            "INSERT INTO access_attempt (action, ip_key_hash, attempted_at) VALUES ('other', x'AA', '2020-03-04 12:00:00')";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task REQ_032_record_persists_at_once_without_a_unit_of_work_commit()
    {
        await using (var context = _db.CreateContext())
        {
            await new EfAccessAttemptStore(context).RecordAsync(Attempt(IpA, AccountX));
        }

        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task SEC_01_record_returns_the_id_of_the_stored_row_and_delete_by_id_removes_only_that_row()
    {
        long first;
        long second;
        await using (var context = _db.CreateContext())
        {
            var store = new EfAccessAttemptStore(context);
            first = await store.RecordAsync(Attempt(IpA, AccountX));
            second = await store.RecordAsync(Attempt(IpA, AccountX));
        }

        Assert.NotEqual(first, second);
        Assert.Equal(first, Scalar<long>($"SELECT id FROM access_attempt WHERE id = {first}"));

        await using (var context = _db.CreateContext())
        {
            var store = new EfAccessAttemptStore(context);
            Assert.Equal(1, await store.DeleteByIdAsync(first));
            Assert.Equal(0, await store.DeleteByIdAsync(first));
        }

        Assert.Equal(1, Rows());
        Assert.Equal(second, Scalar<long>("SELECT id FROM access_attempt"));
    }

    [Fact]
    public async Task REQ_032_the_schema_declares_the_documented_indexes()
    {
        Assert.Equal(
            ["ip_key_hash", "attempted_at"],
            Column("SELECT name FROM pragma_index_info('ix_access_attempt_ip_key') ORDER BY seqno"));
        Assert.Equal(
            ["account_key_hash", "attempted_at"],
            Column("SELECT name FROM pragma_index_info('ix_access_attempt_account_key') ORDER BY seqno"));
        Assert.Equal(
            ["attempted_at"],
            Column("SELECT name FROM pragma_index_info('ix_access_attempt_attempted_at')"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task REQ_047_access_attempt_has_no_foreign_key_to_player()
    {
        await Record(Attempt(IpA, AccountX));

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_list('access_attempt')"));
    }

    [Fact]
    public async Task REQ_032_the_schema_has_no_database_date_function()
    {
        await Record(Attempt(IpA, AccountX));

        var ddl = string.Join('\n', Column("SELECT sql FROM sqlite_master WHERE tbl_name = 'access_attempt' AND sql IS NOT NULL"));

        Assert.NotEmpty(ddl);
        Assert.DoesNotContain("datetime(", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current_timestamp", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("strftime(", ddl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default", ddl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ_032_count_by_ip_counts_the_ip_in_the_window_across_all_actions()
    {
        await Record(
            Attempt(IpA, AccountX),
            Attempt(IpA, AccountY, action: AccessAction.PasswordResetRequest),
            Attempt(IpA, null, action: AccessAction.Registration),
            Attempt(IpB, AccountX));

        Assert.Equal(3, await CountByIp(IpA, Now.AddMinutes(-15)));
        Assert.Equal(1, await CountByIp(IpB, Now.AddMinutes(-15)));
        Assert.Equal(0, await CountByIp(Key(0xFF), Now.AddMinutes(-15)));
    }

    [Fact]
    public async Task REQ_032_count_window_is_inclusive_at_the_start_and_excludes_older_rows()
    {
        var since = Now.AddMinutes(-15);
        await Record(
            Attempt(IpA, AccountX, since.AddTicks(-1)),
            Attempt(IpA, AccountX, since),
            Attempt(IpA, AccountX, Now));

        Assert.Equal(2, await CountByIp(IpA, since));
        Assert.Equal(2, await CountByAccount(AccountX, since));
    }

    [Fact]
    public async Task REQ_032_count_by_account_counts_only_rows_with_that_account_key()
    {
        await Record(
            Attempt(IpA, AccountX),
            Attempt(IpB, AccountX),
            Attempt(IpA, AccountY),
            Attempt(IpA, null, action: AccessAction.Registration));

        Assert.Equal(2, await CountByAccount(AccountX, Now.AddMinutes(-15)));
        Assert.Equal(1, await CountByAccount(AccountY, Now.AddMinutes(-15)));
        Assert.Equal(0, await CountByAccount(Key(0xFF), Now.AddMinutes(-15)));
    }

    [Fact]
    public async Task REQ_047_delete_by_account_returns_the_count_but_only_queues_until_the_commit()
    {
        await Record(Attempt(IpA, AccountX), Attempt(IpA, AccountX), Attempt(IpA, AccountY));

        await using (var context = _db.CreateContext())
        {
            var count = await new EfAccessAttemptStore(context).DeleteByAccountAsync(AccountX);
            Assert.Equal(2, count);
            Assert.Equal(3, Rows());
        }

        Assert.Equal(3, Rows());
    }

    [Fact]
    public async Task REQ_047_delete_by_account_removes_only_that_key_and_keeps_ip_only_and_other_accounts()
    {
        await Record(
            Attempt(IpA, AccountX),
            Attempt(IpB, AccountX),
            Attempt(IpA, AccountY),
            Attempt(IpA, null, action: AccessAction.Registration));

        await using (var context = _db.CreateContext())
        {
            await new EfAccessAttemptStore(context).DeleteByAccountAsync(AccountX);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(2, Rows());
        Assert.Equal(0, await CountByAccount(AccountX, Now.AddDays(-1)));
        Assert.Equal(1, await CountByAccount(AccountY, Now.AddDays(-1)));
        Assert.Equal(2, await CountByIp(IpA, Now.AddDays(-1)));
        Assert.Equal(0, await CountByIp(IpB, Now.AddDays(-1)));
    }

    [Fact]
    public async Task REQ_047_delete_by_account_with_no_matching_rows_returns_zero_and_commits_cleanly()
    {
        await Record(Attempt(IpA, AccountY));

        await using var context = _db.CreateContext();
        var count = await new EfAccessAttemptStore(context).DeleteByAccountAsync(AccountX);
        await new EfUnitOfWork(context).CommitAsync();

        Assert.Equal(0, count);
        Assert.Equal(1, Rows());
    }

    [Fact]
    public async Task REQ_047_delete_older_than_discards_old_rows_keeps_recent_ones_and_returns_the_count()
    {
        var cutoff = Now.AddHours(-24);
        await Record(
            Attempt(IpA, AccountX, cutoff.AddHours(-5)),
            Attempt(IpA, null, cutoff.AddTicks(-1), AccessAction.Registration),
            Attempt(IpA, AccountX, cutoff),
            Attempt(IpB, AccountY, Now));

        int deleted;
        await using (var context = _db.CreateContext())
        {
            deleted = await new EfAccessAttemptStore(context).DeleteOlderThanAsync(cutoff, 1000);
        }

        Assert.Equal(2, deleted);
        Assert.Equal(2, Rows());
        Assert.Equal(1, await CountByIp(IpA, cutoff));
        Assert.Equal(1, await CountByIp(IpB, cutoff));
    }

    [Fact]
    public async Task REQ_047_delete_older_than_applies_at_once_without_a_commit()
    {
        await Record(Attempt(IpA, AccountX, Now.AddDays(-3)));

        await using (var context = _db.CreateContext())
        {
            Assert.Equal(1, await new EfAccessAttemptStore(context).DeleteOlderThanAsync(Now.AddHours(-24), 1000));
        }

        Assert.Equal(0, Rows());
    }
}
