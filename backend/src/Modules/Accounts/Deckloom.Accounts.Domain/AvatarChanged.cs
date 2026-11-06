using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record AvatarChanged(PlayerId PlayerId, string? PreviousStorageKey) : DomainEvent;
