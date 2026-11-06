namespace Deckloom.Accounts.Domain;

public sealed class RecentGoogleLogin : Reauthentication
{
    internal RecentGoogleLogin(PlayerId playerId, DateTimeOffset? reauthenticatedAt)
        : base(playerId)
    {
        ReauthenticatedAt = reauthenticatedAt;
    }

    public DateTimeOffset? ReauthenticatedAt { get; }

    private protected override bool ValueEquals(Reauthentication other) =>
        ReauthenticatedAt == ((RecentGoogleLogin)other).ReauthenticatedAt;

    private protected override int ValueHashCode() => ReauthenticatedAt.GetHashCode();
}
