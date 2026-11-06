using System.Security.Cryptography;
using System.Text;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

// Conteudo do cookie de correlacao OAuth, ja decifrado pela API.
public enum GoogleAuthIntent
{
    SignIn,
    Reauth,
}

public sealed record GoogleOAuthCorrelation(
    string State,
    string Nonce,
    string CodeVerifier,
    GoogleAuthIntent Intent = GoogleAuthIntent.SignIn,
    PlayerId? PlayerId = null);

// SessionToken: token bruto do cookie de sessao; so e usado no intent Reauth.
public sealed record HandleGoogleCallbackCommand(
    string? Code,
    string? State,
    string? Error,
    GoogleOAuthCorrelation? Correlation,
    string? SessionToken = null);

// Uniao fechada do retorno do Google.
public abstract record GoogleCallbackOutcome;

public sealed record GoogleCallbackOk(AuthenticatedSession Session) : GoogleCallbackOutcome;

// PendingToken: token opaco em claro (a API grava no cookie `__Host-dl_pending`); so o hash vai ao store.
public sealed record GoogleCallbackConclude(string PendingToken) : GoogleCallbackOutcome;

public sealed record GoogleCallbackLink(string PendingToken) : GoogleCallbackOutcome;

public sealed record GoogleCallbackFailed : GoogleCallbackOutcome;

public sealed record GoogleCallbackReauthenticated : GoogleCallbackOutcome;

public sealed record GoogleCallbackReauthFailed : GoogleCallbackOutcome;

