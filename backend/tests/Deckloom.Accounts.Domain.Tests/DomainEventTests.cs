using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-001 (habilitadora, sem REQ direto): base DomainEvent do SharedKernel.
// Escolha de forma (a confirmar): DomainEvent e um record abstrato, sem membros proprios;
// OccurredAt e adiado para T-023 (REFACTOR).
public class DomainEventTests
{
    private sealed record SampleHappened(Guid PlayerId) : DomainEvent;

    [Fact]
    public void A_derived_event_is_a_domain_event()
    {
        DomainEvent domainEvent = new SampleHappened(Guid.NewGuid());

        Assert.IsAssignableFrom<DomainEvent>(domainEvent);
    }

    [Fact]
    public void Events_with_the_same_data_are_equal()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new SampleHappened(id), new SampleHappened(id));
        Assert.NotEqual(new SampleHappened(id), new SampleHappened(Guid.NewGuid()));
    }

    [Fact]
    public void DomainEvent_is_abstract()
    {
        Assert.True(typeof(DomainEvent).IsAbstract);
    }
}
