using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record PlayerDeleted(PlayerId PlayerId) : DomainEvent;
