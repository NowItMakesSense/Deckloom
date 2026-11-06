using Deckloom.Accounts.Infrastructure;
using Deckloom.Api.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Deckloom.Api.Tests;

// Fabricas que trocam a persistencia por fakes nao tem banco para verificar: a porta de inicializacao do papel de
// conexao (DB7) so faz sentido com o adapter PostgreSQL. Ponto unico de remocao, usado por todas essas fabricas.
internal static class FakePersistenceHost
{
    public static IServiceCollection RemoveDatabaseRoleCheck(this IServiceCollection services)
    {
        services.RemoveAll<IDatabaseRoleInspector>();
        var checks = services
            .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(DatabaseRoleStartupCheck))
            .ToList();
        foreach (var check in checks)
        {
            services.Remove(check);
        }

        return services;
    }
}
