using Deckloom.Accounts.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure;

// T-075 (REQ-047): a cada Interval (padrao 1 h, medido pelo TimeProvider) chama `purge`, que executa PurgeExpiredData
// num escopo novo. Uma falha de um ciclo nao derruba o servico: o proximo ciclo tenta de novo.
public sealed class ExpiredDataCleanupService : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    private const string OperationName = "ExpiredDataCleanup";

    private readonly Func<CancellationToken, Task<PurgeExpiredDataSummary>> _purge;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExpiredDataCleanupService>? _logger;
    private PeriodicTimer? _timer;

    public ExpiredDataCleanupService(
        Func<CancellationToken, Task<PurgeExpiredDataSummary>> purge,
        TimeProvider clock,
        TimeSpan? interval = null,
        ILogger<ExpiredDataCleanupService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(purge);
        ArgumentNullException.ThrowIfNull(clock);
        _purge = purge;
        _clock = clock;
        _logger = logger;
        Interval = interval ?? DefaultInterval;
    }

    public TimeSpan Interval { get; }

    // O temporizador periodico nasce na partida (antes de o laco ser agendado) e acumula os ticks: avancar o relogio
    // logo apos StartAsync, ou durante um ciclo, nao perde nenhum vencimento.
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new PeriodicTimer(Interval, _clock);
        return base.StartAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunCycle(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada solicitada.
        }
    }

    private void ReportDatabaseFailure(Exception exception)
    {
        if (!DatabaseFailureClassifier.TryGetKnownSqlState(exception, out var sqlState))
        {
            return;
        }

        AccountsMetrics.DatabaseCommandFailed(sqlState);
        if (_logger is not null)
        {
            AccountsLog.DatabaseCommandFailed(_logger, LogLevel.Error, sqlState, OperationName);
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        try
        {
            await _purge(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Tenta de novo no proximo ciclo.
            ReportDatabaseFailure(exception);
        }
    }
}
