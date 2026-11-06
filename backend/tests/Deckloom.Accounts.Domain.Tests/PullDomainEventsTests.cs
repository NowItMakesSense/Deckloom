using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-023 (habilitadora): AggregateRoot.PullDomainEvents(TimeProvider) carimba OccurredAt, devolve em ordem e limpa.
// Nao ha projeto de testes do SharedKernel; os testes ficam no Domain.Tests.
public class PullDomainEventsTests
{
    private static readonly DateTimeOffset First = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = new(2026, 10, 23, 12, 5, 0, TimeSpan.Zero);

    private sealed record Happened(int Number) : DomainEvent;

    private sealed class SampleRoot : AggregateRoot
    {
        public void Do(DomainEvent domainEvent) => Raise(domainEvent);
    }

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Pulled_events_are_stamped_with_the_clock_time()
    {
        var root = new SampleRoot();
        root.Do(new Happened(1));

        var pulled = root.PullDomainEvents(new SettableClock(First));

        Assert.Equal(First, Assert.Single(pulled).OccurredAt);
    }

    [Fact]
    public void Pulled_events_keep_the_order_in_which_they_were_raised()
    {
        var root = new SampleRoot();
        root.Do(new Happened(1));
        root.Do(new Happened(2));
        root.Do(new Happened(3));

        var pulled = root.PullDomainEvents(new SettableClock(First));

        Assert.Equal([1, 2, 3], pulled.Cast<Happened>().Select(e => e.Number).ToArray());
    }

    [Fact]
    public void Pulling_clears_the_pending_events_so_a_second_pull_returns_nothing()
    {
        var root = new SampleRoot();
        root.Do(new Happened(1));
        var clock = new SettableClock(First);

        root.PullDomainEvents(clock);

        Assert.Empty(root.PullDomainEvents(clock));
        Assert.Empty(root.DomainEvents);
    }

    [Fact]
    public void Events_raised_after_a_pull_are_stamped_with_the_new_time()
    {
        var root = new SampleRoot();
        var clock = new SettableClock(First);
        root.Do(new Happened(1));
        var earlier = root.PullDomainEvents(clock);
        clock.Now = Second;
        root.Do(new Happened(2));

        var later = root.PullDomainEvents(clock);

        Assert.Equal(First, Assert.Single(earlier).OccurredAt);
        Assert.Equal(Second, Assert.Single(later).OccurredAt);
    }

    [Fact]
    public void Pulling_with_a_null_clock_throws_ArgumentNullException()
    {
        var root = new SampleRoot();
        root.Do(new Happened(1));

        Assert.Throws<ArgumentNullException>(() => root.PullDomainEvents(null!));
    }

    [Fact]
    public void A_null_clock_does_not_discard_the_pending_events()
    {
        var root = new SampleRoot();
        root.Do(new Happened(1));

        Assert.Throws<ArgumentNullException>(() => root.PullDomainEvents(null!));

        Assert.Single(root.DomainEvents);
    }

    [Fact]
    public void OccurredAt_has_no_public_setter()
    {
        var property = typeof(DomainEvent).GetProperty(nameof(DomainEvent.OccurredAt));

        Assert.NotNull(property);
        Assert.Equal(typeof(DateTimeOffset), property.PropertyType);
        Assert.Null(property.GetSetMethod());
    }
}
