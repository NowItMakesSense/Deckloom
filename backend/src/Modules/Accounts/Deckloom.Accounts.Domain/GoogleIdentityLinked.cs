using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record GoogleIdentityLinked(PlayerId PlayerId) : DomainEvent;
