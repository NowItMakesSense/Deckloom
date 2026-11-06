using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure;

// Linha de accounts.pending_google_auth. O registro da aplicacao (PendingGoogleAuth) nao e
// mapeado direto: esta classe de persistencia fica na infraestrutura e o EfPendingGoogleAuthStore converte.
internal sealed class PendingGoogleAuthRow
{
    public const string SignupKind = "signup";
    public const string LinkKind = "link";

    public Guid Id { get; set; }

    public required TokenHash TokenHash { get; set; }

    public required string Kind { get; set; }

    public required string GoogleSubject { get; set; }

    public required Email Email { get; set; }

    public string? SuggestedDisplayName { get; set; }

    public PlayerId? TargetPlayerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
