using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class CompleteGoogleSignup(
    IPlayerRepository players,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    ISecureTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    ITermsVersionProvider termsVersionProvider,
    IAvatarPresetCatalog avatarPresetCatalog,
    IPendingGoogleAuthStore pendingStore,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private static readonly DomainError PendingNotFound = new("pending_not_found");
    private static readonly DomainError GoogleNotCompleted = new("google_not_completed");

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<AuthenticatedSession>> ExecuteAsync(
        CompleteGoogleSignupCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PendingToken))
        {
            return Result<AuthenticatedSession>.Failure(PendingNotFound);
        }

        var tokenHash = tokenHasher.Hash(command.PendingToken);
        if (await pendingStore.FindAsync(tokenHash, ct) is not PendingGoogleSignup pending)
        {
            return Result<AuthenticatedSession>.Failure(PendingNotFound);
        }

        var now = time.GetUtcNow();
        var validation = new ValidationErrorCollector();
        var displayName = validation.CollectDisplayNameAndTerms(
            command.DisplayName,
            termsVersionProvider,
            command.TermsVersion,
            command.AdultDeclaration,
            command.AcceptTerms,
            now);
        ReportMissingAdultDeclaration(validation, command);
        if (validation.HasErrors)
        {
            return Result<AuthenticatedSession>.Failure(validation.Errors);
        }

        var identity = ExternalIdentity.Create(ExternalIdentity.GoogleProvider, pending.GoogleSubject);
        if (identity.IsFailure)
        {
            return Result<AuthenticatedSession>.Failure(identity.Errors);
        }

        var defaultPreset = await avatarPresetCatalog.GetDefaultAsync(ct);
        var registration = Player.RegisterWithGoogle(
            pending.Email,
            identity.Value,
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
        (string, Session)? opened;
        try
        {
            opened = await unitOfWork.ExecuteAsync<(string, Session)?>(async innerCt =>
            {
                if (await pendingStore.ConsumeAsync(tokenHash, innerCt) is null)
                {
                    return null;
                }

                var rawToken = tokenGenerator.Generate();
                var started = Session.Start(player.Id, tokenHasher.Hash(rawToken), false, now);
                await players.Add(player, innerCt);
                await sessions.Add(started, innerCt);
                return (rawToken, started);
            }, ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<AuthenticatedSession>.Failure(GoogleNotCompleted);
        }

        if (opened is not var (token, session))
        {
            return Result<AuthenticatedSession>.Failure(PendingNotFound);
        }

        AccountsMetrics.PlayerRegistered("google");
        _events.PlayerRegistered(player.Id, "google");
        var summary = await new SessionSummaryFactory(avatarPresetCatalog).CreateAsync(player, ct);
        return Result<AuthenticatedSession>.Success(new AuthenticatedSession(token, session.ExpiresAt, summary));
    }

    // TermsAcceptance.Create para no primeiro erro; aqui os erros de campo saem agregados (contrato da API).
    private static void ReportMissingAdultDeclaration(ValidationErrorCollector validation, CompleteGoogleSignupCommand command)
    {
        var alreadyReported = validation.Errors.Any(e => e.Field == TermsAcceptance.AdultDeclarationField);
        if (!command.AdultDeclaration && !alreadyReported)
        {
            validation.Collect(Result<bool>.Failure(
                new DomainError(TermsAcceptance.RequiredCode, TermsAcceptance.AdultDeclarationField)));
        }
    }
}
