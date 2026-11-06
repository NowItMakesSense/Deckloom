namespace Deckloom.Accounts.Domain;

// Traducao, pelo adaptador de persistencia, de um conflito de concorrencia otimista (token de versao divergente).
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("Optimistic concurrency conflict.")
    {
    }
}
