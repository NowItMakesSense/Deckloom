using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Api.Http;
using Deckloom.Api.Problems;
using Deckloom.Api.Security;

namespace Deckloom.Api.Auth;

// T-085: GET /auth/google/start e /auth/google/callback. O retorno ao front e sempre a rota interna fixa
// /entrar/google/retorno?resultado=<ramo>, sem dados sensiveis. R-34: no signin o returnTo do start e validado (SafeReturnTo), viaja no cookie de correlacao
// e volta como `retorno` so no ramo ok; no reauth e ignorado.
public sealed record PendingResponse(string Kind, string Email, string? SuggestedDisplayName);

public sealed record CompleteSignupRequest(string? DisplayName, bool AcceptTerms, string? TermsVersion, bool AdultDeclaration);

public sealed record LinkGoogleWithPasswordRequest(string? Password);

public static partial class GoogleEndpoints
{
    public const string ReturnRoute = "/entrar/google/retorno";

    public static IEndpointRouteBuilder MapGoogleEndpoints(this IEndpointRouteBuilder routes)
    {
        var google = routes.MapGroup("/api/v1/auth/google");

        google.MapGet("/start", StartAsync);
        google.MapGet("/callback", CallbackAsync);
        google.MapGet("/pending", GetPendingAsync);
        google.MapDelete("/pending", CancelPendingAsync);
        google.MapPost("/complete-signup", CompleteSignupAsync);
        google.MapPost("/link", LinkAsync);

        return routes;
    }

    private static async Task<IResult> StartAsync(
        HttpContext context,
        string? intent,
        string? returnTo,
        IGoogleIdentityVerifier google,
        AuthenticateSession authenticate,
        ISecureTokenGenerator tokens,
        GoogleCookieService cookies,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var reauth = string.Equals(intent, "reauth", StringComparison.Ordinal);
        PlayerId? playerId = null;
        if (reauth)
        {
            var token = context.Request.Cookies[SessionCookieService.CookieName];
            var session = await authenticate.ExecuteAsync(new AuthenticateSessionCommand(token), ct);
            if (session.IsFailure)
            {
                // Navegacao de topo: o contrato manda voltar a pagina de retorno, nunca um JSON 401.
                return Return("falhou");
            }

            playerId = session.Value.PlayerId;
        }

        var correlation = new GoogleOAuthCorrelation(
            tokens.Generate(),
            tokens.Generate(),
            tokens.Generate(),
            reauth ? GoogleAuthIntent.Reauth : GoogleAuthIntent.SignIn,
            playerId);
        Uri url;
        try
        {
            url = await google.BuildAuthorizationUrlAsync(
                correlation.State, correlation.Nonce, ChallengeOf(correlation.CodeVerifier), correlation.Intent, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // R-33: discovery do Google indisponivel. Navegacao de topo: volta a pagina de retorno, nunca um JSON 500.
            LogDiscoveryFailed(loggers.CreateLogger(typeof(GoogleEndpoints).FullName!), ex.GetType().Name);
            return Return("falhou");
        }

        cookies.IssueOAuth(context, correlation, reauth ? null : SafeReturnTo.Validate(returnTo));
        return Results.Redirect(url.AbsoluteUri);
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext context,
        string? code,
        string? state,
        string? error,
        HandleGoogleCallback handle,
        SessionCookieService sessionCookies,
        GoogleCookieService cookies,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var oauth = cookies.ReadOAuth(context);
        var correlation = oauth?.Correlation;
        cookies.ExpireOAuth(context); // uso unico, em qualquer desfecho

        var result = await handle.ExecuteAsync(
            new HandleGoogleCallbackCommand(
                code, state, error, correlation, context.Request.Cookies[SessionCookieService.CookieName]),
            ct);
        if (result.IsFailure)
        {
            return Return("falhou");
        }

        switch (result.Value)
        {
            case GoogleCallbackOk ok:
                sessionCookies.Issue(context, ok.Session, rememberMe: false);
                return Return("ok", oauth?.ReturnTo);
            case GoogleCallbackConclude conclude:
                cookies.IssuePending(context, conclude.PendingToken);
                return Return("concluir");
            case GoogleCallbackLink link:
                cookies.IssuePending(context, link.PendingToken);
                return Return("vincular");
            case GoogleCallbackReauthenticated:
                return Return("reautenticado");
            case GoogleCallbackReauthFailed:
                return Return("reautenticacao-falhou");
            default:
                return Return("falhou");
        }
    }

    private static async Task<IResult> GetPendingAsync(
        HttpContext context, GetPendingGoogleAuth getPending, GoogleCookieService cookies, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var result = await getPending.ExecuteAsync(new GetPendingGoogleAuthCommand(cookies.ReadPending(context)), ct);
        if (result.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(result.Errors);
        }

        var view = result.Value;
        var kind = view.Kind == PendingGoogleAuthKind.Link ? "link" : "signup";
        return Results.Json(
            new PendingResponse(kind, view.Email, view.SuggestedDisplayName), AuthEndpoints.JsonOptions);
    }

    private static async Task<IResult> CancelPendingAsync(
        HttpContext context, CancelPendingGoogleAuth cancel, GoogleCookieService cookies, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        await cancel.ExecuteAsync(new CancelPendingGoogleAuthCommand(cookies.ReadPending(context)), ct);
        cookies.ExpirePending(context);
        return Results.NoContent();
    }

    private static async Task<IResult> CompleteSignupAsync(
        HttpContext context,
        CompleteGoogleSignup complete,
        SessionCookieService sessionCookies,
        GoogleCookieService cookies,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var request = await AuthEndpoints.ReadJsonAsync<CompleteSignupRequest>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var body = request.Value!;
        var result = await complete.ExecuteAsync(
            new CompleteGoogleSignupCommand(
                cookies.ReadPending(context), body.DisplayName, body.AcceptTerms, body.TermsVersion, body.AdultDeclaration),
            ct);
        if (result.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(result.Errors);
        }

        sessionCookies.Issue(context, result.Value, rememberMe: false);
        cookies.ExpirePending(context);
        return Results.Json(
            AuthEndpoints.ToResponse(result.Value.Summary), AuthEndpoints.JsonOptions, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LinkAsync(
        HttpContext context,
        LinkGoogleWithPassword link,
        SessionCookieService sessionCookies,
        GoogleCookieService cookies,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var request = await AuthEndpoints.ReadJsonAsync<LinkGoogleWithPasswordRequest>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var ip = ClientIpKey.From(context);
        var result = await link.ExecuteAsync(
            new LinkGoogleWithPasswordCommand(cookies.ReadPending(context), request.Value!.Password, ip), ct);
        if (result.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(result.Errors);
        }

        sessionCookies.Issue(context, result.Value, rememberMe: false);
        cookies.ExpirePending(context);
        return Results.Json(AuthEndpoints.ToResponse(result.Value.Summary), AuthEndpoints.JsonOptions);
    }

    private static IResult Return(string resultado, string? retorno = null) =>
        Results.Redirect(
            retorno is null
                ? $"{ReturnRoute}?resultado={resultado}"
                : $"{ReturnRoute}?resultado={resultado}&retorno={Uri.EscapeDataString(retorno)}");

    // PKCE S256: base64url(SHA-256(code_verifier)) sem padding.
    private static string ChallengeOf(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // So o nome do tipo: a mensagem de bibliotecas HTTP pode carregar URL ou dados da requisicao.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Google start failed: discovery unavailable: {exceptionType}")]
    private static partial void LogDiscoveryFailed(ILogger logger, string exceptionType);
}
