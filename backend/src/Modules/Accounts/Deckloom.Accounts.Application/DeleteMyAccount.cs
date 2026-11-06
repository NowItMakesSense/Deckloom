using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed record DeleteMyAccountCommand(AuthenticatedPlayer? Caller, string? Password, bool Confirmed, string Ip);

public sealed class DeleteMyAccount(
    IPlayerRepository players,
    ISessionRepository sessions,
    IPlayerDecksPurger decksPurger,
    IAvatarStorage avatarStorage,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    AccessThrottle throttle,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly DomainError IncorrectPassword = new("incorrect_password");

    private readonly CredentialVerifier _verifier = new(passwordHasher);
    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<AccountDeleted>> ExecuteAsync(DeleteMyAccountCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Caller is null)
        {
            return Result<AccountDeleted>.Failure(Unauthenticated);
        }

        var player = await players.GetById(command.Caller.PlayerId, ct);
        if (player is null)
        {
            return Result<AccountDeleted>.Failure(Unauthenticated);
        }

        // O orcamento por conta vale so para o fluxo com senha (ADR-0005 E7); conta Google nao tem senha a adivinhar.
        var keys = player.PasswordHash is null
            ? throttle.KeysWithoutAttempt(AccessAction.AccountDeletion, player.Email.Value, command.Ip)
            : await throttle.BeforeAttemptAsync(AccessAction.AccountDeletion, player.Email.Value, command.Ip, ct);
        if (keys.BudgetExceeded)
        {
            return Result<AccountDeleted>.Failure(AccessThrottle.TooManyAttempts);
        }

        var reauthentication = player.PasswordHash is null
            ? await ReauthenticateWithGoogleAsync(command.Caller, ct)
            : await ReauthenticateWithPasswordAsync(player, command, keys, ct);
        if (reauthentication.IsFailure)
        {
            // Senha incorreta e a unica falha que conta (o provisorio ja e ela); o resto nao e tentativa de senha.
            if (!reauthentication.Errors.Contains(IncorrectPassword))
            {
                await throttle.ReleaseAsync(keys);
            }

            LogRefusal(player.Id, reauthentication.Errors);
            return Result<AccountDeleted>.Failure(reauthentication.Errors);
        }

        var now = time.GetUtcNow();
        var deleted = player.Delete(command.Confirmed, reauthentication.Value, now);
        if (deleted.IsFailure)
        {
            await throttle.ReleaseAsync(keys);
            LogRefusal(player.Id, deleted.Errors);
            return Result<AccountDeleted>.Failure(deleted.Errors);
        }

        await RemoveAccountAsync(player, keys, now, ct);
        AccountsMetrics.AccountDeleted();

        return Result<AccountDeleted>.Success(new AccountDeleted());
    }

    // Evento 1018: so os motivos fechados do catalogo; outros erros (ex.: sessao invalida) nao geram o evento.
    private void LogRefusal(PlayerId playerId, IReadOnlyList<DomainError> errors)
    {
        var reason = errors.Select(RefusalReason).FirstOrDefault(r => r is not null);
        if (reason is not null)
        {
            _events.AccountDeletionRejected(playerId, reason);
        }
    }

    private static string? RefusalReason(DomainError error) => error switch
    {
        { Code: "incorrect_password" } => "incorrect_password",
        { Code: Player.ReauthRequiredCode } => "reauth_required",
        { Code: Player.ReauthExpiredCode } => "reauth_expired",
        { Field: "confirm" } => "not_confirmed",
        _ => null,
    };

    private async Task<Result<Reauthentication>> ReauthenticateWithPasswordAsync(
        Player player,
        DeleteMyAccountCommand command,
        AccessAttemptKeys keys,
        CancellationToken ct)
    {
        var passwordResult = Password.Create(command.Password);
        var validation = new ValidationErrorCollector();
        if (!command.Confirmed)
        {
            validation.Collect(Result<bool>.Failure(new DomainError(Player.RequiredCode)), "confirm");
        }

        if (IsRequiredOrTooLong(passwordResult))
        {
            validation.Collect(passwordResult, "password");
        }

        if (validation.HasErrors)
        {
            return Result<Reauthentication>.Failure(validation.Errors);
        }

        // Tamanho abaixo do minimo nunca coincide com um hash armazenado: recusa sem o hasher.
        if (passwordResult.IsFailure || !await _verifier.VerifyAsync(passwordResult.Value, player.PasswordHash, ct))
        {
            return Result<Reauthentication>.Failure(IncorrectPassword);
        }

        return Result<Reauthentication>.Success(new PasswordProof(player.Id));
    }

    // A Session e lida na hora da exclusao: a reautenticacao pode ter ocorrido depois de o Caller ser montado.
    private async Task<Result<Reauthentication>> ReauthenticateWithGoogleAsync(AuthenticatedPlayer caller, CancellationToken ct)
    {
        var session = await sessions.FindById(caller.SessionId, ct);
        return session is null
            ? Result<Reauthentication>.Failure(Unauthenticated)
            : Result<Reauthentication>.Success(session.ToRecentGoogleLogin());
    }

    private async Task RemoveAccountAsync(Player player, AccessAttemptKeys keys, DateTimeOffset now, CancellationToken ct)
    {
        var customAvatarKey = (player.Avatar as CustomAvatar)?.StorageKey;
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await decksPurger.PurgeAsync(player.Id, innerCt);
            await throttle.ClearAccountFailuresAsync(keys);
            await new PreviousAvatarRemover(avatarStorage).RemoveAsync(customAvatarKey, innerCt);
            await sessions.RevokeAllFor(player.Id, SessionRevocationReason.AccountDeletion, now, innerCt);
            await players.Remove(player, innerCt);
        }, ct);
    }

    private static bool IsRequiredOrTooLong(Result<Password> result) =>
        result.IsFailure && result.Errors.Any(e => e.Code is Password.RequiredCode or Password.TooLongCode);
}
