using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record PasswordResetIssued(PlayerId PlayerId, DateTimeOffset ExpiresAt) : DomainEvent;
