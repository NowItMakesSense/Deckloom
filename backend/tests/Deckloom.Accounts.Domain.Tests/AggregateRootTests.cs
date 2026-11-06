using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-015 (habilitadora): base AggregateRoot do SharedKernel.
// Contrato assumido para o GREEN: DomainEvents (somente leitura, na ordem de emissao), ClearDomainEvents(),
// e metodo protegido Raise(DomainEvent) usado pelos agregados.
public class AggregateRootTests
{
    private sealed record Happened(int Number) : DomainEvent;

    private sealed class SampleRoot : AggregateRoot
    {
        public void Do(DomainEvent domainEvent) => Raise(domainEvent);
    }

    [Fact]
    public void A_new_aggregate_has_no_events()
    {
        Assert.Empty(new SampleRoot().DomainEvents);
    }

    [Fact]
    public void Raised_events_are_exposed_in_order()
    {
        var root = new SampleRoot();

        root.Do(new Happened(1));
        root.Do(new Happened(2));

        Assert.Equal<DomainEvent>([new Happened(1), new Happened(2)], root.DomainEvents);
    }

    [Fact]
    public void ClearDomainEvents_removes_all_events()
    {
        var root = new SampleRoot();
        root.Do(new Happened(1));

        root.ClearDomainEvents();

        Assert.Empty(root.DomainEvents);
    }

    [Fact]
    public void The_exposed_collection_is_a_snapshot_that_does_not_change_when_events_are_raised_later()
    {
        var root = new SampleRoot();
        var before = root.DomainEvents;

        root.Do(new Happened(1));

        Assert.Empty(before);
    }
}
