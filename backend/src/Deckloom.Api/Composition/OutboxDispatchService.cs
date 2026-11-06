namespace Deckloom.Api.Composition;

// Chama o OutboxDispatcher a cada intervalo (medido pelo TimeProvider), num escopo novo. Uma falha de um ciclo
// (por exemplo banco fora do ar) nao derruba o servico: o proximo ciclo tenta de novo. Sem acesso ao banco na partida.
public sealed partial class OutboxDispatchService(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboxDispatchService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCycle(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Parada solicitada.
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            CycleFailed(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox: ciclo de despacho falhou ({ErrorType}); tenta de novo no proximo.")]
    private static partial void CycleFailed(ILogger logger, string errorType);
}
