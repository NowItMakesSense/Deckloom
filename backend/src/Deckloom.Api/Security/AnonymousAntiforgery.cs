using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;

namespace Deckloom.Api.Security;

// O token anti-CSRF e emitido antes do login e reutilizado depois (o front guarda um so token por navegador).
// O antiforgery do ASP.NET amarra o token ao usuario autenticado; aqui o vinculo e feito so ao cookie
// __Host-dl_csrf (par cookie + cabecalho), avaliando o token sempre como anonimo, para que o mesmo token valha
// antes e depois do login e do logout.
internal static class AnonymousAntiforgery
{
    public static async Task<bool> IsRequestValidAsync(IAntiforgery antiforgery, HttpContext context)
    {
        var original = context.User;
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        try
        {
            return await antiforgery.IsRequestValidAsync(context);
        }
        finally
        {
            context.User = original;
        }
    }

    public static string GetAndStoreRequestToken(IAntiforgery antiforgery, HttpContext context)
    {
        var original = context.User;
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        try
        {
            return antiforgery.GetAndStoreTokens(context).RequestToken ?? string.Empty;
        }
        finally
        {
            context.User = original;
        }
    }
}
