using System.Security.Cryptography;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Deckloom.Api.Security;

// T-085: cookies do fluxo Google (design: Cookies). `__Host-dl_oauth` carrega state, nonce e verificador PKCE
// cifrados com o Data Protection do ASP.NET (sem segredo novo), com expiracao embutida; `__Host-dl_pending`
// carrega o token opaco da pendencia (so o hash vai ao store). Nunca loga o conteudo.
// Conteudo decifrado do cookie de correlacao: ReturnTo e so da API (ja validado no start), nunca vai ao caso de uso.
public sealed record GoogleOAuthCookie(GoogleOAuthCorrelation Correlation, string? ReturnTo);

public sealed class GoogleCookieService(IDataProtectionProvider protection, TimeProvider time)
{
    public const string OAuthCookieName = "__Host-dl_oauth";
    public const string PendingCookieName = "__Host-dl_pending";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = protection.CreateProtector("Deckloom.Accounts.GoogleOAuth.v1");

    public void IssueOAuth(HttpContext context, GoogleOAuthCorrelation correlation, string? returnTo = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(correlation);

        var payload = new Payload(
            correlation.State,
            correlation.Nonce,
            correlation.CodeVerifier,
            correlation.Intent == GoogleAuthIntent.Reauth,
            correlation.PlayerId?.Value,
            time.GetUtcNow() + Lifetime,
            returnTo);
        context.Response.Cookies.Append(
            OAuthCookieName, _protector.Protect(JsonSerializer.Serialize(payload)), Options(Lifetime));
    }

    // Cookie ausente, adulterado ou expirado vira null (o chamador trata como falha uniforme).
    public GoogleOAuthCookie? ReadOAuth(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Cookies.TryGetValue(OAuthCookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(value));
            if (payload is null || payload.ExpiresAt <= time.GetUtcNow())
            {
                return null;
            }

            var correlation = new GoogleOAuthCorrelation(
                payload.State,
                payload.Nonce,
                payload.Verifier,
                payload.Reauth ? GoogleAuthIntent.Reauth : GoogleAuthIntent.SignIn,
                payload.PlayerId is { } id ? new PlayerId(id) : null);
            return new GoogleOAuthCookie(correlation, payload.ReturnTo);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public void ExpireOAuth(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = Options(TimeSpan.Zero);
        options.Expires = DateTimeOffset.UnixEpoch;
        context.Response.Cookies.Append(OAuthCookieName, string.Empty, options);
    }

    public void IssuePending(HttpContext context, string pendingToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Cookies.Append(PendingCookieName, pendingToken, Options(Lifetime));
    }

    public string? ReadPending(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.Cookies.TryGetValue(PendingCookieName, out var value) && !string.IsNullOrEmpty(value)
            ? value
            : null;
    }

    public void ExpirePending(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = Options(TimeSpan.Zero);
        options.Expires = DateTimeOffset.UnixEpoch;
        context.Response.Cookies.Append(PendingCookieName, string.Empty, options);
    }

    private static CookieOptions Options(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = maxAge,
    };

    private sealed record Payload(
        string State, string Nonce, string Verifier, bool Reauth, Guid? PlayerId, DateTimeOffset ExpiresAt, string? ReturnTo = null);
}
