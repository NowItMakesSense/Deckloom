using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record ProfileUpdated(PlayerId PlayerId, IReadOnlyList<string> ChangedFields) : DomainEvent;
