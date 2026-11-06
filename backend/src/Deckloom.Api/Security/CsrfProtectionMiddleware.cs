using Deckloom.Api.Account;
using Deckloom.Api.Problems;
using Microsoft.AspNetCore.Antiforgery;

namespace Deckloom.Api.Security;

// Mutacoes (POST/PUT/DELETE) exigem X-CSRF-Token valido para o cookie da sessao e, se houver
// cabecalho Origin, que ele seja igual a Auth:PublicOrigin (design: CSRF). Nunca loga o token.
public sealed class CsrfProtectionMiddleware(RequestDelegate next, IAntiforgery antiforgery, IConfiguration configuration)
{
    public const string CookieName = "__Host-dl_csrf";
    public const string HeaderName = "X-CSRF-Token";

    public async Task InvokeAsync(HttpContext context, IAccountsAuditLog auditLog)
    {
        if (IsMutation(context.Request.Method) && !await IsAllowedAsync(context))
        {
            // Somente o caminho: a query string pode carregar valores sensiveis.
            auditLog.CsrfRejected(context.Request.Path.Value ?? string.Empty);
            await DomainErrorProblemMapper.CsrfFailedResult().ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool IsMutation(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method);

    private async Task<bool> IsAllowedAsync(HttpContext context)
    {
        if (!IsOriginAllowed(context.Request))
        {
            return false;
        }

        try
        {
            return await AnonymousAntiforgery.IsRequestValidAsync(antiforgery, context);
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private bool IsOriginAllowed(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var origin) || origin.Count == 0)
        {
            return true;
        }

        var publicOrigin = configuration["Auth:PublicOrigin"]?.TrimEnd('/');
        return !string.IsNullOrEmpty(publicOrigin)
            && origin.Count == 1
            && string.Equals(origin[0], publicOrigin, StringComparison.Ordinal);
    }
}
