namespace Deckloom.Accounts.Application;

public sealed record LoginWithPasswordCommand(string? Email, string? Password, bool RememberMe, string Ip);
