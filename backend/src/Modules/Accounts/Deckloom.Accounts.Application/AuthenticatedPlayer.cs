using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public sealed record AuthenticatedPlayer(PlayerId PlayerId, SessionId SessionId, SessionSummary Summary);
