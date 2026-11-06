namespace Deckloom.Api.Security;

// T-077: cabecalhos de seguranca em toda resposta, inclusive a de erro (registrado antes do tratamento de excecao).
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string SessionCookieName = "__Host-dl_session";

    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; "
        + "connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;

        response.OnStarting(() =>
        {
            var headers = response.Headers;
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            if (!environment.IsDevelopment())
            {
                headers["Strict-Transport-Security"] = "max-age=31536000";
            }

            // Respostas com sessao nao sao cacheadas, salvo quando o endpoint declara a propria politica (avatar versionado).
            if (request.Cookies.ContainsKey(SessionCookieName) && headers.CacheControl.Count == 0)
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
