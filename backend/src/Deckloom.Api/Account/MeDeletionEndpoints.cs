using Deckloom.Accounts.Application;
using Deckloom.Api.Auth;
using Deckloom.Api.Http;
using Deckloom.Api.Problems;
using Deckloom.Api.Security;

namespace Deckloom.Api.Account;

public sealed record DeleteAccountRequest(bool Confirm, string? Password);

// Porta de log de auditoria da conta: so identificadores, nunca email ou senha.
public interface IAccountsAuditLog
{
    void AccountDeleted(Guid playerId);

    void CsrfRejected(string path);
}

// T-090: exclusao da propria conta. Nenhuma rota recebe PlayerId; o dono vem sempre da sessao.
public static class MeDeletionEndpoints
{
    public static IEndpointRouteBuilder MapMeDeletionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/v1/me/deletion", DeleteAsync);
        return routes;
    }

    private static async Task<IResult> DeleteAsync(
        HttpContext context,
        AuthenticateSession authenticate,
        DeleteMyAccount deleteAccount,
        SessionCookieService cookies,
        IAccountsAuditLog auditLog,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var caller = await authenticate.ExecuteAsync(
            new AuthenticateSessionCommand(context.Request.Cookies[SessionCookieService.CookieName]), ct);
        if (caller.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(caller.Errors);
        }

        var request = await AuthEndpoints.ReadJsonAsync<DeleteAccountRequest>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var ip = ClientIpKey.From(context);
        var result = await deleteAccount.ExecuteAsync(
            new DeleteMyAccountCommand(caller.Value, request.Value!.Password, request.Value.Confirm, ip), ct);
        if (result.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(result.Errors);
        }

        auditLog.AccountDeleted(caller.Value.PlayerId.Value);
        cookies.Expire(context);
        return Results.NoContent();
    }
}