public sealed class HandleGoogleCallback(
    IPlayerRepository players,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IGoogleIdentityVerifier google,
    ISecureTokenGenerator tokenGenerator,
    ITokenHasher tokenHasher,
    IAvatarPresetCatalog avatarPresetCatalog,
    IPendingGoogleAuthStore pendingStore,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private const string AuthorizeStage = "authorize";
    private const string CallbackStage = "callback";
    private const string TokenStage = "token";
    private const string IdTokenStage = "id_token";

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<GoogleCallbackOutcome>> ExecuteAsync(
        HandleGoogleCallbackCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Correlation?.Intent == GoogleAuthIntent.Reauth)
        {
            return await ReauthenticateAsync(command, ct);
        }

        if (!IsAcceptable(command))
        {
            return Failed(FailureStage(command), RejectionReason(command));
        }

        // Falha do verificador (token) ja e registrada pelo adapter com estagio e motivo reais (sem duplicar aqui).
        var identity = await CompleteAtGoogleAsync(command.Code!, command.Correlation!, ct);
        if (identity is null)
        {
            return Failed(TokenStage);
        }

        if (!identity.EmailVerified)
        {
            return Failed(IdTokenStage, GoogleAuthFailureReasons.UnverifiedEmail);
        }

        var player = await players.FindByGoogleSubject(identity.Subject, ct);
        if (player is not null)
        {
            var authenticated = await OpenSessionAsync(player, ct);
            AccountsMetrics.GoogleSucceeded("signed_in");
            _events.GoogleAuthCompleted("signed_in", player.Id);
            return Result<GoogleCallbackOutcome>.Success(new GoogleCallbackOk(authenticated));
        }

        return await HandleUnknownSubjectAsync(identity, ct);
    }

    // Reautenticacao para excluir a conta: nenhuma sessao nova nem pendencia; toda falha vira reautenticacao-falhou.
    private async Task<Result<GoogleCallbackOutcome>> ReauthenticateAsync(
        HandleGoogleCallbackCommand command, CancellationToken ct)
    {
        var correlation = command.Correlation!;
        var now = time.GetUtcNow();

        var session = await new SessionLookup(sessions, tokenHasher).FindAsync(command.SessionToken, ct);
        if (!IsAcceptable(command))
        {
            return ReauthFailed(FailureStage(command), RejectionReason(command));
        }

        // Callback aceitavel, mas sem sessao valida do mesmo jogador: o motivo e a sessao, nao o subject.
        if (session is null || !session.IsValid(now) || correlation.PlayerId != session.PlayerId)
        {
            return ReauthFailed(CallbackStage, GoogleAuthFailureReasons.SessionInvalid);
        }

        var identity = await CompleteAtGoogleAsync(command.Code!, correlation, ct);
        if (identity is null)
        {
            return ReauthFailed(TokenStage);
        }

        if (!identity.EmailVerified)
        {
            return ReauthFailed(IdTokenStage, GoogleAuthFailureReasons.UnverifiedEmail);
        }

        if (identity.AuthTime is not { } authTime || now - authTime > Reauthentication.Window)
        {
            return ReauthFailed(IdTokenStage, GoogleAuthFailureReasons.ProviderError);
        }

        var player = await players.GetById(correlation.PlayerId!.Value, ct);
        var presented = ExternalIdentity.Create(ExternalIdentity.GoogleProvider, identity.Subject);
        if (player?.ExternalIdentity is not { } linked || presented.IsFailure)
        {
            return ReauthFailed(IdTokenStage, GoogleAuthFailureReasons.SubjectMismatch);
        }

        var marked = await unitOfWork.ExecuteAsync(async innerCt =>
        {
            if (session.MarkReauthenticated(linked, presented.Value, now).IsFailure)
            {
                return false;
            }

            await sessions.Save(session, innerCt);
            return true;
        }, ct);

        if (!marked)
        {
            return ReauthFailed(IdTokenStage, GoogleAuthFailureReasons.SubjectMismatch);
        }

        AccountsMetrics.GoogleSucceeded("reauthenticated");
        _events.GoogleAuthCompleted("reauthenticated", player.Id);
        return Result<GoogleCallbackOutcome>.Success(new GoogleCallbackReauthenticated());
    }

    // Subject sem conta vinculada: concluir cadastro (sem conta com o e-mail) ou vincular
    // (conta sem identidade Google). Conta que ja tem outra identidade Google falha (REQ-009).
    private async Task<Result<GoogleCallbackOutcome>> HandleUnknownSubjectAsync(
        GoogleVerifiedIdentity identity, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var sameEmail = await players.FindByEmail(identity.Email, ct);

        if (sameEmail is null)
        {
            var token = await StorePendingAsync(
                hash => PendingGoogleSignup.Create(hash, identity.Subject, identity.Email, identity.Name, now), ct);
            AccountsMetrics.GoogleSucceeded("pending_signup");
            _events.GoogleAuthCompleted("pending_signup", null);
            return Result<GoogleCallbackOutcome>.Success(new GoogleCallbackConclude(token));
        }

        if (sameEmail.ExternalIdentity is not null)
        {
            return Failed(IdTokenStage, GoogleAuthFailureReasons.SubjectMismatch);
        }

        var linkToken = await StorePendingAsync(
            hash => PendingGoogleLink.Create(hash, identity.Subject, identity.Email, sameEmail.Id, now), ct);
        AccountsMetrics.GoogleSucceeded("pending_link");
        _events.GoogleAuthCompleted("pending_link", sameEmail.Id);
        return Result<GoogleCallbackOutcome>.Success(new GoogleCallbackLink(linkToken));
    }

    // Gera o token opaco, grava so o hash (com TTL de PendingGoogleAuth.Ttl) e devolve o token em claro.
    private async Task<string> StorePendingAsync(Func<TokenHash, PendingGoogleAuth> create, CancellationToken ct)
    {
        return await unitOfWork.ExecuteAsync(async innerCt =>
        {
            var rawToken = tokenGenerator.Generate();
            await pendingStore.AddAsync(create(tokenHasher.Hash(rawToken)), innerCt);
            return rawToken;
        }, ct);
    }

    // Cancelamento no Google (parametro error, ex.: access_denied) e falha de autorizacao; o resto e callback invalido.
    private static string FailureStage(HandleGoogleCallbackCommand command) =>
        string.IsNullOrEmpty(command.Error) ? CallbackStage : AuthorizeStage;

    // Motivo (codigo fechado do catalogo) da recusa do callback: cancelamento, erro do provedor ou state invalido.
    private static string RejectionReason(HandleGoogleCallbackCommand command)
    {
        if (!string.IsNullOrEmpty(command.Error))
        {
            return command.Error == "access_denied" ? GoogleAuthFailureReasons.Cancelled : GoogleAuthFailureReasons.ProviderError;
        }

        return string.IsNullOrEmpty(command.Code) ? GoogleAuthFailureReasons.ProviderError : GoogleAuthFailureReasons.StateMismatch;
    }

    private static bool IsAcceptable(HandleGoogleCallbackCommand command) =>
        string.IsNullOrEmpty(command.Error)
        && !string.IsNullOrEmpty(command.Code)
        && command.Correlation is not null
        && !string.IsNullOrEmpty(command.State)
        && StateMatches(command.State, command.Correlation.State);

    private static bool StateMatches(string received, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(expected));

    private async Task<GoogleVerifiedIdentity?> CompleteAtGoogleAsync(
        string code, GoogleOAuthCorrelation correlation, CancellationToken ct)
    {
        try
        {
            return await google.CompleteAsync(code, correlation.CodeVerifier, correlation.Nonce, ct);
        }
        catch (GoogleVerificationFailedException)
        {
            return null;
        }
    }

    private async Task<AuthenticatedSession> OpenSessionAsync(Player player, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var (token, session) = await unitOfWork.ExecuteAsync(async innerCt =>
        {
            var rawToken = tokenGenerator.Generate();
            var started = Session.Start(player.Id, tokenHasher.Hash(rawToken), false, now);
            await sessions.Add(started, innerCt);
            return (rawToken, started);
        }, ct);

        var summary = await new SessionSummaryFactory(avatarPresetCatalog).CreateAsync(player, ct);
        return new AuthenticatedSession(token, session.ExpiresAt, summary);
    }

    // reason nulo: a falha veio do verificador, cujo adapter ja registrou o evento 1010 (evita contagem dupla).
    private Result<GoogleCallbackOutcome> ReauthFailed(string stage, string? reason = null)
    {
        AccountsMetrics.GoogleFailed(stage);
        LogFailure(stage, reason);
        return Result<GoogleCallbackOutcome>.Success(new GoogleCallbackReauthFailed());
    }

    private Result<GoogleCallbackOutcome> Failed(string stage, string? reason = null)
    {
        AccountsMetrics.GoogleFailed(stage);
        LogFailure(stage, reason);
        return Result<GoogleCallbackOutcome>.Success(new GoogleCallbackFailed());
    }

    private void LogFailure(string stage, string? reason)
    {
        if (reason is not null)
        {
            _events.GoogleAuthFailed(stage, reason);
        }
    }
}
