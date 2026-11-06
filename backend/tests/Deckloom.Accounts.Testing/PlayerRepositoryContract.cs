using System.Reflection;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Testing;

// T-141 (retro item 1): contrato de IPlayerRepository escrito como os casos de uso chamam a porta: gravar sempre por
// IUnitOfWork.ExecuteAsync SEM CommitAsync, e ler de volta em um escopo novo. Roda contra o fake e contra EfPlayerRepository.
// Cobertura da porta: GetById, FindByEmail, FindByGoogleSubject, FindByResetTokenHash, ExistsByEmail, Add, Save, Remove,
// DeleteExpiredResetTokens.
public abstract class PlayerRepositoryContract : IAsyncDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string OtherStored =
        "v1$pbkdf2-sha256$600000$b3RoZXJzYWx0b3RoZXJzYQ==$b3RoZXJoYXNob3RoZXJoYXNob3RoZXJoYXNob3RoZXI=";
    private const string AvatarKey = "key-p-0000000000000000000";

    private static readonly DateTimeOffset Now = AccountsContractFixture.ContractNow;
    private static readonly byte[] Image = [0x52, 0x49, 0x46, 0x46, 0x01, 0x02, 0x03];

    private AccountsContractFixture? _fixture;

    protected abstract AccountsContractFixture CreateFixture();

    private AccountsContractFixture Fixture => _fixture ??= CreateFixture();

    public async ValueTask DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

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

    private static Player NewGooglePlayer(string email = "gui@example.com", string subject = "google-sub-1") =>
        Player.RegisterWithGoogle(
            Email.Create(email).Value,
            ExternalIdentity.Create(ExternalIdentity.GoogleProvider, subject).Value,
            DisplayName.Create("Gui Lima").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private static TokenHash Token(byte seed) => TokenHash.Create([.. Enumerable.Repeat(seed, TokenHash.Length)]).Value;

    private static Email EmailOf(string value) => Email.Create(value).Value;

    private static FormatId[] Formats(params string[] ids) => [.. ids.Select(id => new FormatId(id))];

    // PasswordProof so tem construtor interno no Dominio; o caso de uso o cria igual (LinkGoogleWithPassword).
    private static PasswordProof ProofFor(Player player) =>
        (PasswordProof)Activator.CreateInstance(
            typeof(PasswordProof),
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [player.Id],
            null)!;

    private async Task Persist(Player player)
    {
        await using var scope = Fixture.CreateScope();
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(player, ct));
    }

    // Carrega o Jogador em um escopo proprio, aplica a mudanca e grava como os casos de uso: Save dentro de ExecuteAsync.
    private async Task Change(PlayerId id, Action<Player> change)
    {
        await using var scope = Fixture.CreateScope();
        var player = await scope.Players.GetById(id);
        Assert.NotNull(player);
        await scope.UnitOfWork.ExecuteAsync(ct =>
        {
            change(player);
            return scope.Players.Save(player, ct);
        });
    }

    private async Task<Player?> Reload(PlayerId id)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.Players.GetById(id);
    }

    [Fact]
    public async Task Player_contract_Add_then_GetById_in_a_new_scope_returns_the_player()
    {
        var player = NewPlayer();
        await Persist(player);

        var loaded = await Reload(player.Id);

        Assert.NotNull(loaded);
        Assert.Equal(player.Id, loaded.Id);
        Assert.Equal(player.Email, loaded.Email);
        Assert.Equal(player.DisplayName, loaded.DisplayName);
        Assert.Equal(player.PasswordHash?.ToStoredString(), loaded.PasswordHash?.ToStoredString());
    }

    [Fact]
    public async Task Player_contract_FindByEmail_and_ExistsByEmail_ignore_case_and_surrounding_spaces()
    {
        var player = NewPlayer("ana@example.com");
        await Persist(player);
        await using var reader = Fixture.CreateScope();
        var typed = EmailOf("  ANA@Example.COM ");

        var found = await reader.Players.FindByEmail(typed);

        Assert.NotNull(found);
        Assert.Equal(player.Id, found.Id);
        Assert.True(await reader.Players.ExistsByEmail(typed));
    }

    [Fact]
    public async Task Player_contract_FindByEmail_and_ExistsByEmail_of_an_unknown_email_are_absent()
    {
        await Persist(NewPlayer("ana@example.com"));
        await using var reader = Fixture.CreateScope();

        Assert.Null(await reader.Players.FindByEmail(EmailOf("other@example.com")));
        Assert.False(await reader.Players.ExistsByEmail(EmailOf("other@example.com")));
    }

    [Fact]
    public async Task Player_contract_a_missing_player_is_absent_not_an_exception()
    {
        await using var reader = Fixture.CreateScope();

        Assert.Null(await reader.Players.GetById(new PlayerId(Guid.NewGuid())));
        Assert.Null(await reader.Players.FindByGoogleSubject("nobody"));
        Assert.Null(await reader.Players.FindByResetTokenHash(Token(9)));
    }

    [Fact]
    public async Task Player_contract_FindByGoogleSubject_matches_the_google_identity()
    {
        var player = NewGooglePlayer(subject: "google-sub-1");
        await Persist(player);
        await using var reader = Fixture.CreateScope();

        var found = await reader.Players.FindByGoogleSubject("google-sub-1");

        Assert.NotNull(found);
        Assert.Equal(player.Id, found.Id);
        Assert.Equal("google-sub-1", found.ExternalIdentity?.Subject);
        Assert.Equal(ExternalIdentity.GoogleProvider, found.ExternalIdentity?.Provider);
        Assert.Null(await reader.Players.FindByGoogleSubject("google-sub-2"));
    }

    [Fact]
    public async Task Player_contract_a_duplicate_email_is_refused_with_the_same_observable_outcome_and_keeps_the_first()
    {
        var first = NewPlayer("dup@example.com");
        var second = NewPlayer("DUP@example.com");
        await Persist(first);
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(second, ct)));

        Assert.Equal(first.Id, (await Reload(first.Id))?.Id);
        Assert.Null(await Reload(second.Id));
    }

    [Fact]
    public async Task Player_contract_a_duplicate_google_identity_is_refused_with_UniqueConstraintViolationException()
    {
        await Persist(NewGooglePlayer("one@example.com", "same-sub"));
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.Players.Add(NewGooglePlayer("two@example.com", "same-sub"), ct)));
    }

    [Fact]
    public async Task Player_contract_Save_persists_the_profile_changes_read_in_a_new_scope()
    {
        var player = NewPlayer();
        await Persist(player);

        await Change(player.Id, p => Assert.True(p.UpdateProfile("Ana Maria", "Gosto de Commander", "mtg", [], []).IsSuccess));

        var loaded = await Reload(player.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Ana Maria", loaded.DisplayName.Value);
        Assert.Equal("Gosto de Commander", loaded.Bio?.Value);
        Assert.Equal(FavoriteTcg.Default, loaded.FavoriteTcg);
    }

    [Fact]
    public async Task Player_contract_Save_persists_the_custom_avatar_and_the_preset_choice()
    {
        var player = NewPlayer();
        await Persist(player);

        await Change(player.Id, p => Assert.True(p.SetCustomAvatar(AvatarKey).IsSuccess));
        Assert.Equal(AvatarKey, (await Reload(player.Id))?.Avatar is CustomAvatar custom ? custom.StorageKey : null);

        await Change(player.Id, p => Assert.True(p.ChooseAvatarPreset("preset-1").IsSuccess));
        Assert.Equal("preset-1", (await Reload(player.Id))?.Avatar is PresetAvatar preset ? preset.PresetId : null);
    }

    [Fact]
    public async Task Player_contract_preferred_formats_are_saved_and_read_back_in_a_new_scope()
    {
        var player = NewPlayer();
        await Persist(player);
        var active = Formats("commander", "modern", "pauper");

        await Change(player.Id, p => Assert.True(
            p.UpdateProfile("Ana Souza", null, "mtg", Formats("commander", "modern"), active).IsSuccess));

        Assert.Equal(
            new HashSet<FormatId>(Formats("commander", "modern")),
            [.. (await Reload(player.Id))!.Formats.Ids]);
    }

    [Fact]
    public async Task Player_contract_formats_chosen_before_Add_are_saved_by_Add()
    {
        var player = NewPlayer();
        Assert.True(player.UpdateProfile("Ana Souza", null, "mtg", Formats("commander"), Formats("commander")).IsSuccess);
        await Persist(player);

        Assert.Equal(new HashSet<FormatId>(Formats("commander")), [.. (await Reload(player.Id))!.Formats.Ids]);
    }

    [Fact]
    public async Task Player_contract_an_inactive_format_already_chosen_is_preserved_when_the_profile_is_saved_again_REQ_046()
    {
        var player = NewPlayer();
        await Persist(player);
        await Change(player.Id, p => Assert.True(
            p.UpdateProfile("Ana Souza", null, "mtg", Formats("commander", "oldformat"), Formats("commander", "oldformat")).IsSuccess));

        // "oldformat" deixou o catalogo ativo: o Jogador troca so o que enxerga e o formato inativo continua gravado.
        await Change(player.Id, p => Assert.True(
            p.UpdateProfile("Ana Souza", null, "mtg", Formats("modern"), Formats("commander", "modern")).IsSuccess));

        Assert.Equal(
            new HashSet<FormatId>(Formats("modern", "oldformat")),
            [.. (await Reload(player.Id))!.Formats.Ids]);
    }

    [Fact]
    public async Task Player_contract_Save_persists_the_rehashed_password()
    {
        var player = NewPlayer();
        await Persist(player);
        var newHash = PasswordHash.Parse(OtherStored).Value;

        await Change(player.Id, p => Assert.True(p.RehashPassword(newHash).IsSuccess));

        Assert.Equal(newHash.ToStoredString(), (await Reload(player.Id))!.PasswordHash?.ToStoredString());
    }

    [Fact]
    public async Task Player_contract_an_issued_reset_token_is_found_by_its_hash()
    {
        var player = NewPlayer();
        await Persist(player);

        await Change(player.Id, p => Assert.True(p.IssuePasswordReset(Token(1), Now).IsSuccess));

        await using var reader = Fixture.CreateScope();
        var found = await reader.Players.FindByResetTokenHash(Token(1));
        Assert.NotNull(found);
        Assert.Equal(player.Id, found.Id);
        Assert.Equal(Now.AddMinutes(60), found.PasswordResetToken?.ExpiresAt);
        Assert.Null(await reader.Players.FindByResetTokenHash(Token(2)));
    }

    [Fact]
    public async Task Player_contract_issuing_a_new_reset_token_invalidates_the_previous_one()
    {
        var player = NewPlayer();
        await Persist(player);
        await Change(player.Id, p => Assert.True(p.IssuePasswordReset(Token(1), Now).IsSuccess));

        await Change(player.Id, p => Assert.True(p.IssuePasswordReset(Token(2), Now.AddMinutes(1)).IsSuccess));

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Players.FindByResetTokenHash(Token(1)));
        Assert.Equal(player.Id, (await reader.Players.FindByResetTokenHash(Token(2)))?.Id);
    }

    [Fact]
    public async Task Player_contract_a_reset_token_is_consumed_once_and_the_new_password_is_saved()
    {
        var player = NewPlayer();
        await Persist(player);
        await Change(player.Id, p => Assert.True(p.IssuePasswordReset(Token(1), Now).IsSuccess));
        var newHash = PasswordHash.Parse(OtherStored).Value;

        await Change(player.Id, p => Assert.True(p.ResetPassword(Token(1), newHash, Now.AddMinutes(5)).IsSuccess));

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Players.FindByResetTokenHash(Token(1)));
        var loaded = await reader.Players.GetById(player.Id);
        Assert.Equal(newHash.ToStoredString(), loaded!.PasswordHash?.ToStoredString());
        Assert.Null(loaded.PasswordResetToken);
    }

    [Fact]
    public async Task Player_contract_DeleteExpiredResetTokens_removes_only_expired_tokens_in_batches_and_keeps_the_players()
    {
        var expired = new[] { NewPlayer("e1@example.com"), NewPlayer("e2@example.com"), NewPlayer("e3@example.com") };
        var live = NewPlayer("live@example.com");
        for (var index = 0; index < expired.Length; index++)
        {
            var seed = (byte)(10 + index);
            await Persist(expired[index]);
            await Change(expired[index].Id, p => Assert.True(p.IssuePasswordReset(Token(seed), Now.AddHours(-3)).IsSuccess));
        }

        await Persist(live);
        await Change(live.Id, p => Assert.True(p.IssuePasswordReset(Token(50), Now).IsSuccess));
        await using var scope = Fixture.CreateScope();

        var first = await scope.Players.DeleteExpiredResetTokens(Now, 2);
        var second = await scope.Players.DeleteExpiredResetTokens(Now, 2);
        var third = await scope.Players.DeleteExpiredResetTokens(Now, 2);

        Assert.Equal([2, 1, 0], new[] { first, second, third });
        await using var reader = Fixture.CreateScope();
        foreach (var player in expired)
        {
            var loaded = await reader.Players.GetById(player.Id);
            Assert.NotNull(loaded);
            Assert.Null(loaded.PasswordResetToken);
        }

        Assert.Equal(live.Id, (await reader.Players.FindByResetTokenHash(Token(50)))?.Id);
    }

    [Fact]
    public async Task Player_contract_Save_persists_the_google_link_found_by_subject_afterwards()
    {
        var player = NewPlayer("link@example.com");
        await Persist(player);
        var identity = ExternalIdentity.Create(ExternalIdentity.GoogleProvider, "linked-sub").Value;

        await Change(player.Id, p => Assert.True(p.LinkGoogle(identity, p.Email, ProofFor(p)).IsSuccess));

        await using var reader = Fixture.CreateScope();
        var found = await reader.Players.FindByGoogleSubject("linked-sub");
        Assert.Equal(player.Id, found?.Id);
        Assert.Equal(identity, found?.ExternalIdentity);
    }

    [Fact]
    public async Task Player_contract_Remove_deletes_the_player_and_the_next_lookups_are_empty()
    {
        var player = NewGooglePlayer();
        await Persist(player);
        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Remove(loaded!, ct));

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Players.GetById(player.Id));
        Assert.Null(await reader.Players.FindByEmail(player.Email));
        Assert.False(await reader.Players.ExistsByEmail(player.Email));
        Assert.Null(await reader.Players.FindByGoogleSubject("google-sub-1"));
    }

    [Fact]
    public async Task Player_contract_Remove_frees_the_email_and_the_google_identity_for_a_new_registration()
    {
        var player = NewGooglePlayer("again@example.com", "again-sub");
        await Persist(player);
        await using (var scope = Fixture.CreateScope())
        {
            var loaded = await scope.Players.GetById(player.Id);
            await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Remove(loaded!, ct));
        }

        var again = NewGooglePlayer("again@example.com", "again-sub");
        await Persist(again);

        Assert.Equal(again.Id, (await Reload(again.Id))?.Id);
    }

    // Monta um Jogador com tudo o que ele possui (formato, avatar proprio, sessao e pendencia de vinculo), grava e o remove.
    private async Task<(Player Player, Session Session, PendingGoogleLink Pending)> RemoveAPlayerWithEverything()
    {
        var player = NewGooglePlayer();
        Assert.True(player.UpdateProfile("Gui Lima", null, "mtg", Formats("commander"), Formats("commander")).IsSuccess);
        player.SetCustomAvatar(AvatarKey);
        var session = Session.Start(player.Id, Token(7), false, Now);
        var pending = PendingGoogleLink.Create(Token(8), "other-sub", player.Email, player.Id, Now);
        await using (var setup = Fixture.CreateScope())
        {
            await setup.UnitOfWork.ExecuteAsync(async ct =>
            {
                await setup.Players.Add(player, ct);
                await setup.Avatars.SaveAsync(AvatarKey, Image, ct);
            });
            await setup.UnitOfWork.ExecuteAsync(async ct =>
            {
                await setup.Sessions.Add(session, ct);
                await setup.Pendings.AddAsync(pending, ct);
            });
        }

        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Remove(loaded!, ct));
        return (player, session, pending);
    }

    [Fact]
    public async Task Player_contract_Remove_deletes_the_player_with_its_formats_and_google_identity()
    {
        var (player, _, _) = await RemoveAPlayerWithEverything();

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Players.GetById(player.Id));
        Assert.Null(await reader.Players.FindByGoogleSubject("google-sub-1"));
    }

    [Fact]
    public async Task Player_contract_Remove_deletes_the_sessions_of_the_player()
    {
        var (_, session, _) = await RemoveAPlayerWithEverything();

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Sessions.FindById(session.Id));
        Assert.Null(await reader.Sessions.FindByTokenHash(Token(7)));
    }

    [Fact]
    public async Task Player_contract_Remove_deletes_the_pending_google_link_of_the_player()
    {
        await RemoveAPlayerWithEverything();

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Pendings.FindAsync(Token(8)));
    }

    [Fact]
    public async Task Player_contract_Remove_deletes_the_custom_avatar_of_the_player()
    {
        await RemoveAPlayerWithEverything();

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Avatars.ReadAsync(AvatarKey));
    }
}
