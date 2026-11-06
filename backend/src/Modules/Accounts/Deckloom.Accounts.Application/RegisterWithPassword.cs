using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class RegisterWithPassword(
    IPlayerRepository players,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ISecureTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    ITermsVersionProvider termsVersionProvider,
    IAvatarPresetCatalog avatarPresetCatalog,
    AccessThrottle throttle,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<AuthenticatedSession>> ExecuteAsync(
        RegisterWithPasswordCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var keys = await throttle.BeforeAttemptAsync(AccessAction.Registration, command.Email, command.Ip, ct);

        var now = time.GetUtcNow();
        var validation = new ValidationErrorCollector();
        var email = validation.Collect(Email.Create(command.Email), "email");
        var password = validation.CollectNewPassword(command.Password, "password");
        var displayName = validation.CollectDisplayNameAndTerms(
            command.DisplayName,
            termsVersionProvider,
            command.TermsVersion,
            command.AdultDeclaration,
            command.AcceptTerms,
            now);
        if (validation.HasErrors)
        {
            return Result<AuthenticatedSession>.Failure(validation.Errors);
        }

        if (await players.ExistsByEmail(email!, ct))
        {
            return await RefuseUnavailableEmailAsync(keys, ct);
        }

        var passwordHash = await passwordHasher.HashAsync(password!, ct);
        var defaultPreset = await avatarPresetCatalog.GetDefaultAsync(ct);

        var registration = Player.RegisterWithPassword(
            email!,
            passwordHash,
            displayName!,
            termsVersionProvider.CurrentVersion,
            command.TermsVersion,
            command.AcceptTerms,
            command.AdultDeclaration,
            now,
            defaultPreset.Id);
        if (registration.IsFailure)
        {
            return Result<AuthenticatedSession>.Failure(registration.Errors);
        }

        var player = registration.Value;
        string token;
        Session session;
        try
        {
            (token, session) = await unitOfWork.ExecuteAsync(async innerCt =>
            {
                var rawToken = tokenGenerator.Generate();
                var started = Session.Start(player.Id, tokenHasher.Hash(rawToken), false, now);
                await players.Add(player, innerCt);
                await sessions.Add(started, innerCt);
                return (rawToken, started);
            }, ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return await RefuseUnavailableEmailAsync(keys, ct);
        }

        AccountsMetrics.PlayerRegistered("password");
        _events.PlayerRegistered(player.Id, "password");
        var summary = await new SessionSummaryFactory(avatarPresetCatalog).CreateAsync(player, ct);
        return Result<AuthenticatedSession>.Success(new AuthenticatedSession(token, session.ExpiresAt, summary));
    }

    // Unico ponto que monta a recusa por email indisponivel; o evento so leva a chave de IP e fica fora da transacao.
    private async Task<Result<AuthenticatedSession>> RefuseUnavailableEmailAsync(
        AccessAttemptKeys keys, CancellationToken ct)
    {
        await throttle.RecordAsync(keys, ct);
        return Result<AuthenticatedSession>.Failure(new DomainError("unavailable", "email"));
    }
}
