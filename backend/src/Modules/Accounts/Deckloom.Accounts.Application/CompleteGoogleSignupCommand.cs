namespace Deckloom.Accounts.Application;

// PendingToken: token em claro do cookie `__Host-dl_pending`. Email e subject vem da pendencia, nao do comando.
public sealed record CompleteGoogleSignupCommand(
    string? PendingToken,
    string? DisplayName,
    bool AcceptTerms,
    string? TermsVersion,
    bool AdultDeclaration);
