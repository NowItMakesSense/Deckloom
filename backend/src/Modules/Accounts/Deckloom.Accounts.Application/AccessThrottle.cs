using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class AccessThrottle(
    IAccessAttemptStore store,
    IKeyedHasher hasher,
    IDelayer delayer,
    ProgressiveDelayPolicy policy,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    // Recusa por orcamento por conta (SEC-01): codigo estavel, mensagem generica, sem dica de tempo (U14).
    public static readonly DomainError TooManyAttempts = new("too_many_attempts");

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    private static readonly TimeSpan BudgetWindow = TimeSpan.FromMinutes(60);

    // Acoes com chave de conta gravam o evento provisorio ANTES de contar, esperar e verificar a senha (ADR-0005,
    // emenda SEC-01): a i-esima tentativa gravada ve pelo menos i eventos, entao a concorrencia eleva o atraso e o
    // orcamento por conta vale para qualquer numero de IPs. O cadastro nao muda (so IP, gravado na recusa).
    public async Task<AccessAttemptKeys> BeforeAttemptAsync(
        AccessAction action, string? email, string ip, CancellationToken ct = default)
    {
        var ipKeyHash = hasher.Hash(ip);
        var accountKeyHash = action == AccessAction.Registration || email is null
            ? null
            : hasher.Hash(email.Trim().ToLowerInvariant());

        long? reservedId = accountKeyHash is null
            ? null
            : await store.RecordAsync(new AccessAttempt(action, ipKeyHash, accountKeyHash, time.GetUtcNow()), ct);
        var own = reservedId is null ? 0 : 1;

        var now = time.GetUtcNow();
        var since = now - Window;
        var ipCount = await store.CountByIpAsync(ipKeyHash, since, ct);
        var accountCount = accountKeyHash is null
            ? 0
            : await store.CountByAccountAsync(accountKeyHash, since, ct);

        var budgetExceeded = false;
        if (accountKeyHash is not null
            && await store.CountByAccountAsync(accountKeyHash, now - BudgetWindow, ct) > policy.AccountHourlyBudget)
        {
            // A recusa nao consome orcamento: sem isso, tentativas excedentes estenderiam a propria recusa.
            await DeleteProvisionalAsync(reservedId!.Value);
            reservedId = null;
            budgetExceeded = true;
            _events.AccessBudgetExceeded(AccountsMetrics.ActionCode(action));
        }

        var delay = policy.DelayFor(accountCount - own, ipCount - own, action);
        if (delay > TimeSpan.Zero)
        {
            AccountsMetrics.ProgressiveDelayApplied(action, delay);
            _events.ProgressiveDelayApplied(
                AccountsMetrics.ActionCode(action),
                (int)delay.TotalMilliseconds,
                ScopeOf(policy.DelayFor(accountCount - own, 0, action), delay));
            await delayer.DelayAsync(delay, ct);
        }

        return new AccessAttemptKeys(action, ipKeyHash, accountKeyHash, reservedId, budgetExceeded);
    }

    // O atraso aplicado e o maior entre conta e IP; o escopo e quem o determinou.
    private static string ScopeOf(TimeSpan accountDelay, TimeSpan applied) =>
        accountDelay == applied ? "account" : "ip";

    // Chaves sem evento provisorio, sem contagem, sem atraso e sem orcamento: para fluxos fora do orcamento (exclusao
    // de conta Google, sem senha a adivinhar), que ainda precisam das chaves para limpar as falhas da conta.
    public AccessAttemptKeys KeysWithoutAttempt(AccessAction action, string? email, string ip) =>
        new(action, hasher.Hash(ip), email is null ? null : hasher.Hash(email.Trim().ToLowerInvariant()));

    // So o cadastro grava assim (recusa por email em uso, REQ-041); as demais acoes ja gravaram o evento provisorio.
    public Task RecordAsync(AccessAttemptKeys keys, CancellationToken ct = default) =>
        store.RecordAsync(
            new AccessAttempt(keys.Action, keys.IpKeyHash, keys.AccountKeyHash, time.GetUtcNow()), ct);

    // Apaga so o evento provisorio da tentativa (validacao que nao conta, vinculo concluido). Sem efeito sem id.
    public async Task ReleaseAsync(AccessAttemptKeys keys)
    {
        if (keys.ReservedAttemptId is { } id)
        {
            await DeleteProvisionalAsync(id);
        }
    }

    // SEC-16 / ADR-0005 E4: as exclusoes compensatorias (evento provisorio e falhas da conta) nao podem ser canceladas
    // com a requisicao (senao o evento ficaria contando / o Jogador legitimo herdaria o atraso); por isso nao usam o ct
    // da requisicao. Ponto unico da regra de token.
    private Task<int> DeleteProvisionalAsync(long id) => store.DeleteByIdAsync(id, CancellationToken.None);

    public async Task ClearAccountFailuresAsync(AccessAttemptKeys keys)
    {
        if (keys.AccountKeyHash is null)
        {
            return;
        }

        await store.DeleteByAccountAsync(keys.AccountKeyHash, CancellationToken.None);
    }
}
