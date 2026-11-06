namespace Deckloom.Accounts.Infrastructure;

/// <summary>Porta de leitura dos atributos do papel de conexao da API (DB7/UA2); consulta somente leitura.</summary>
public interface IDatabaseRoleInspector
{
    Task<RoleSnapshot> InspectAsync(CancellationToken cancellationToken);
}
