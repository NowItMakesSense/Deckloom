using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-024 (REQ-002, REQ-027; J1): fake em memoria de IPlayerRepository.
// Forma (design.md, "Forma dos repositorios"): async com ct; Add so enfileira; a unicidade do email
// estoura em IUnitOfWork.CommitAsync com UniqueConstraintViolationException("ux_player_email");
// referencias; Save no-op; Remove fisico (email livre apos o commit); ausencia = nulo.
public class InMemoryPlayerRepositoryTests
{
    private const string EmailConstraint = "ux_player_email";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;

    public InMemoryPlayerRepositoryTests() => _players = new InMemoryPlayerRepository(_uow);

    private async Task Persist(Player player)
    {
        await _players.Add(player);
        await _uow.CommitAsync();
    }

    [Fact]
    public async Task REQ002_Add_with_duplicate_email_does_not_throw_before_commit()
    {
        await Persist(APasswordPlayer());

        var error = await Record.ExceptionAsync(() => _players.Add(APasswordPlayer()));

        Assert.Null(error);
    }

    [Fact]
    public async Task REQ002_Second_Add_with_same_email_is_rejected_on_commit()
    {
        var first = APasswordPlayer();
        await Persist(first);
        await _players.Add(APasswordPlayer());

        var error = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => _uow.CommitAsync());

