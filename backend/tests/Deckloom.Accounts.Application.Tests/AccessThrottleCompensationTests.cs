using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-152 (RED): SEC-16 / REQ-032, REQ-041 (ADR-0005, E4 e E5, nota SEC-16). Uma exclusao compensatoria JA DECIDIDA do
// AccessThrottle (apagar o proprio evento provisorio na recusa por orcamento, ou ReleaseAsync nos desfechos de erro de
// validacao e vinculo concluido) nao depende do cancelamento da requisicao: o token passado ao store nao e o da
// requisicao e a exclusao conclui mesmo com o token cancelado. O cancelamento ANTES do desfecho continua contando (o
// evento fica), e o OperationCanceledException da requisicao ainda propaga ao chamador depois de limpar.
// Lacunas assumidas: (1) o store do teste (CancellationAwareStore) imita o EF: lanca OperationCanceledException em
// qualquer operacao recebida com token cancelado e registra o token recebido em DeleteByIdAsync; (2) a janela "depois
// da gravacao e antes da exclusao" e produzida cancelando a requisicao logo apos a segunda contagem por conta (a do
// orcamento), ultima chamada antes da exclusao compensatoria; (3) os casos de erro de validacao e de vinculo Google
// passam por AccessThrottle.ReleaseAsync, entao sao exercitados nele (os pontos de chamada dos casos de uso ficam
// para o GREEN e para os testes existentes dos casos de uso).
public sealed class AccessThrottleCompensationTests : IDisposable
{
    private const string Ip = "203.0.113.7";
    private const string OtherIp = "198.51.100.1";
    private const string Email = "ana@example.com";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly FakeKeyedHasher _hasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly CancellationTokenSource _request = new();
    private readonly InMemoryAccessAttemptStore _inner;
    private readonly CancellationAwareStore _store;

    public AccessThrottleCompensationTests()
    {
        _inner = new InMemoryAccessAttemptStore(_uow);
        _store = new CancellationAwareStore(_inner);
    }

    public void Dispose() => _request.Dispose();

    private AccessThrottle NewThrottle(IDelayer delayer, int budget = 2) =>
        new(
            _store,
            _hasher,
            delayer,
            new ProgressiveDelayPolicy(new ProgressiveDelayOptions { AccountHourlyBudget = budget }),
            _clock);

    private byte[] AccountKey => _hasher.Hash(Email);

    private async Task SeedAccountEvents(int times)
    {
        for (var i = 0; i < times; i++)
        {
            await _inner.RecordAsync(new AccessAttempt(AccessAction.Login, _hasher.Hash(OtherIp), AccountKey, Now.AddMinutes(-1)));
        }
    }

    private Task<int> AccountCount() => _inner.CountByAccountAsync(AccountKey, Now.AddHours(-1));

    [Fact]
    public async Task SEC16_budget_refusal_deletes_its_own_event_even_if_the_request_is_cancelled_after_the_write()
    {
        await SeedAccountEvents(5);
        _store.CancelRequestAfterBudgetCount = _request;
        var throttle = NewThrottle(new RecordingDelayer());

        // O cancelamento da requisicao ainda propaga (no atraso), mas so depois da compensacao.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip, _request.Token));

        Assert.True(_request.IsCancellationRequested);
        Assert.Equal(5, await AccountCount());
    }

    [Fact]
    public async Task SEC16_budget_refusal_deletes_with_a_token_that_is_not_the_request_token()
    {
        await SeedAccountEvents(5);
        _store.CancelRequestAfterBudgetCount = _request;
        var throttle = NewThrottle(new RecordingDelayer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip, _request.Token));

        var seen = Assert.Single(_store.DeleteTokens);
        Assert.NotEqual(_request.Token, seen);
        Assert.False(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task SEC16_release_removes_the_provisional_event_even_with_a_cancelled_request_token()
    {
        var throttle = NewThrottle(new RecordingDelayer());
        var keys = await throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);
        Assert.NotNull(keys.ReservedAttemptId);
        Assert.Equal(1, await AccountCount());
        await _request.CancelAsync();

        await throttle.ReleaseAsync(keys);

        Assert.Equal(0, await AccountCount());
    }

    [Fact]
    public async Task SEC16_release_deletes_with_a_token_that_is_not_the_request_token()
    {
        var throttle = NewThrottle(new RecordingDelayer());
        var keys = await throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);
        await _request.CancelAsync();

        await throttle.ReleaseAsync(keys);

        var seen = Assert.Single(_store.DeleteTokens);
        Assert.NotEqual(_request.Token, seen);
        Assert.False(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task SEC16_cancellation_before_the_outcome_still_counts_and_leaves_the_event()
    {
        await SeedAccountEvents(1);
        var throttle = NewThrottle(new CancellingDelayer(_request), budget: 30);
        await SeedAccountEvents(4);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip, _request.Token));

        Assert.Equal(6, await AccountCount()); // 5 semeados + o evento provisorio, que conta
        Assert.Empty(_store.DeleteTokens);
    }

    // Cancela a requisicao dentro do atraso: o desfecho ainda nao foi decidido.
    private sealed class CancellingDelayer(CancellationTokenSource request) : IDelayer
    {
        public async Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            await request.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }
    }

    // Imita o EF: toda operacao com token cancelado lanca OperationCanceledException; registra o token das exclusoes.
    private sealed class CancellationAwareStore(InMemoryAccessAttemptStore inner) : IAccessAttemptStore
    {
        private int _accountCounts;

        public List<CancellationToken> DeleteTokens { get; } = [];

        public CancellationTokenSource? CancelRequestAfterBudgetCount { get; set; }

        public Task<long> RecordAsync(AccessAttempt attempt, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.RecordAsync(attempt, ct);
        }

        public Task<int> DeleteByIdAsync(long id, CancellationToken ct = default)
        {
            DeleteTokens.Add(ct);
            ct.ThrowIfCancellationRequested();
            return inner.DeleteByIdAsync(id, ct);
        }

        public Task<int> CountByIpAsync(byte[] ipKeyHash, DateTimeOffset since, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return inner.CountByIpAsync(ipKeyHash, since, ct);
        }

        public async Task<int> CountByAccountAsync(byte[] accountKeyHash, DateTimeOffset since, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var count = await inner.CountByAccountAsync(accountKeyHash, since, ct);
            if (++_accountCounts == 2 && CancelRequestAfterBudgetCount is { } source)
            {
                await source.CancelAsync();
            }

            return count;
        }

        public Task<int> DeleteByAccountAsync(byte[] accountKeyHash, CancellationToken ct = default) =>
            inner.DeleteByAccountAsync(accountKeyHash, ct);

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct = default) =>
            inner.DeleteOlderThanAsync(cutoff, batchSize, ct);
    }
}
