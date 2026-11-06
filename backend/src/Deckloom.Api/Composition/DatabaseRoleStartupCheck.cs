using Deckloom.Accounts.Infrastructure;

namespace Deckloom.Api.Composition;

// Porta de inicializacao (DB7/UA2): recusa subir se o papel de conexao nao e conforme. Roda como o primeiro
// IHostedService, antes de o servidor aceitar requisicoes. So atua quando o inspetor esta registrado (adapter
// PostgreSQL); com persistencia fake nao ha o que verificar. Mensagens trazem apenas codigos de condicao.
// Nao verifica migracoes pendentes.
public sealed class DatabaseRoleStartupCheck(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var inspector = services.GetService<IDatabaseRoleInspector>();
        if (inspector is null)
        {
            return;
        }

        RoleSnapshot snapshot;
        try
        {
            snapshot = await inspector.InspectAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Sem inner exception: o texto original pode conter valores de conexao.
            throw new InvalidOperationException("Startup refused: the database connection role could not be verified (DB7).");
        }

        var decision = DatabaseRolePolicy.Evaluate(snapshot);
        if (!decision.IsAccepted)
        {
            var codes = string.Join(", ", decision.Reasons.Select(r => r.Code));
            throw new InvalidOperationException($"Startup refused: the database connection role is not compliant ({codes}).");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