        Assert.Equal(EmailConstraint, error.ConstraintName);
        Assert.Same(first, await _players.FindByEmail(AnEmail()));
    }

    [Fact]
    public async Task REQ002_Two_Adds_with_same_email_in_one_commit_are_rejected()
    {
        await _players.Add(APasswordPlayer());
        await _players.Add(APasswordPlayer());

        var error = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => _uow.CommitAsync());

        Assert.Equal(EmailConstraint, error.ConstraintName);
    }

    [Fact]
    public async Task REQ002_Duplicate_email_is_detected_after_normalization()
    {
        await Persist(APasswordPlayer("ana@example.com"));
        await _players.Add(APasswordPlayer("ANA@Example.COM"));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => _uow.CommitAsync());
    }

    [Fact]
    public async Task REQ002_Different_emails_commit_without_conflict()
    {
        await _players.Add(APasswordPlayer("ana@example.com"));
        await _players.Add(APasswordPlayer("bia@example.com"));

        var error = await Record.ExceptionAsync(() => _uow.CommitAsync());

        Assert.Null(error);
        Assert.True(await _players.ExistsByEmail(AnEmail("bia@example.com")));
    }

    [Fact]
    public async Task REQ027_Remove_frees_the_email_and_a_new_registration_gets_another_PlayerId()
    {
        var original = APasswordPlayer();
        await Persist(original);

        await _players.Remove(original);
        await _uow.CommitAsync();

        Assert.False(await _players.ExistsByEmail(AnEmail()));
        Assert.Null(await _players.FindByEmail(AnEmail()));
        Assert.Null(await _players.GetById(original.Id));

        var reborn = APasswordPlayer();
        await Persist(reborn);

        Assert.NotEqual(original.Id, reborn.Id);
        Assert.Same(reborn, await _players.FindByEmail(AnEmail()));
    }

    [Fact]
    public async Task REQ027_Remove_of_unknown_player_is_a_noop()
    {
        var kept = APasswordPlayer("ana@example.com");
        await Persist(kept);

        await _players.Remove(APasswordPlayer("bia@example.com"));
        var error = await Record.ExceptionAsync(() => _uow.CommitAsync());

        Assert.Null(error);
        Assert.Same(kept, await _players.GetById(kept.Id));
    }

    [Fact]
    public async Task REQ002_GetById_returns_the_same_reference_or_null()
    {
        var player = APasswordPlayer();
        await Persist(player);

        Assert.Same(player, await _players.GetById(player.Id));
        Assert.Null(await _players.GetById(new PlayerId(Guid.NewGuid())));
    }

    [Fact]
    public async Task REQ002_ExistsByEmail_is_coherent_with_FindByEmail()
    {
        var player = APasswordPlayer();
        await Persist(player);

        Assert.True(await _players.ExistsByEmail(AnEmail()));
        Assert.Same(player, await _players.FindByEmail(AnEmail()));
        Assert.False(await _players.ExistsByEmail(AnEmail("outra@example.com")));
        Assert.Null(await _players.FindByEmail(AnEmail("outra@example.com")));
    }

    [Fact]
    public async Task REQ002_FindByEmail_compares_the_normalized_email()
    {
        var player = APasswordPlayer("ana@example.com");
        await Persist(player);

        Assert.Same(player, await _players.FindByEmail(AnEmail("ANA@Example.COM")));
        Assert.True(await _players.ExistsByEmail(AnEmail("ANA@Example.COM")));
    }

    [Fact]
    public async Task REQ002_FindByGoogleSubject_finds_only_the_player_with_that_subject()
    {
        var google = AGooglePlayer("gui@example.com", "sub-1");
        await Persist(google);
        await Persist(APasswordPlayer("ana@example.com"));

        Assert.Same(google, await _players.FindByGoogleSubject("sub-1"));
        Assert.Null(await _players.FindByGoogleSubject("sub-2"));
    }

    [Fact]
    public async Task REQ002_FindByResetTokenHash_finds_the_player_that_issued_that_token()
    {
        var player = APasswordPlayer("ana@example.com");
        var other = APasswordPlayer("bia@example.com");
        await Persist(player);
        await Persist(other);
        Assert.Null(await _players.FindByResetTokenHash(AHash(1)));

        Assert.True(player.IssuePasswordReset(AHash(1), Now).IsSuccess);
        await _players.Save(player);

        Assert.Same(player, await _players.FindByResetTokenHash(AHash(1)));
        Assert.Null(await _players.FindByResetTokenHash(AHash(2)));
    }

    [Fact]
    public async Task REQ002_Save_of_a_player_never_added_is_a_noop()
    {
        var stranger = APasswordPlayer();

        var error = await Record.ExceptionAsync(() => _players.Save(stranger));
        await _uow.CommitAsync();

        Assert.Null(error);
        Assert.Null(await _players.GetById(stranger.Id));
    }

    [Fact]
    public async Task REQ002_Failed_commit_applies_nothing_not_even_the_valid_Add()
    {
        await Persist(APasswordPlayer("ana@example.com"));
        var valid = APasswordPlayer("bia@example.com");
        await _players.Add(valid);
        await _players.Add(APasswordPlayer("ana@example.com"));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => _uow.CommitAsync());

        Assert.False(await _players.ExistsByEmail(AnEmail("bia@example.com")));
        Assert.Null(await _players.GetById(valid.Id));
    }

    [Fact]
    public async Task REQ027_Failed_commit_does_not_apply_a_pending_Remove()
    {
        var kept = APasswordPlayer("ana@example.com");
        var removable = APasswordPlayer("bia@example.com");
        await Persist(kept);
        await Persist(removable);
        await _players.Remove(removable);
        await _players.Add(APasswordPlayer("ana@example.com"));

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => _uow.CommitAsync());

        Assert.Same(removable, await _players.GetById(removable.Id));
        Assert.True(await _players.ExistsByEmail(AnEmail("bia@example.com")));
        Assert.Same(kept, await _players.FindByEmail(AnEmail("ana@example.com")));
    }

    [Fact]
    public async Task REQ002_Failed_commit_discards_pending_operations()
    {
        var first = APasswordPlayer("ana@example.com");
        await Persist(first);
        var valid = APasswordPlayer("bia@example.com");
        await _players.Add(valid);
        await _players.Add(APasswordPlayer("ana@example.com"));
        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => _uow.CommitAsync());

        var error = await Record.ExceptionAsync(() => _uow.CommitAsync());

        Assert.Null(error);
        Assert.Null(await _players.GetById(valid.Id));
        Assert.Same(first, await _players.FindByEmail(AnEmail("ana@example.com")));
    }
}
