namespace Deckloom.Accounts.Application;

public enum PendingGoogleAuthKind
{
    Signup,
    Link,
}

// Sem subject nem tokens do Google: so o que a tela de conclusao/vinculo precisa.
public sealed record PendingGoogleAuthView(
    PendingGoogleAuthKind Kind,
    string Email,
    string? SuggestedDisplayName);
