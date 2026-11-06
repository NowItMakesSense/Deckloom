namespace Deckloom.Accounts.Infrastructure;

public sealed record DatabaseRoleDecision(IReadOnlyCollection<DatabaseRoleRejection> Reasons)
{
    public bool IsAccepted => Reasons.Count == 0;
}
