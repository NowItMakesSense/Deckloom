using System.Data.Common;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// R-18 (REQ-047). O filtro de data das limpezas de retencao (sessao, token de redefinicao, login Google pendente) roda
// no banco: a limpeza nao le linha alguma das tabelas (so SELECT EXISTS e DELETE com predicado e LIMIT), e a fronteira
// da expiracao continua a mesma (expira_em <= agora apaga; um instante antes mantem).
public sealed class CleanupFilterInDatabaseTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Start = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();

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

    private async Task<Session> PersistSession(Player player, string token)
    {
        var session = Session.Start(player.Id, Hasher.Hash(token), false, Start);
        await using var context = _db.CreateContext();
        await new EfSessionRepository(context).Add(session);
        await new EfUnitOfWork(context).CommitAsync();
        return session;
    }

    private async Task<DateTimeOffset> IssueResetToken(Player player, string token)
    {
        await using var context = _db.CreateContext();
        var repository = new EfPlayerRepository(context);
        var loaded = (await repository.GetById(player.Id))!;
        Assert.True(loaded.IssuePasswordReset(Hasher.Hash(token), Start).IsSuccess);
        await repository.Save(loaded);
        await new EfUnitOfWork(context).CommitAsync();
        return loaded.PasswordResetToken!.ExpiresAt;
    }

    private async Task<PendingGoogleAuth> PersistPending(string token, FakeTimeProvider clock)
    {
        var pending = PendingGoogleSignup.Create(
            Hasher.Hash(token), "sub-" + token, Email.Create("gui@example.com").Value, "Gui Google", Start);
        await using var context = _db.CreateContext();
        await new EfPendingGoogleAuthStore(context, clock).AddAsync(pending);
        await new EfUnitOfWork(context).CommitAsync();
        return pending;
    }

    private int Rows(string table)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<(PurgeExpiredDataSummary Summary, ReadProbe Probe)> Purge(DateTimeOffset now)
    {
        var probe = new ReadProbe();
        var clock = new FakeTimeProvider(now);
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlite(_db.Connection)
            .AddInterceptors(probe)
            .Options;
        await using var context = new TestAccountsDbContext(options);
        var job = new PurgeExpiredData(
            new EfSessionRepository(context),
            new EfPlayerRepository(context),
            new EfPendingGoogleAuthStore(context, clock),
            new EfAccessAttemptStore(context),
            clock,
            batchSize: 10);
        return (await job.ExecuteAsync(), probe);
    }

    [Fact]
    public async Task R18_session_cleanup_reads_no_row_and_keeps_the_cutoff_boundary()
    {
        var owner = await PersistPlayer("owner@example.com");
        var session = await PersistSession(owner, "s1");

        var (before, beforeProbe) = await Purge(session.ExpiresAt.AddTicks(-1));
        Assert.Equal(0, before.Sessions);
        Assert.Equal(1, Rows("player_session"));
        Assert.Empty(beforeProbe.SelectsOn("player_session"));

        var (atCutoff, atProbe) = await Purge(session.ExpiresAt);
        Assert.Equal(1, atCutoff.Sessions);
        Assert.Equal(0, Rows("player_session"));
        Assert.Empty(atProbe.SelectsOn("player_session"));
    }

    [Fact]
    public async Task R18_reset_token_cleanup_reads_no_row_and_keeps_the_cutoff_boundary()
    {
        var owner = await PersistPlayer("owner@example.com");
        var expiresAt = await IssueResetToken(owner, "r1");

        var (before, beforeProbe) = await Purge(expiresAt.AddTicks(-1));
        Assert.Equal(0, before.ResetTokens);
        Assert.Equal(1, Rows("password_reset_token"));
        Assert.Empty(beforeProbe.SelectsOn("password_reset_token"));

        var (atCutoff, atProbe) = await Purge(expiresAt);
        Assert.Equal(1, atCutoff.ResetTokens);
        Assert.Equal(0, Rows("password_reset_token"));
        Assert.Equal(1, Rows("player"));
        Assert.Empty(atProbe.SelectsOn("password_reset_token"));
    }

    [Fact]
    public async Task R18_pending_google_cleanup_reads_no_row_and_keeps_the_cutoff_boundary()
    {
        var pending = await PersistPending("p1", new FakeTimeProvider(Start));

        var (before, beforeProbe) = await Purge(pending.ExpiresAt.AddTicks(-1));
        Assert.Equal(0, before.PendingGoogleAuths);
        Assert.Equal(1, Rows("pending_google_auth"));
        Assert.Empty(beforeProbe.SelectsOn("pending_google_auth"));

        var (atCutoff, atProbe) = await Purge(pending.ExpiresAt);
        Assert.Equal(1, atCutoff.PendingGoogleAuths);
        Assert.Equal(0, Rows("pending_google_auth"));
        Assert.Empty(atProbe.SelectsOn("pending_google_auth"));
    }

    [Fact]
    public async Task R18_revoked_sessions_are_removed_even_before_expiry()
    {
        var owner = await PersistPlayer("owner@example.com");
        var revoked = await PersistSession(owner, "revoked");
        await PersistSession(owner, "live");
        await using (var context = _db.CreateContext())
        {
            var repository = new EfSessionRepository(context);
            var loaded = (await repository.FindById(revoked.Id))!;
            loaded.Revoke(SessionRevocationReason.Logout, Start);
            await repository.Save(loaded);
            await new EfUnitOfWork(context).CommitAsync();
        }

        var (summary, probe) = await Purge(Start);

        Assert.Equal(1, summary.Sessions);
        Assert.Equal(1, Rows("player_session"));
        Assert.Empty(probe.SelectsOn("player_session"));
    }

    // Registra o texto de cada consulta que le linhas; a checagem de existencia (SELECT EXISTS) nao le linha e e ignorada.
    private sealed class ReadProbe : DbCommandInterceptor
    {
        private readonly List<string> _selects = [];
        private readonly object _gate = new();

        public List<string> SelectsOn(string table)
        {
            lock (_gate)
            {
                return _selects.Where(s => s.Contains($"\"{table}\"", StringComparison.Ordinal)).ToList();
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
        {
            var text = command.CommandText.TrimStart();
            var isExistenceCheck = text.StartsWith("SELECT EXISTS", StringComparison.OrdinalIgnoreCase);
            if (isExistenceCheck || !text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_gate)
            {
                _selects.Add(command.CommandText);
            }
        }
    }
}
