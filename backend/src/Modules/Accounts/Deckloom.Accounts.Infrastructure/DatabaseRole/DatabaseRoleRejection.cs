namespace Deckloom.Accounts.Infrastructure;

/// <summary>Condicao violada pelo papel; a mensagem nunca carrega nome de papel, banco, host ou connection string.</summary>
public sealed record DatabaseRoleRejection(string Code, string Message);
