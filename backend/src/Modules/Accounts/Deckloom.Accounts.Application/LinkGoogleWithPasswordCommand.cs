namespace Deckloom.Accounts.Application;

// PendingToken: token em claro do cookie `__Host-dl_pending`. O email da chave de throttle vem da pendencia.
public sealed record LinkGoogleWithPasswordCommand(string? PendingToken, string? Password, string Ip);
