using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-028 (habilitadora; REQ-025; ADR-0006): IPlayerDecksPurger.PurgeAsync(PlayerId, ct). RecordingPlayerDecksPurger(uow)
// registra os PlayerId em Purged como mudanca pendente: aplicada no commit (CommitAsync/ExecuteAsync) e descartada
// no rollback, como os demais fakes do InMemoryUnitOfWork. FailWith(Exception) faz PurgeAsync lancar.
public class RecordingPlayerDecksPurgerTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly RecordingPlayerDecksPurger _purger;
    private readonly PlayerId _ana = new(Guid.NewGuid());
    private readonly PlayerId _gui = new(Guid.NewGuid());

    public RecordingPlayerDecksPurgerTests() => _purger = new RecordingPlayerDecksPurger(_uow);

    [Fact]
    public void REQ025_Starts_with_nothing_purged()
    {
        Assert.Empty(_purger.Purged);
    }

    [Fact]
    public async Task REQ025_Purged_contains_the_player_ids_after_the_unit_of_work_commits()
    {
        await _uow.ExecuteAsync(async ct =>
        {
            await _purger.PurgeAsync(_ana, ct);
            await _purger.PurgeAsync(_gui, ct);
        });

        Assert.Equal([_ana, _gui], _purger.Purged);
    }

    [Fact]
    public async Task REQ025_A_purge_is_pending_until_commit()
    {
        await _purger.PurgeAsync(_ana);
        Assert.Empty(_purger.Purged);

        await _uow.CommitAsync();

        Assert.Equal([_ana], _purger.Purged);
    }

    [Fact]
    public async Task REQ025_Rollback_discards_the_purge_when_the_block_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _purger.PurgeAsync(_ana, ct);
                throw new InvalidOperationException("later step failed");
            }));
        await _uow.CommitAsync();

        Assert.Empty(_purger.Purged);
    }

    [Fact]
    public async Task REQ025_Rollback_discards_the_purge_when_the_commit_fails_on_uniqueness()
    {
        var players = new InMemoryPlayerRepository(_uow);
        await players.Add(PlayerFixtures.APasswordPlayer());
        await _uow.CommitAsync();

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _purger.PurgeAsync(_ana, ct);
                await players.Add(PlayerFixtures.APasswordPlayer(), ct);
            }));

        Assert.Empty(_purger.Purged);
    }

    [Fact]
    public async Task REQ025_Purges_confirmed_earlier_survive_a_later_rollback()
    {
        await _uow.ExecuteAsync(ct => _purger.PurgeAsync(_ana, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _purger.PurgeAsync(_gui, ct);
                throw new InvalidOperationException();
            }));

        Assert.Equal([_ana], _purger.Purged);
    }

    [Fact]
    public async Task REQ025_FailWith_makes_PurgeAsync_throw_the_injected_exception_and_purge_nothing()
    {
        var boom = new InvalidOperationException("decks unavailable");
        _purger.FailWith(boom);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _purger.PurgeAsync(_ana));
        await _uow.CommitAsync();

        Assert.Same(boom, thrown);
        Assert.Empty(_purger.Purged);
    }
}
