using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public abstract record PendingGoogleAuth(
    Guid Id,
    TokenHash TokenHash,
    string GoogleSubject,
    Email Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
}

public sealed record PendingGoogleSignup(
    Guid Id,
    TokenHash TokenHash,
    string GoogleSubject,
    Email Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? SuggestedDisplayName)
    : PendingGoogleAuth(Id, TokenHash, GoogleSubject, Email, CreatedAt, ExpiresAt)
{
    public static PendingGoogleSignup Create(
        TokenHash tokenHash,
        string googleSubject,
        Email email,
        string? suggestedDisplayName,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), tokenHash, googleSubject, email, now, now + Ttl, suggestedDisplayName);
}

public sealed record PendingGoogleLink(
    Guid Id,
    TokenHash TokenHash,
    string GoogleSubject,
    Email Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    PlayerId TargetPlayerId)
    : PendingGoogleAuth(Id, TokenHash, GoogleSubject, Email, CreatedAt, ExpiresAt)
{
    public static PendingGoogleLink Create(
        TokenHash tokenHash,
        string googleSubject,
        Email email,
        PlayerId targetPlayerId,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), tokenHash, googleSubject, email, now, now + Ttl, targetPlayerId);
}
