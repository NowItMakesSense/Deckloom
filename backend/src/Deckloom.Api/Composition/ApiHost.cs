using System.Net;
using Deckloom.Api.Account;
using Deckloom.Api.Auth;
using Deckloom.Api.Catalog;
using Deckloom.Api.Http;
using Deckloom.Api.Logging;
using Deckloom.Api.Problems;
using Deckloom.Api.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Deckloom.Api.Composition;

// Ponto unico da composicao HTTP da API: servicos e pipeline. O Program de producao e o host de teste E2E
// (backend/tests/Deckloom.Api.E2E.Host) chamam os mesmos metodos; o host de teste so troca adapters por fakes.
public static class ApiHost
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.FormatterName = TraceJsonConsoleFormatter.FormatterName);
        builder.Logging.AddConsoleFormatter<TraceJsonConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
        // O log de acesso (Hosting.Diagnostics) e o do RedirectResult gravam a URL completa em Information; o callback OAuth
        // leva code/state na query e o start leva state/nonce/code_challenge. Em Warning ficam fora do fluxo normal.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Result", LogLevel.Warning);
        builder.Services.AddProblemDetails();
        // R-14: so se confia no X-Forwarded-For vindo de proxies listados em Forwarding:TrustedProxies (IPs ou CIDRs;
        // em Docker Compose, a sub-rede da rede entre nginx e API). Sem a chave: apenas loopback. Entrada invalida
        // derruba o start. Limpa as redes conhecidas (o padrao inclui 127.0.0.0/8) e so encaminha o X-Forwarded-For.
        // A leitura e adiada para a resolucao das opcoes: fontes adicionadas depois deste ponto (ConfigureAppConfiguration)
        // tambem valem.
        builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var trustedProxies = TrustedProxyList.Parse(configuration.GetSection(TrustedProxyList.SectionName));
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in trustedProxies.Addresses)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in trustedProxies.Networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });
        builder.Services.AddHttpLimits();
        // R-15: sem esquema de autenticacao no pipeline. Cada endpoint protegido chama AuthenticateSession uma unica vez
        // (T-039: o callback Google nao renova a sessao); rotas publicas nunca tocam a sessao.
        builder.Services.AddSingleton<SessionCookieService>();
        builder.Services.AddSingleton<IAccountsAuditLog, AccountsAuditLog>();
        builder.Services.AddSingleton<IDatabaseFailureLog, DatabaseFailureLog>();
        builder.Services.AddSingleton<GoogleCookieService>();
        builder.Services.AddDataProtection();
        // Portas -> adapters de producao, casos de uso e opcoes validadas no start (ver Deckloom.Api.Composition).
        builder.Services.AddAccountsModule(builder.Configuration, builder.Environment);
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfProtectionMiddleware.HeaderName;
            options.Cookie.Name = CsrfProtectionMiddleware.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
        });
        // SEC-04/SEC-18: Secure sempre (prefixo __Host-), sem depender do esquema da requisicao nem de Auth:PublicOrigin.
        builder.Services.Configure<AntiforgeryOptions>(options =>
        {
            var template = options.Cookie;
            options.Cookie = new SecureCookieBuilder
            {
                Name = template.Name,
                HttpOnly = template.HttpOnly,
                SecurePolicy = CookieSecurePolicy.SameAsRequest,
                SameSite = template.SameSite,
                Path = template.Path,
            };
        });
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Primeiro no pipeline do host: captura qualquer falha de middlewares e endpoints seguintes.
        // CORS fica desabilitado de proposito (mesma origem do frontend): sem AddCors/UseCors.
        // Antes do limitador de concorrencia e dos endpoints: eles leem Connection.RemoteIpAddress.
        app.UseForwardedHeaders();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<UnhandledExceptionMiddleware>();
        app.UseMiddleware<BodySizeLimitMiddleware>();
        app.UseRateLimiter();
        app.UseMiddleware<CsrfProtectionMiddleware>();

        app.MapGet("/api/v1/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = AnonymousAntiforgery.GetAndStoreRequestToken(antiforgery, context) }));

        app.MapCatalogEndpoints();
        app.MapAuthEndpoints();
        app.MapGoogleEndpoints();
        app.MapMeProfileEndpoints();
        app.MapAvatarEndpoints();
        app.MapMeDeletionEndpoints();
    }
}
