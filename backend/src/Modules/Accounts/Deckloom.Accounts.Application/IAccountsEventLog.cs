using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// T-093: porta de log do catalogo de eventos 1001-1020 (design.md). So aceita identificadores internos e codigos
// fechados: nunca email, senha, token, hash, IP, nome de exibicao ou bio. O adapter fica na API (Composition),
// porque o catalogo (AccountsLog) mora na Infrastructure.
public interface IAccountsEventLog
{
    void PlayerRegistered(PlayerId playerId, string method);

    void LoginSucceeded(PlayerId playerId, SessionId sessionId, bool rememberMe);

    void LoginFailed(string reason, PlayerId? playerId);

    void LoggedOut(PlayerId playerId, SessionId sessionId);

    void PasswordResetRequested(string outcome, PlayerId? playerId);

    void PasswordResetCompleted(PlayerId playerId, int revokedSessions);

    void PasswordResetRejected(string reason);

    void PasswordResetEmailFailed(PlayerId playerId, int attempt);

    void GoogleAuthCompleted(string outcome, PlayerId? playerId);

    void GoogleAuthFailed(string stage, string reason);

    void GoogleLinked(PlayerId playerId);

    void GoogleLinkRejected(PlayerId playerId, string reason);

    void ProfileUpdated(PlayerId playerId, IReadOnlyList<string> changedFields);

    void AvatarChanged(PlayerId playerId, string kind);

    void AvatarRejected(PlayerId playerId, string reason, string? detail);

    void ProgressiveDelayApplied(string action, int delayMs, string scope);

    void AccessBudgetExceeded(string action);

    void AccountDeletionRejected(PlayerId playerId, string reason);

    void SessionRevoked(PlayerId playerId, SessionId sessionId, string reason);
}

// Padrao dos casos de uso construidos sem adapter (testes de unidade): nao registra nada.
public sealed class NullAccountsEventLog : IAccountsEventLog
{
    public static readonly NullAccountsEventLog Instance = new();

    private NullAccountsEventLog()
    {
    }

    public void PlayerRegistered(PlayerId playerId, string method)
    {
    }

    public void LoginSucceeded(PlayerId playerId, SessionId sessionId, bool rememberMe)
    {
    }

    public void LoginFailed(string reason, PlayerId? playerId)
    {
    }

    public void LoggedOut(PlayerId playerId, SessionId sessionId)
    {
    }

    public void PasswordResetRequested(string outcome, PlayerId? playerId)
    {
    }

    public void PasswordResetCompleted(PlayerId playerId, int revokedSessions)
    {
    }

    public void PasswordResetRejected(string reason)
    {
    }

    public void PasswordResetEmailFailed(PlayerId playerId, int attempt)
    {
    }

    public void GoogleAuthCompleted(string outcome, PlayerId? playerId)
    {
    }

    public void GoogleAuthFailed(string stage, string reason)
    {
    }

    public void GoogleLinked(PlayerId playerId)
    {
    }

    public void GoogleLinkRejected(PlayerId playerId, string reason)
    {
    }

    public void ProfileUpdated(PlayerId playerId, IReadOnlyList<string> changedFields)
    {
    }

    public void AvatarChanged(PlayerId playerId, string kind)
    {
    }

    public void AvatarRejected(PlayerId playerId, string reason, string? detail)
    {
    }

    public void ProgressiveDelayApplied(string action, int delayMs, string scope)
    {
    }

    public void AccessBudgetExceeded(string action)
    {
    }

    public void AccountDeletionRejected(PlayerId playerId, string reason)
    {
    }

    public void SessionRevoked(PlayerId playerId, SessionId sessionId, string reason)
    {
    }
}
