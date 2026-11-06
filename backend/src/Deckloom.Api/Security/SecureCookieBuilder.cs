namespace Deckloom.Api.Security;

// SEC-04/SEC-18: atras do reverse proxy a API recebe HTTP puro e ignora o X-Forwarded-Proto (ADR-0016). O antiforgery
// com SecurePolicy=Always lanca em requisicao nao HTTPS, e SameAsRequest deixaria o cookie sem Secure, que o navegador
// recusa pelo prefixo __Host-. Este builder usa SameAsRequest (o antiforgery nao lanca) e fixa Secure sempre, como os
// cookies de sessao e do Google: nada que o cliente envie rebaixa o cookie.
internal sealed class SecureCookieBuilder : CookieBuilder
{
    public override CookieOptions Build(HttpContext context, DateTimeOffset expiresFrom)
    {
        var options = base.Build(context, expiresFrom);
        options.Secure = true;
        return options;
    }
}
