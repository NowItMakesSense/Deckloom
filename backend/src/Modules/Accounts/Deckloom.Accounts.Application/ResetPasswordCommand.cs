namespace Deckloom.Accounts.Application;

public sealed record ResetPasswordCommand(string? Token, string? NewPassword);
