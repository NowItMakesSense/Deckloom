namespace Deckloom.Accounts.Domain;

public abstract class Reauthentication : IEquatable<Reauthentication>
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private protected Reauthentication(PlayerId playerId)
    {
        PlayerId = playerId;
    }

    public PlayerId PlayerId { get; }

    public bool Equals(Reauthentication? other) =>
        other is not null && GetType() == other.GetType() && PlayerId == other.PlayerId && ValueEquals(other);

    public override bool Equals(object? obj) => Equals(obj as Reauthentication);

    public override int GetHashCode() => HashCode.Combine(GetType(), PlayerId, ValueHashCode());

    private protected abstract bool ValueEquals(Reauthentication other);

    private protected abstract int ValueHashCode();
}
