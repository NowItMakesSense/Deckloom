using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class LinkGoogleWithPassword(
    IPlayerRepository players,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ISecureTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IAvatarPresetCatalog avatarPresetCatalog,
    IPendingGoogleAuthStore pendingStore,
    AccessThrottle throttle,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private static readonly DomainError PendingNotFound = new("pending_not_found");
    private static readonly DomainError IncorrectPassword = new("incorrect_password");
    private static readonly DomainError GoogleNotCompleted = new("google_not_completed");

    private readonly CredentialVerifier _verifier = new(passwordHasher);
    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<AuthenticatedSession>> ExecuteAsync(
        LinkGoogleWithPasswordCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PendingToken))
        {
            return Result<AuthenticatedSession>.Failure(PendingNotFound);
        }

        var tokenHash = tokenHasher.Hash(command.PendingToken);
        if (await pendingStore.FindAsync(tokenHash, ct) is not PendingGoogleLink pending)
        {
            return Result<AuthenticatedSession>.Failure(PendingNotFound);
        }

        var keys = await throttle.BeforeAttemptAsync(AccessAction.GoogleLink, pending.Email.Value, command.Ip, ct);

        if (keys.BudgetExceeded)
        {
            AccountsMetrics.GoogleBudgetExceeded();
            return Result<AuthenticatedSession>.Failure(AccessThrottle.TooManyAttempts);
        }

        var validation = new ValidationErrorCollector();
        var password = validation.Collect(Password.Create(command.Password), "password");
        if (validation.HasErrors)
        {
            await throttle.ReleaseAsync(keys);
            return Result<AuthenticatedSession>.Failure(validation.Errors);
        }

        var player = await players.GetById(pending.TargetPlayerId, ct);
        if (player is null)
        {
            await throttle.ReleaseAsync(keys);
            return Result<AuthenticatedSession>.Failure(PendingNotFound);
        }

        // Falha de senha: o evento provisorio de BeforeAttemptAsync ja e a falha (SEC-01).
        if (!await _verifier.VerifyAsync(password!, player.PasswordHash, ct))
        {
            _events.GoogleLinkRejected(player.Id, "incorrect_password");
            return Result<AuthenticatedSession>.Failure(IncorrectPassword);
        }

        // Senha correta: o vinculo nao e falha, entao so o proprio evento provisorio sai (os demais da conta ficam).
        await throttle.ReleaseAsync(keys);

        var identity = ExternalIdentity.Create(ExternalIdentity.GoogleProvider, pending.GoogleSubject);
        if (identity.IsFailure)
        {
            return Result<AuthenticatedSession>.Failure(identity.Errors);
        }

        var now = time.GetUtcNow();
        Result<Player>? linkFailure = null;
        (string, Session)? opened;
        try
        {
            opened = await unitOfWork.ExecuteAsync<(string, Session)?>(async innerCt =>
            {
                var linked = player.LinkGoogle(identity.Value, pending.Email, new PasswordProof(player.Id));
                if (linked.IsFailure)
                {
                    linkFailure = linked;
                    return null;
                }

                if (await pendingStore.ConsumeAsync(tokenHash, innerCt) is null)
                {
                    return null;
                }

                var rawToken = tokenGenerator.Generate();
                var started = Session.Start(player.Id, tokenHasher.Hash(rawToken), false, now);
                await players.Save(player, innerCt);
                await sessions.Add(started, innerCt);
                return (rawToken, started);
            }, ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<AuthenticatedSession>.Failure(GoogleNotCompleted);
        }

        if (linkFailure is not null)
        {
            return Result<AuthenticatedSession>.Failure(linkFailure.Errors);
        }

        if (opened is not var (token, session))
        {
            return Result<AuthenticatedSession>.Failure(PendingNotFound);
        }

        _events.GoogleLinked(player.Id);
        var summary = await new SessionSummaryFactory(avatarPresetCatalog).CreateAsync(player, ct);
        return Result<AuthenticatedSession>.Success(new AuthenticatedSession(token, session.ExpiresAt, summary));
    }
}
