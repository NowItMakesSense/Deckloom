using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record PasswordChanged(PlayerId PlayerId) : DomainEvent;
