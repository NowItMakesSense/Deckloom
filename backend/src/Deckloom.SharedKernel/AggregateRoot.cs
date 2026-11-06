namespace Deckloom.SharedKernel;

public abstract class AggregateRoot
{
    private readonly List<DomainEvent> _domainEvents = [];

    public IReadOnlyCollection<DomainEvent> DomainEvents => [.. _domainEvents];

    public void ClearDomainEvents() => _domainEvents.Clear();

    public IReadOnlyList<DomainEvent> PullDomainEvents(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var occurredAt = clock.GetUtcNow();
        foreach (var domainEvent in _domainEvents)
        {
            domainEvent.OccurredAt = occurredAt;
        }

        var pulled = _domainEvents.ToArray();
        _domainEvents.Clear();
        return pulled;
    }

    protected void Raise(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
