namespace Deckloom.Accounts.Application;

public sealed record RequestPasswordResetCommand(string? Email, string Ip);
