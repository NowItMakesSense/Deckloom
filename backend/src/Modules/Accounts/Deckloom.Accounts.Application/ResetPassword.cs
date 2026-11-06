using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class ResetPassword(
    IPlayerRepository players,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private static readonly DomainError LinkInvalid = new(Player.ResetLinkInvalidCode);

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<PasswordResetCompleted>> ExecuteAsync(
        ResetPasswordCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await AttemptAsync(command, ct);
        AccountsMetrics.PasswordResetOutcome(result.IsSuccess ? "completed" : "rejected");
        if (result.IsFailure)
        {
            _events.PasswordResetRejected(RejectionReason(result.Errors));
        }

        return result;
    }

    private static string RejectionReason(IReadOnlyList<DomainError> errors) =>
        errors.Any(e => e.Code == Player.ResetLinkInvalidCode) ? "invalid_token" : "validation";

    private async Task<Result<PasswordResetCompleted>> AttemptAsync(ResetPasswordCommand command, CancellationToken ct)
    {
        // A senha e validada antes do token: erro de senha nao consome o link.
        var validation = new ValidationErrorCollector();
        var password = validation.CollectNewPassword(command.NewPassword, "newPassword");
        if (validation.HasErrors || password is null)
        {
            return Result<PasswordResetCompleted>.Failure(validation.Errors);
        }

        if (string.IsNullOrWhiteSpace(command.Token))
        {
            return Result<PasswordResetCompleted>.Failure(LinkInvalid);
        }

        var presentedHash = tokenHasher.Hash(command.Token);
        var player = await players.FindByResetTokenHash(presentedHash, ct);
        if (player is null)
        {
            return Result<PasswordResetCompleted>.Failure(LinkInvalid);
        }

        var newHash = await passwordHasher.HashAsync(password, ct);

        // Perder a corrida pelo token (consumo atomico, REQ-030) e o mesmo caso do link ja usado.
        try
        {
            var revoked = 0;
            var completed = await unitOfWork.ExecuteAsync(async innerCt =>
            {
                var now = time.GetUtcNow();
                var reset = player.ResetPassword(presentedHash, newHash, now);
                if (reset.IsFailure)
                {
                    return Result<PasswordResetCompleted>.Failure(reset.Errors);
                }

                await players.Save(player, innerCt);
                revoked = await sessions.RevokeAllFor(player.Id, SessionRevocationReason.PasswordReset, now, innerCt);
                return Result<PasswordResetCompleted>.Success(new PasswordResetCompleted());
            }, ct);

            if (completed.IsSuccess)
            {
                _events.PasswordResetCompleted(player.Id, revoked);
            }

            return completed;
        }
        catch (ConcurrencyConflictException)
        {
            return Result<PasswordResetCompleted>.Failure(LinkInvalid);
        }
    }
}
