using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public sealed record AccessAttempt(
    AccessAction Action,
    byte[] IpKeyHash,
    byte[]? AccountKeyHash,
    DateTimeOffset AttemptedAt);
