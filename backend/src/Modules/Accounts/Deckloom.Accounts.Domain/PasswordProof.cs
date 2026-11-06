namespace Deckloom.Accounts.Domain;

public sealed class PasswordProof : Reauthentication
{
    internal PasswordProof(PlayerId playerId)
        : base(playerId)
    {
    }

    private protected override bool ValueEquals(Reauthentication other) => true;

    private protected override int ValueHashCode() => 0;
}
