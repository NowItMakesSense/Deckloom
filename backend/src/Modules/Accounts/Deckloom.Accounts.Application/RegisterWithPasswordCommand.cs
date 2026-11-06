namespace Deckloom.Accounts.Application;

public sealed record RegisterWithPasswordCommand(
    string? Email,
    string? Password,
    string? DisplayName,
    bool AcceptTerms,
    string? TermsVersion,
    bool AdultDeclaration,
    string Ip);
