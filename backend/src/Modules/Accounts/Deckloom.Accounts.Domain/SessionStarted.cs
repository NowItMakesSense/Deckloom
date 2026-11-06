using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record SessionStarted(SessionId SessionId, PlayerId PlayerId, bool RememberMe) : DomainEvent;
