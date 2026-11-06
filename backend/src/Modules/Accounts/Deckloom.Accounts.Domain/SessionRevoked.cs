using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record SessionRevoked(SessionId SessionId, PlayerId PlayerId, SessionRevocationReason Reason) : DomainEvent;
