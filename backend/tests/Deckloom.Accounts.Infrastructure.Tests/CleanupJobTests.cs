using System.Data.Common;
using System.Globalization;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-075 (REQ-047). design.md (retencao de eventos de acesso 24 h; sessoes, tokens e pendencias expirados;
// PurgeExpiredData). Contrato para o implementer (GREEN):
//   sealed class ExpiredDataCleanupService : BackgroundService      // namespace Deckloom.Accounts.Infrastructure
//     ctor(Func<CancellationToken, Task<PurgeExpiredDataSummary>> purge, TimeProvider clock, TimeSpan? interval = null)
//     A cada intervalo (padrao ExpiredDataCleanupService.DefaultInterval = 1 h, medido pelo TimeProvider) chama `purge`
//     (a raiz de composicao abre um escopo novo e executa PurgeExpiredData). Se `purge` lancar, o servico segue vivo
//     e tenta no ciclo seguinte. Nao e obrigatorio executar no start; o RED so exige uma chamada por intervalo.
//   Os Ef* abaixo deixam de lancar NotSupportedException("Planned for T-075"):
//     EfSessionRepository.DeleteExpiredOrRevoked(now): apaga sessoes revogadas ou com expires_at <= now; devolve contagem;
//       IMEDIATO (fora do EfUnitOfWork), uma execucao concorrente que ja apagou a linha nao e erro.
//     EfPlayerRepository.DeleteExpiredResetTokens(now): apaga tokens com expires_at <= now (o Jogador permanece);
//       IMEDIATO; devolve contagem.
//   Duas instancias simultaneas (varias replicas) nunca falham: DELETE condicional idempotente, sem excecao.
// Nota: as duas execucoes paralelas usam contextos distintos sobre a MESMA conexao SQLite em memoria; se a conexao
//   compartilhada nao tolerar uso concorrente, o GREEN pode serializar o acesso na fixture sem mudar a assercao.
public sealed class CleanupJobTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Start = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();
    private static readonly byte[] IpKey = Enumerable.Repeat((byte)0xA1, 32).ToArray();
    private static readonly byte[] AccountKey = Enumerable.Repeat((byte)0x11, 32).ToArray();

    private readonly SqliteInMemoryFixture _db = new();
    private readonly FakeTimeProvider _clock = new(Start);

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

    // Uma execucao de PurgeExpiredData com repositorios novos (escopo novo), como o servico faz a cada ciclo.
    private async Task<PurgeExpiredDataSummary> RunPurge(CancellationToken ct = default)
    {
        await using var context = _db.CreateContext();
        var useCase = new PurgeExpiredData(
            new EfSessionRepository(context),
            new EfPlayerRepository(context),
            new EfPendingGoogleAuthStore(context, _clock),
            new EfAccessAttemptStore(context),
            _clock,
            1000);
        return await useCase.ExecuteAsync(ct);
    }

    private int Rows(string table)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // Semeia: 1 evento antigo (> 24 h) + 1 recente; 1 sessao expirada + 1 revogada + 1 viva; 1 token vencido (outro
    // Jogador) + 1 vivo; 1 pendencia expirada + 1 viva. Tudo relativo a Start.
    private async Task Seed()
    {
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");

        await RecordAttempt(Start.AddHours(-25));
        await RecordAttempt(Start.AddHours(-1));

        await PersistSession(ana, "expired-session", Start.AddHours(-2));
        await PersistSession(ana, "revoked-session", Start, revoke: true);
        await PersistSession(ana, "live-session", Start);

        await IssueResetToken(ana.Id, "expired-reset", Start.AddHours(-2));
        await IssueResetToken(bia.Id, "live-reset", Start);

        await PersistPending("expired-pending", Start.AddHours(-1));
        await PersistPending("live-pending", Start);
    }

    private void AssertOnlyFreshDataRemains()
    {
        Assert.Equal(1, Rows("access_attempt"));
        Assert.Equal(1, Rows("player_session"));
        Assert.Equal(1, Rows("password_reset_token"));
        Assert.Equal(1, Rows("pending_google_auth"));
        Assert.Equal(2, Rows("player"));
    }

    [Fact]
    public async Task REQ_047_purge_removes_access_events_older_than_24h_and_keeps_recent_ones()
    {
        await RecordAttempt(Start.AddHours(-25));
        await RecordAttempt(Start.AddHours(-24).AddMinutes(1));
        await RecordAttempt(Start);

        var summary = await RunPurge();

        Assert.Equal(1, summary.AccessAttempts);
        Assert.Equal(2, Rows("access_attempt"));
    }

    [Fact]
    public async Task REQ_047_purge_removes_expired_sessions_reset_tokens_and_pending_google_auths()
    {
        await Seed();

        var summary = await RunPurge();

        Assert.Equal(new PurgeExpiredDataSummary(Sessions: 2, ResetTokens: 1, PendingGoogleAuths: 1, AccessAttempts: 1), summary);
        AssertOnlyFreshDataRemains();
    }

    [Fact]
    public async Task REQ_047_purge_is_idempotent_running_twice_changes_nothing_and_does_not_fail()
    {
        await Seed();

        await RunPurge();
        var second = await RunPurge();

        Assert.Equal(new PurgeExpiredDataSummary(0, 0, 0, 0), second);
        AssertOnlyFreshDataRemains();
    }

    [Fact]
    public async Task REQ_047_two_simultaneous_instances_both_complete_and_the_final_state_is_the_expected_one()
    {
        await Seed();

        var both = await Task.WhenAll(RunPurge(), RunPurge());

        Assert.Equal(1, both.Sum(s => s.AccessAttempts));
        Assert.Equal(1, both.Sum(s => s.ResetTokens));
        Assert.Equal(1, both.Sum(s => s.PendingGoogleAuths));
        Assert.Equal(2, both.Sum(s => s.Sessions));
        AssertOnlyFreshDataRemains();
    }

    [Fact]
    public async Task REQ_047_the_service_calls_purge_once_per_hourly_cycle_driven_by_the_time_provider()
    {
        var calls = new CycleCounter();
        using var service = new ExpiredDataCleanupService(calls.Purge, _clock);

        await service.StartAsync(CancellationToken.None);
        try
        {
            var baseline = calls.Count;
            _clock.Advance(TimeSpan.FromMinutes(59));
            await Task.Delay(100);
            Assert.Equal(baseline, calls.Count);

            _clock.Advance(TimeSpan.FromMinutes(1));
            await calls.WaitUntil(baseline + 1);
            _clock.Advance(TimeSpan.FromHours(1));
            await calls.WaitUntil(baseline + 2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task REQ_047_the_service_keeps_running_after_a_failed_cycle()
    {
        var calls = new CycleCounter { FailFirst = true };
        using var service = new ExpiredDataCleanupService(calls.Purge, _clock);

        await service.StartAsync(CancellationToken.None);
        try
        {
            _clock.Advance(ExpiredDataCleanupService.DefaultInterval);
            await calls.WaitUntil(1);
            _clock.Advance(ExpiredDataCleanupService.DefaultInterval);
            await calls.WaitUntil(2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task REQ_047_a_service_cycle_purges_the_data_past_retention()
    {
        await Seed();
        // QA-10: espera o fim do ciclo por sinal deterministico, nao por polling com prazo de relogio que desiste em silencio.
        var cycleDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new ExpiredDataCleanupService(
            async ct =>
            {
                var summary = await RunPurge(ct);
                cycleDone.TrySetResult();
                return summary;
            },
            _clock);

        await service.StartAsync(CancellationToken.None);
        try
        {
            _clock.Advance(ExpiredDataCleanupService.DefaultInterval);
            await cycleDone.Task.WaitAsync(TimeSpan.FromMinutes(2));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Apos 1 h os eventos de 25 h atras sumiram e o de 1 h atras (agora com 2 h) permanece.
        Assert.Equal(1, Rows("access_attempt"));
    }

    // T-139 (RED): falha de comando de banco (SqlState 57014) no job vira o evento 1021 DatabaseCommandFailed (Error,
    // so sqlState e operation) e o job segue para o ciclo seguinte. Construtor assumido:
    //   ExpiredDataCleanupService(purge, clock, TimeSpan? interval = null, ILogger<ExpiredDataCleanupService>? logger = null)
    [Fact]
    public async Task T_139_a_database_command_failure_in_a_cycle_logs_event_1021_and_the_next_cycle_still_runs()
    {
        var calls = new CycleCounter { FailFirstWith = new CleanupDbException("57014") };
        var logger = new CapturingLogger();
        using var service = new ExpiredDataCleanupService(calls.Purge, _clock, logger: logger);

        await service.StartAsync(CancellationToken.None);
        try
        {
            _clock.Advance(ExpiredDataCleanupService.DefaultInterval);
            await calls.WaitUntil(1);
            _clock.Advance(ExpiredDataCleanupService.DefaultInterval);
            await calls.WaitUntil(2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var logged = Assert.Single(logger.Entries, e => e.Id == 1021);
        Assert.Equal("DatabaseCommandFailed", logged.Name);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Equal(
            ["operation", "sqlState"],
            logged.Properties.Keys.Where(k => k != "{OriginalFormat}").Order().ToArray());
        Assert.Equal("57014", logged.Properties["sqlState"]?.ToString());
        Assert.Equal("ExpiredDataCleanup", logged.Properties["operation"]?.ToString());
    }

    private sealed class CycleCounter
    {
        private int _count;
        private readonly object _gate = new();

        public bool FailFirst { get; init; }

        public Exception? FailFirstWith { get; init; }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        public Task<PurgeExpiredDataSummary> Purge(CancellationToken ct)
        {
            int n;
            lock (_gate)
            {
                n = ++_count;
            }

            if (FailFirst && n == 1)
            {
                throw new InvalidOperationException("boom");
            }

            if (FailFirstWith is not null && n == 1)
            {
                throw FailFirstWith;
            }

            return Task.FromResult(new PurgeExpiredDataSummary(0, 0, 0, 0));
        }

        public async Task WaitUntil(int expected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Count < expected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(Count >= expected, $"Expected at least {expected} purge calls, saw {Count}.");
        }
    }

    private sealed class CleanupDbException(string sqlState) : DbException("command failed")
    {
        public override string? SqlState => sqlState;
    }

    private sealed record LoggedEntry(
        int Id, string? Name, LogLevel Level, IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLogger : ILogger<ExpiredDataCleanupService>
    {
        private readonly object _gate = new();
        private readonly List<LoggedEntry> _entries = [];

        public IReadOnlyList<LoggedEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    props[pair.Key] = pair.Value;
                }
            }

            lock (_gate)
            {
                _entries.Add(new LoggedEntry(eventId.Id, eventId.Name, logLevel, props));
            }
        }
    }
}
