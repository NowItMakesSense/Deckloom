namespace Deckloom.SharedKernel;

public abstract record DomainEvent
{
    // Carimbado por AggregateRoot.PullDomainEvents; sem setter publico.
    public DateTimeOffset OccurredAt { get; internal set; }
}
