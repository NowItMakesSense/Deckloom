using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// ReservedAttemptId: id do evento provisorio gravado antes da verificacao (SEC-01); nulo no cadastro e quando a
// tentativa foi recusada por passar do orcamento (o evento e apagado). BudgetExceeded: nao verificar a senha.
public sealed record AccessAttemptKeys(
    AccessAction Action,
    byte[] IpKeyHash,
    byte[]? AccountKeyHash,
    long? ReservedAttemptId = null,
    bool BudgetExceeded = false);
