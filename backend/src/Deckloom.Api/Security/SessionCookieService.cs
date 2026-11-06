using Deckloom.Accounts.Application;

namespace Deckloom.Api.Security;

// T-080: emissao e expiracao do cookie __Host-dl_session (design: Cookies). O prefixo __Host- exige
// Secure, Path=/ e ausencia de Domain; sem Max-Age o cookie dura a sessao do navegador, com "lembrar de mim" dura 30 dias.
public sealed class SessionCookieService
{
    public const string CookieName = "__Host-dl_session";

    private static readonly TimeSpan RememberMeLifetime = TimeSpan.FromDays(30);

    public void Issue(HttpContext context, AuthenticatedSession session, bool rememberMe)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);

        var options = BaseOptions();
        if (rememberMe)
        {
            options.MaxAge = RememberMeLifetime;
        }

        context.Response.Cookies.Append(CookieName, session.SessionToken, options);
    }

    public void Expire(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = BaseOptions();
        options.MaxAge = TimeSpan.Zero;
        options.Expires = DateTimeOffset.UnixEpoch;
        context.Response.Cookies.Append(CookieName, string.Empty, options);
    }

    private static CookieOptions BaseOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
    };
}
