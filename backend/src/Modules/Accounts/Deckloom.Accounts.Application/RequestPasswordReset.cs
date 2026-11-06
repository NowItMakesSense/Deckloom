using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class RequestPasswordReset(
    IPlayerRepository players,
    IUnitOfWork unitOfWork,
    ISecureTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IPasswordResetNotifier notifier,
    AccessThrottle throttle,
    PasswordResetOptions options,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private const string ResetPath = "/redefinir-senha#token=";

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<PasswordResetRequestAccepted>> ExecuteAsync(
        RequestPasswordResetCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var keys = await throttle.BeforeAttemptAsync(AccessAction.PasswordResetRequest, command.Email, command.Ip, ct);


        // Acima do orcamento por conta: o mesmo 202, sem token nem email (nao revela existencia; SEC-01, SEC-03).
        if (keys.BudgetExceeded)
        {
            AccountsMetrics.PasswordResetBudgetExceeded();
            return Result<PasswordResetRequestAccepted>.Success(new PasswordResetRequestAccepted());
        }

        var validation = new ValidationErrorCollector();
        var email = validation.Collect(Email.Create(command.Email), "email");
        if (validation.HasErrors || email is null)
        {
            await throttle.ReleaseAsync(keys);
            return Result<PasswordResetRequestAccepted>.Failure(validation.Errors);
        }

        // O evento provisorio de BeforeAttemptAsync fica: toda solicitacao valida conta, exista a conta ou nao.

        var player = await players.FindByEmail(email, ct);
        if (player is not null && player.PasswordHash is not null)
        {
            var rawToken = await IssueTokenAsync(player, ct);
            AccountsMetrics.PasswordResetOutcome("token_issued");
            _events.PasswordResetRequested("token_issued", player.Id);
            await TryNotifyAsync(player.Id, email, rawToken, ct);
        }
        else
        {
            AccountsMetrics.PasswordResetOutcome("no_eligible_account");
            _events.PasswordResetRequested("no_eligible_account", player?.Id);
        }

        return Result<PasswordResetRequestAccepted>.Success(new PasswordResetRequestAccepted());
    }

    private Task<string> IssueTokenAsync(Player player, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(async innerCt =>
        {
            var rawToken = tokenGenerator.Generate();
            if (player.IssuePasswordReset(tokenHasher.Hash(rawToken), time.GetUtcNow()).IsFailure)
            {
                throw new InvalidOperationException("A player with a local password must accept a reset token.");
            }

            await players.Save(player, innerCt);
            return rawToken;
        }, ct);

    private async Task TryNotifyAsync(PlayerId playerId, Email email, string rawToken, CancellationToken ct)
    {
        var link = $"{options.PublicBaseUrl.TrimEnd('/')}{ResetPath}{rawToken}";
        try
        {
            await notifier.NotifyAsync(playerId, email, link, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Falha de envio nao pode distinguir a resposta (anti-enumeracao).
        catch (Exception)
#pragma warning restore CA1031
        {
            AccountsMetrics.ResetEmailFailed();
            _events.PasswordResetEmailFailed(playerId, 1);
        }
    }
}
