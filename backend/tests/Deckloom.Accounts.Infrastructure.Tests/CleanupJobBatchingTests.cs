using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-138 (REQ-047, DB9/ADR-0015, U/UA3). A limpeza apaga em lotes, cada lote numa transacao curta propria.
// Contrato para o implementer (GREEN), fixado aqui por reflexao para o RED falhar por comportamento ausente e nao por
// erro de compilacao:
//   - Portas: ISessionRepository.DeleteExpiredOrRevoked(now, batchSize, ct), IPlayerRepository.DeleteExpiredResetTokens(
//     now, batchSize, ct), IPendingGoogleAuthStore.DeleteExpiredAsync(batchSize, ct), IAccessAttemptStore.
//     DeleteOlderThanAsync(cutoff, batchSize, ct). Cada chamada apaga NO MAXIMO um lote (DELETE com limite, em
//     transacao propria, commit antes de voltar) e devolve a quantidade apagada.
//   - PurgeExpiredData ganha o parametro de construtor `int batchSize` (ultimo; nome exatamente "batchSize"), repete
//     cada limpeza ate um lote vir menor que batchSize (35 linhas com lote 10 => 4 DELETEs: 10, 10, 10, 5), soma as
//     contagens no PurgeExpiredDataSummary e, com o CancellationToken cancelado, para ENTRE lotes sem lancar.
//   - batchSize <= 0 => ArgumentOutOfRangeException no construtor (faixa de Cleanup:BatchSize, padrao 1000 na
//     composicao). A leitura da configuracao em si fica na composicao (T-075) e nao e testada aqui.
public sealed partial class CleanupJobBatchingTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const int Batch = 10;
    private const int Old = 35;
    private const int Recent = 3;

    private static readonly DateTimeOffset Start = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();
    private static readonly byte[] IpKey = Enumerable.Repeat((byte)0xA1, 32).ToArray();
    private static readonly byte[] AccountKey = Enumerable.Repeat((byte)0x11, 32).ToArray();

    private readonly SqliteInMemoryFixture _db = new();
    private readonly FakeTimeProvider _clock = new(Start);

    public void Dispose() => _db.Dispose();

    // ---- semeadura ----

    private async Task SeedAccessAttempts()
    {
        for (var i = 0; i < Old; i++)
        {
            await RecordAttempt(Start.AddHours(-25).AddMinutes(-i));
        }

        for (var i = 0; i < Recent; i++)
        {
            await RecordAttempt(Start.AddHours(-1).AddMinutes(-i));
        }
    }

    private async Task SeedSessions()
    {
        var owner = await PersistPlayer("owner@example.com");
        for (var i = 0; i < Old; i++)
        {
            // 20 expiradas + 15 revogadas
            await PersistSession(owner, "old-session-" + i, i < 20 ? Start.AddHours(-2) : Start, revoke: i >= 20);
        }

        for (var i = 0; i < Recent; i++)
        {
            await PersistSession(owner, "live-session-" + i, Start);
        }
    }

    private async Task SeedResetTokens()
    {
        for (var i = 0; i < Old; i++)
        {
            var player = await PersistPlayer($"old{i}@example.com");
            await IssueResetToken(player.Id, "old-reset-" + i, Start.AddHours(-2));
        }

        for (var i = 0; i < Recent; i++)
        {
            var player = await PersistPlayer($"live{i}@example.com");
            await IssueResetToken(player.Id, "live-reset-" + i, Start);
        }
    }

    private async Task SeedPendings()
    {
        for (var i = 0; i < Old; i++)
        {
            await PersistPending("old-pending-" + i, Start.AddHours(-1));
        }

        for (var i = 0; i < Recent; i++)
        {
            await PersistPending("live-pending-" + i, Start);
        }
    }

    private static Player NewPlayer(string email) =>
        Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Start,
            "preset-1").Value;

    private async Task<Player> PersistPlayer(string email)
    {
        var player = NewPlayer(email);
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player;
    }

    private async Task PersistSession(Player player, string token, DateTimeOffset startedAt, bool revoke = false)
    {
        var session = Session.Start(player.Id, Hasher.Hash(token), false, startedAt);
        if (revoke)
        {
            session.Revoke(SessionRevocationReason.Logout, startedAt);
        }

        await using var context = _db.CreateContext();
        await new EfSessionRepository(context).Add(session);
        await new EfUnitOfWork(context).CommitAsync();
    }

    private async Task IssueResetToken(PlayerId id, string token, DateTimeOffset issuedAt)
    {
        await using var context = _db.CreateContext();
        var repository = new EfPlayerRepository(context);
        var player = (await repository.GetById(id))!;
        Assert.True(player.IssuePasswordReset(Hasher.Hash(token), issuedAt).IsSuccess);
        await repository.Save(player);
        await new EfUnitOfWork(context).CommitAsync();
    }

    private async Task PersistPending(string token, DateTimeOffset createdAt)
    {
        var pending = PendingGoogleSignup.Create(
            Hasher.Hash(token), "sub-" + token, Email.Create("gui@example.com").Value, "Gui Google", createdAt);
        await using var context = _db.CreateContext();
        await new EfPendingGoogleAuthStore(context, _clock).AddAsync(pending);
        await new EfUnitOfWork(context).CommitAsync();
    }

    private async Task RecordAttempt(DateTimeOffset at)
    {
        await using var context = _db.CreateContext();
        await new EfAccessAttemptStore(context).RecordAsync(new AccessAttempt(AccessAction.Login, IpKey, AccountKey, at));
    }

    private int Rows(string table)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---- execucao instrumentada ----

    private static PurgeExpiredData Build(AccountsDbContext context, TimeProvider clock, int batchSize)
    {
        var constructor = typeof(PurgeExpiredData).GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Any(p => p.Name == "batchSize" && p.ParameterType == typeof(int)));
        Assert.True(
            constructor is not null,
            "T-138: PurgeExpiredData precisa de um construtor com o parametro `int batchSize` (Cleanup:BatchSize).");

        var values = constructor!.GetParameters().Select(p => p.Name switch
        {
            "sessions" => (object)new EfSessionRepository(context),
            "players" => new EfPlayerRepository(context),
            "pendingGoogleAuths" => new EfPendingGoogleAuthStore(context, clock),
            "accessAttempts" => new EfAccessAttemptStore(context),
            "clock" => clock,
            "batchSize" => batchSize,
            _ => throw new InvalidOperationException($"Parametro inesperado '{p.Name}'."),
        }).ToArray();

        try
        {
            return (PurgeExpiredData)constructor.Invoke(values);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private async Task<PurgeExpiredDataSummary> RunPurge(
        WriteProbe probe, int batchSize = Batch, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlite(_db.Connection)
            .AddInterceptors(probe.Capture, probe, probe.Transactions)
            .Options;
        await using var context = new TestAccountsDbContext(options);
        var summary = await Build(context, _clock, batchSize).ExecuteAsync(ct);
        Assert.Null(context.Database.CurrentTransaction);
        return summary;
    }

    private static List<CapturedWrite> DeletesOn(WriteProbe probe, string table) =>
        probe.Capture.Writes.Where(w => w.Kind == WriteKind.Delete && w.Table == table).ToList();

    public static TheoryData<string> Tables => new()
    {
        "access_attempt",
        "player_session",
        "password_reset_token",
        "pending_google_auth",
    };

    private Task Seed(string table) => table switch
    {
        "access_attempt" => SeedAccessAttempts(),
        "player_session" => SeedSessions(),
        "password_reset_token" => SeedResetTokens(),
        "pending_google_auth" => SeedPendings(),
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    private static int Count(PurgeExpiredDataSummary summary, string table) => table switch
    {
        "access_attempt" => summary.AccessAttempts,
        "player_session" => summary.Sessions,
        "password_reset_token" => summary.ResetTokens,
        _ => summary.PendingGoogleAuths,
    };

    // ---- testes ----

    [Theory]
    [MemberData(nameof(Tables))]
    public async Task REQ_047_a_cycle_deletes_in_batches_of_at_most_the_batch_size_until_no_old_row_remains(string table)
    {
        await Seed(table);
        var probe = new WriteProbe();

        var summary = await RunPurge(probe);

        var deletes = DeletesOn(probe, table);
        Assert.Equal(4, deletes.Count);
        Assert.Equal([10, 10, 10, 5], probe.AffectedRows(table));
        Assert.Equal(Old, Count(summary, table));
        Assert.Equal(Recent, Rows(table == "password_reset_token" ? "password_reset_token" : table));
    }

    [Fact]
    public async Task REQ_047_recent_access_events_stay_while_the_old_ones_are_removed()
    {
        await SeedAccessAttempts();

        await RunPurge(new WriteProbe());

        Assert.Equal(Recent, Rows("access_attempt"));
    }

    [Fact]
    public async Task REQ_047_each_batch_runs_in_its_own_short_transaction_committed_before_the_next()
    {
        await SeedAccessAttempts();
        var probe = new WriteProbe();

        await RunPurge(probe);

        var deletes = probe.DeleteTransactions("access_attempt");
        Assert.Equal(4, deletes.Count);
        Assert.All(deletes, t => Assert.NotNull(t));
        Assert.Equal(4, deletes.Distinct().Count());
        Assert.Equal(4, probe.Transactions.Committed);
        Assert.Equal(1, probe.Transactions.MaxOpen);
    }

    [Fact]
    public async Task REQ_047_a_second_run_changes_nothing_and_issues_no_delete()
    {
        await SeedAccessAttempts();
        await RunPurge(new WriteProbe());

        var probe = new WriteProbe();
        var second = await RunPurge(probe);

        Assert.Equal(new PurgeExpiredDataSummary(0, 0, 0, 0), second);
        Assert.Equal(Recent, Rows("access_attempt"));
        Assert.Equal(0, probe.Transactions.Opened - probe.Transactions.Committed - probe.Transactions.RolledBack);
        Assert.True(probe.AffectedRows("access_attempt").All(n => n == 0));
    }

    [Fact]
    public async Task REQ_047_cancelling_between_batches_stops_without_error_and_leaves_no_open_transaction()
    {
        await SeedAccessAttempts();
        using var cts = new CancellationTokenSource();
        var probe = new WriteProbe { OnDelete = () => cts.Cancel() };

        var summary = await RunPurge(probe, ct: cts.Token);

        Assert.Equal(10, summary.AccessAttempts);
        Assert.Single(DeletesOn(probe, "access_attempt"));
        Assert.Equal(Old - 10 + Recent, Rows("access_attempt"));
        Assert.Equal(probe.Transactions.Opened, probe.Transactions.Committed + probe.Transactions.RolledBack);
    }

    [Fact]
    public async Task REQ_047_two_simultaneous_instances_do_not_fail_and_together_remove_every_old_row()
    {
        await SeedAccessAttempts();

        var both = await Task.WhenAll(RunPurge(new WriteProbe()), RunPurge(new WriteProbe()));

        Assert.Equal(Old, both.Sum(s => s.AccessAttempts));
        Assert.Equal(Recent, Rows("access_attempt"));
    }

    [Fact]
    public async Task REQ_047_no_captured_delete_is_unbounded()
    {
        await SeedAccessAttempts();
        await SeedSessions();
        await SeedResetTokens();
        await SeedPendings();
        var probe = new WriteProbe();

        await RunPurge(probe);

        var deletes = probe.Capture.Writes.Where(w => w.Kind == WriteKind.Delete).ToList();
        Assert.NotEmpty(deletes);
        Assert.All(
            deletes,
            d => Assert.True(
                LimitRegex().IsMatch(d.Sql),
                $"T-138: DELETE sem limite de lote em '{d.Table}': {d.Sql}"));
    }

    [Fact]
    public async Task REQ_047_retention_rules_are_unchanged_the_24h_window_and_recent_rows_are_kept()
    {
        await RecordAttempt(Start.AddHours(-25));
        await RecordAttempt(Start.AddHours(-24).AddMinutes(1));
        await RecordAttempt(Start);

        var summary = await RunPurge(new WriteProbe());

        Assert.Equal(1, summary.AccessAttempts);
        Assert.Equal(2, Rows("access_attempt"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void REQ_047_a_batch_size_not_greater_than_zero_is_rejected(int batchSize)
    {
        using var context = _db.CreateContext();

        Assert.Throws<ArgumentOutOfRangeException>(() => Build(context, _clock, batchSize));
    }

    [GeneratedRegex(@"\bLIMIT\b|\bTOP\b|\bFETCH\s+FIRST\b", RegexOptions.IgnoreCase)]
    private static partial Regex LimitRegex();

    // Registra, por DELETE executado, a tabela, as linhas afetadas e a transacao do comando.
    private sealed class WriteProbe : DbCommandInterceptor
    {
        private readonly List<(string Table, int Rows, DbTransaction? Transaction)> _deletes = [];
        private readonly object _gate = new();

        public UpdateStatementCapture Capture { get; } = new();

        public TransactionProbe Transactions { get; } = new();

        public Action? OnDelete { get; init; }

        public List<int> AffectedRows(string table)
        {
            lock (_gate)
            {
                return _deletes.Where(d => d.Table == table).Select(d => d.Rows).ToList();
            }
        }

        public List<DbTransaction?> DeleteTransactions(string table)
        {
            lock (_gate)
            {
                return _deletes.Where(d => d.Table == table).Select(d => d.Transaction).ToList();
            }
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Observe(command, result);
            return ValueTask.FromResult(result);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Observe(command, result);
            return result;
        }

        private void Observe(DbCommand command, int result)
        {
            var match = DeleteTableRegex().Match(command.CommandText);
            if (!match.Success)
            {
                return;
            }

            lock (_gate)
            {
                _deletes.Add((match.Groups["table"].Value, result, command.Transaction));
            }

            OnDelete?.Invoke();
        }
    }

    private sealed class TransactionProbe : DbTransactionInterceptor
    {
        private int _opened;
        private int _committed;
        private int _rolledBack;
        private int _open;
        private int _maxOpen;

        public int Opened => _opened;

        public int Committed => _committed;

        public int RolledBack => _rolledBack;

        public int MaxOpen => _maxOpen;

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            Started();
            return ValueTask.FromResult(result);
        }

        public override DbTransaction TransactionStarted(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            Started();
            return result;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _committed);
            Interlocked.Decrement(ref _open);
            return Task.CompletedTask;
        }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            Interlocked.Increment(ref _committed);
            Interlocked.Decrement(ref _open);
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _rolledBack);
            Interlocked.Decrement(ref _open);
            return Task.CompletedTask;
        }

        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        {
            Interlocked.Increment(ref _rolledBack);
            Interlocked.Decrement(ref _open);
        }

        private void Started()
        {
            Interlocked.Increment(ref _opened);
            var now = Interlocked.Increment(ref _open);
            int seen;
            do
            {
                seen = _maxOpen;
            }
            while (now > seen && Interlocked.CompareExchange(ref _maxOpen, now, seen) != seen);
        }
    }

    [GeneratedRegex("""^DELETE\s+FROM\s+(?:"\w+"\.)?"(?<table>\w+)"(?:\s|$)""", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteTableRegex();
}
