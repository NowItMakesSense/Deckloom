using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class LoginWithPassword(
    IPlayerRepository players,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ISecureTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IAvatarPresetCatalog avatarPresetCatalog,
    AccessThrottle throttle,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private readonly CredentialVerifier _verifier = new(passwordHasher);
    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<AuthenticatedSession>> ExecuteAsync(
        LoginWithPasswordCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var keys = await throttle.BeforeAttemptAsync(AccessAction.Login, command.Email, command.Ip, ct);
        if (keys.BudgetExceeded)
        {
            AccountsMetrics.LoginBudgetExceeded();
            return Result<AuthenticatedSession>.Failure(AccessThrottle.TooManyAttempts);
        }

        // A duracao de processamento exclui o atraso deliberado: o cronometro comeca depois dele.
        var started = time.GetTimestamp();
        var result = await AttemptAsync(command, keys, ct);
        AccountsMetrics.LoginProcessed(time.GetElapsedTime(started));
        AccountsMetrics.LoginAttempted(result.IsSuccess);
        return result;
    }

    private async Task<Result<AuthenticatedSession>> AttemptAsync(
        LoginWithPasswordCommand command, AccessAttemptKeys keys, CancellationToken ct)
    {
        var emailResult = Email.Create(command.Email);
        var passwordResult = Password.Create(command.Password);

        var required = new ValidationErrorCollector();
        if (IsRequired(emailResult))
        {
            required.Collect(emailResult, "email");
        }

        if (IsRequired(passwordResult))
        {
            required.Collect(passwordResult, "password");
        }

        if (required.HasErrors)
        {
            await throttle.ReleaseAsync(keys);
            return Result<AuthenticatedSession>.Failure(required.Errors);
        }

        // Tamanho de senha invalido nunca coincide com um hash armazenado: recusa sem o hasher.
        if (passwordResult.IsFailure)
        {
            return await RefuseAsync(keys, null, ct);
        }

        var password = passwordResult.Value;
        var player = emailResult.IsSuccess ? await players.FindByEmail(emailResult.Value, ct) : null;
        var storedHash = player?.PasswordHash;
        if (!await _verifier.VerifyAsync(password, storedHash, ct) || player is null || storedHash is null)
        {
            return await RefuseAsync(keys, player?.Id, ct);
        }

        var now = time.GetUtcNow();
        var newHash = passwordHasher.NeedsRehash(storedHash)
            ? await passwordHasher.HashAsync(password, ct)
            : null;

        var (token, session) = await unitOfWork.ExecuteAsync(async innerCt =>
        {
            var rawToken = tokenGenerator.Generate();
            var started = Session.Start(player.Id, tokenHasher.Hash(rawToken), command.RememberMe, now);
            await sessions.Add(started, innerCt);
            await throttle.ClearAccountFailuresAsync(keys);
            if (newHash is not null)
            {
                RehashOrThrow(player, newHash);
                await players.Save(player, innerCt);
            }

            return (rawToken, started);
        }, ct);

        _events.LoginSucceeded(player.Id, session.Id, command.RememberMe);
        var summary = await new SessionSummaryFactory(avatarPresetCatalog).CreateAsync(player, ct);
        return Result<AuthenticatedSession>.Success(new AuthenticatedSession(token, session.ExpiresAt, summary));
    }

    private static bool IsRequired<T>(Result<T> result) =>
        result.IsFailure && result.Errors.Any(e => e.Code == Player.RequiredCode);

    private static void RehashOrThrow(Player player, PasswordHash newHash)
    {
        if (player.RehashPassword(newHash).IsFailure)
        {
            throw new InvalidOperationException("A verified password login must have a local password to rehash.");
        }
    }

    private async Task<Result<AuthenticatedSession>> RefuseAsync(
        AccessAttemptKeys keys, PlayerId? knownPlayer, CancellationToken ct)
    {
        _events.LoginFailed("invalid_credentials", knownPlayer);
        // O evento provisorio de BeforeAttemptAsync ja e a falha (SEC-01): nao grava um segundo.
        return Result<AuthenticatedSession>.Failure(new DomainError("invalid_credentials"));
    }
}
