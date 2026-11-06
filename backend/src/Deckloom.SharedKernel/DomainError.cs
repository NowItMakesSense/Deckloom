namespace Deckloom.SharedKernel;

public sealed record DomainError(string Code, string? Field = null);
