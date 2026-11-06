using Deckloom.Api.Composition;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Deckloom.Api.E2E.Host;

// Tipo de entrada usado pelo WebApplicationFactory dos testes (E2eEnvironmentGuardTests).
public sealed class E2eHostMarker;

// Host de teste E2E: mesma composicao HTTP da API (ApiHost) com adapters em memoria quando o ambiente e "E2E".
// Em qualquer outro ambiente nenhum fake e registrado e as rotas /__test respondem 404.
internal static class E2eHostProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var e2e = E2eEnvironment.IsE2E(builder.Environment);

        StubGoogleAuthority? google = null;
        if (e2e)
        {
            google = StubGoogleAuthority.Start(builder.Configuration);
            builder.Configuration.AddInMemoryCollection(E2eEnvironment.PlaceholderSettings(builder.Configuration));
        }

        ApiHost.ConfigureServices(builder);
        builder.Services.RemoveDatabaseRoleCheck();
        if (e2e)
        {
            builder.Services.AddE2eAdapters();
            // SEC-06: fora de Development a API recusa Authority/PublicOrigin em http. Este host de teste fala com o
            // StubGoogleOidcServer em http loopback, entao so aqui a regra e dispensada (nunca no host de producao).
            builder.Services.RemoveAll<IValidateOptions<AuthOptions>>();
            builder.Services.RemoveAll<IValidateOptions<EmailOptions>>();
            builder.Services.AddSingleton(google!);
        }

        var app = builder.Build();
        if (google is not null)
        {
            app.Lifetime.ApplicationStopping.Register(google.Dispose);
            google.UseClock(app.Services.GetRequiredService<TimeProvider>());
        }

        if (e2e)
        {
            // O navegador fala http://localhost (contexto seguro: aceita cookies Secure/__Host-), mas o antiforgery e
            // os cookies da API exigem HTTPS. Somente neste host de teste a requisicao e tratada como HTTPS.
            app.Use((context, next) =>
            {
                context.Request.Scheme = Uri.UriSchemeHttps;
                return next(context);
            });
        }

        // Antes do pipeline da API: o CSRF barraria o POST de teste com 403 e nunca chegaria a 404.
        app.MapWhen(
            context => context.Request.Path.StartsWithSegments(TestEndpoints.Prefix),
            branch => branch.Run(context => e2e
                ? TestEndpoints.HandleAsync(context)
                : NotFound(context)));
        ApiHost.ConfigurePipeline(app);

        app.Run();
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}
