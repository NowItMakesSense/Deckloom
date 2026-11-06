using System.Globalization;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-P03 (APR-017, AC-P03a): os eventos de dominio do agregado viram linhas de accounts.outbox_message gravadas no MESMO
// SaveChanges/transacao que persiste o agregado. EF Core sobre SQLite em memoria (ADR-0012).
public sealed class OutboxPersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player NewPlayer(string email = "ana@example.com") =>
        Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private long Count(string table)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task AC_P03a_commit_writes_the_aggregate_and_one_outbox_message_per_domain_event()
    {
        await using var context = _db.CreateContext();
        var player = NewPlayer();
        var raised = player.DomainEvents.Count;
        await new EfPlayerRepository(context).Add(player);

        await new EfUnitOfWork(context).CommitAsync();

        Assert.True(raised > 0);
        Assert.Equal(1, Count("player"));
        Assert.Equal(raised, Count("outbox_message"));
        var pending = await new EfOutboxStore(context).GetPendingAsync(10);
        var message = Assert.Single(pending, m => m.Type == nameof(PlayerRegistered));
        Assert.Contains(player.Id.Value.ToString(), message.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public async Task AC_P03a_rollback_of_the_use_case_leaves_neither_aggregate_nor_message()
    {
        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteAsync(async ct =>
        {
            await new EfPlayerRepository(context).Add(NewPlayer(), ct);
            await context.SaveChangesAsync(ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(0, Count("player"));
        Assert.Equal(0, Count("outbox_message"));
    }

    [Fact]
    public async Task AC_P03a_failed_save_leaves_neither_aggregate_nor_message()
    {
        await using (var first = _db.CreateContext())
        {
            await new EfPlayerRepository(first).Add(NewPlayer());
            await new EfUnitOfWork(first).CommitAsync();
        }

        var playersBefore = Count("player");
        var messagesBefore = Count("outbox_message");
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(NewPlayer()); // mesmo email: viola ux_player_email

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => new EfUnitOfWork(context).CommitAsync());

        Assert.Equal(playersBefore, Count("player"));
        Assert.Equal(messagesBefore, Count("outbox_message"));
    }

    [Fact]
    public async Task AC_P03a_saving_twice_does_not_duplicate_messages_and_keeps_domain_events_on_the_aggregate()
    {
        await using var context = _db.CreateContext();
        var player = NewPlayer();
        var raised = player.DomainEvents.Count;
        await new EfPlayerRepository(context).Add(player);
        var unitOfWork = new EfUnitOfWork(context);

        await unitOfWork.CommitAsync();
        await unitOfWork.CommitAsync();

        Assert.Equal(raised, Count("outbox_message"));
        Assert.Equal(raised, player.DomainEvents.Count);
    }

    [Fact]
    public async Task AC_P03a_store_marks_a_message_processed_only_once()
    {
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(NewPlayer());
        await new EfUnitOfWork(context).CommitAsync();
        var store = new EfOutboxStore(context);
        var message = (await store.GetPendingAsync(10))[0];

        var first = await store.MarkProcessedAsync(message.Id, Now);
        var second = await store.MarkProcessedAsync(message.Id, Now);

        Assert.True(first);
        Assert.False(second);
        Assert.DoesNotContain(await store.GetPendingAsync(10), m => m.Id == message.Id);
    }

    [Fact]
    public async Task AC_P03a_store_returns_pending_messages_oldest_first_limited_by_batch()
    {
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(NewPlayer());
        await new EfUnitOfWork(context).CommitAsync();
        var store = new EfOutboxStore(context);

        var one = await store.GetPendingAsync(1);

        Assert.Single(one);
        var all = await store.GetPendingAsync(100);
        Assert.Equal(all.OrderBy(m => m.OccurredAt).Select(m => m.Id), all.Select(m => m.Id));
    }
}
