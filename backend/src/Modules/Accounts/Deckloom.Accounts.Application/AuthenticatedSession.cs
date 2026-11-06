namespace Deckloom.Accounts.Application;

public sealed record AuthenticatedSession(string SessionToken, DateTimeOffset ExpiresAt, SessionSummary Summary);
