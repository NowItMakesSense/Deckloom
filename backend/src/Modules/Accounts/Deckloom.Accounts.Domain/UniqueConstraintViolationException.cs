namespace Deckloom.Accounts.Domain;

public sealed class UniqueConstraintViolationException(string constraintName)
    : Exception($"Unique constraint violated: {constraintName}")
{
    public string ConstraintName { get; } = constraintName;
}
