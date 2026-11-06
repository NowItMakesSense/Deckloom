namespace Deckloom.Accounts.Application;

public sealed record SessionSummary(string DisplayName, AvatarView Avatar, bool HasPassword, bool HasGoogle);
