using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Testing;

// T-140 (retro item 1): base das suites de contrato por porta de persistencia. Cada lado (fake em memoria, adapter EF)
// fornece uma subclasse que monta o sistema sob teste; um "escopo" e o que um pedido HTTP enxerga (uma UoW, os
// repositorios dela e um unico contexto). Um segundo escopo le o que o primeiro gravou, como uma leitura posterior.
public sealed class AccountsContractScope(
    IUnitOfWork unitOfWork,
    IPlayerRepository players,
    IAvatarStorage avatars,
    Func<bool> isTransactionOpen,
    Func<ValueTask>? dispose = null,
    ISessionRepository? sessions = null,
    IPendingGoogleAuthStore? pendings = null,
    IAccessAttemptStore? accessAttempts = null) : IAsyncDisposable
{
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public IPlayerRepository Players { get; } = players;

    public IAvatarStorage Avatars { get; } = avatars;

    // T-141: portas adicionais que a exclusao do Jogador atinge; as suites que nao as usam nao precisam fornece-las.
    public ISessionRepository Sessions =>
        sessions ?? throw new InvalidOperationException("This fixture does not provide ISessionRepository.");

    public IPendingGoogleAuthStore Pendings =>
        pendings ?? throw new InvalidOperationException("This fixture does not provide IPendingGoogleAuthStore.");

    // T-144: tentativas de acesso (gravacao imediata, fora da UoW); so a suite dessa porta as fornece.
    public IAccessAttemptStore AccessAttempts =>
        accessAttempts ?? throw new InvalidOperationException("This fixture does not provide IAccessAttemptStore.");

    public bool IsTransactionOpen => isTransactionOpen();

    public ValueTask DisposeAsync() => dispose?.Invoke() ?? ValueTask.CompletedTask;
}

public abstract class AccountsContractFixture : IAsyncDisposable
{
    // Relogio fixo comum aos dois lados (T-141): os relogios fake dos subtipos partem daqui.
    public static readonly DateTimeOffset ContractNow = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // T-143: relogio compartilhado pelos escopos do mesmo fixture; as suites que dependem de tempo o avancam por aqui.
    public FakeTimeProvider Clock { get; } = new(ContractNow);

    public abstract AccountsContractScope CreateScope();

    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync();
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;
}
