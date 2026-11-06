using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-016 (REQ-014, REQ-015, REQ-016, REQ-040, REQ-046; J5): Player.UpdateProfile, tudo ou nada.
// Design (emenda 2026-10-23, "Edicao de Perfil"): valores brutos; chama DisplayName/Bio/FavoriteTcg.Create e
// FormatPreferences.Update; falha devolve TODOS os erros com Field do contrato (displayName, bio, favoriteTcg,
// formatIds) sem alterar o estado; ProfileUpdated(PlayerId, ChangedFields) na ordem fixa displayName, bio,
// favoriteTcg, formatIds; salvar sem mudanca e sucesso sem evento; activeFormatIds e o catalogo ativo.
// Contrato assumido para o GREEN: Result<Player> UpdateProfile(string?, string?, string?, IEnumerable<FormatId>,
// IReadOnlyCollection<FormatId>) devolvendo o proprio Player; Player.Bio : Bio?.
public class UpdateProfileTests
{
    private const string Current = "2026-10-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly FormatId[] AllActive = Ids("standard", "modern", "pioneer", "commander", "pauper");
    private static readonly FormatId[] ActiveWithoutPauper = Ids("standard", "modern", "pioneer", "commander");

    private static FormatId[] Ids(params string[] values) => [.. values.Select(value => new FormatId(value))];

    private static IEnumerable<string> Values(IEnumerable<FormatId> ids) =>
        ids.Select(id => id.Value).Order(StringComparer.Ordinal);

    private static Player APlayer()
    {
        var player = Player.RegisterWithPassword(
            Email.Create("ana@example.com").Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana").Value,
            Current,
            Current,
            true,
            true,
            Now,
            "preset-01").Value;
        player.ClearDomainEvents();
        return player;
    }

    private static Player AnEditedPlayer()
    {
        var player = APlayer();
        Assert.True(player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("modern", "pauper"), AllActive).IsSuccess);
        player.ClearDomainEvents();
        return player;
    }

    private static void AssertUnchanged(Player player)
    {
        Assert.Equal("Ana Mage", player.DisplayName.Value);
        Assert.Equal("Jogo Commander", player.Bio?.Value);
        Assert.Equal(FavoriteTcg.Default, player.FavoriteTcg);
        Assert.Equal(["modern", "pauper"], Values(player.Formats.Ids));
        Assert.Empty(player.DomainEvents);
    }

    // REQ-014

    [Fact]
    public void REQ_014_valid_change_persists_name_bio_tcg_and_formats()
    {
        var player = APlayer();

        var result = player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("commander"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana Mage", player.DisplayName.Value);
        Assert.Equal("Jogo Commander", player.Bio?.Value);
        Assert.Equal(FavoriteTcg.Default, player.FavoriteTcg);
        Assert.Equal(["commander"], Values(player.Formats.Ids));
    }

    [Fact]
    public void REQ_014_valid_change_emits_profile_updated_with_the_changed_fields_in_fixed_order()
    {
        var player = APlayer();

        player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("commander"), AllActive);

        var evt = Assert.IsType<ProfileUpdated>(Assert.Single(player.DomainEvents));
        Assert.Equal(player.Id, evt.PlayerId);
        Assert.Equal(["displayName", "bio", "formatIds"], evt.ChangedFields);
    }

    [Fact]
    public void REQ_014_only_the_fields_that_changed_are_reported()
    {
        var player = AnEditedPlayer();

        player.UpdateProfile("Ana Mage", "Outra bio", "mtg", Ids("modern", "pauper"), AllActive);

        var evt = Assert.IsType<ProfileUpdated>(Assert.Single(player.DomainEvents));
        Assert.Equal(["bio"], evt.ChangedFields);
    }

    [Fact]
    public void REQ_014_formats_in_a_different_order_are_not_a_change()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("pauper", "modern"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_014_saving_without_any_change_succeeds_and_emits_no_event()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("modern", "pauper"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_014_values_are_normalized_before_comparing_so_padding_is_not_a_change()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("  Ana Mage  ", "Jogo Commander\r\n", "mtg", Ids("modern", "pauper"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Empty(player.DomainEvents);
    }

    // REQ-015 / REQ-040: tudo ou nada

    [Theory]
    [InlineData("Ab", "length")]
    [InlineData("Ana\nMage", "invalid_characters")]
    [InlineData(null, "length")]
    public void REQ_015_invalid_display_name_rejects_everything_and_keeps_the_state(string? name, string code)
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile(name, "Nova bio", "mtg", Ids("commander"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError(code, "displayName"), Assert.Single(result.Errors));
        AssertUnchanged(player);
    }

    [Fact]
    public void REQ_040_bio_with_a_control_character_rejects_everything_and_keeps_the_state()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Nome Novo", "nul\0", "mtg", Ids("commander"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid_characters", "bio"), Assert.Single(result.Errors));
        AssertUnchanged(player);
    }

    [Fact]
    public void REQ_015_bio_over_280_characters_is_too_long()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Nome Novo", new string('a', 281), "mtg", Ids("commander"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("too_long", "bio"), Assert.Single(result.Errors));
        AssertUnchanged(player);
    }

    [Theory]
    [InlineData("Pokemon")]
    [InlineData("MTG")]
    [InlineData("")]
    [InlineData(null)]
    public void REQ_015_unsupported_tcg_rejects_everything_and_keeps_the_state(string? tcg)
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Nome Novo", "Nova bio", tcg, Ids("commander"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid", "favoriteTcg"), Assert.Single(result.Errors));
        AssertUnchanged(player);
    }

    [Fact]
    public void REQ_015_unknown_format_rejects_everything_and_keeps_the_state()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Nome Novo", "Nova bio", "mtg", Ids("commander", "FormatoInventado"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid", "formatIds"), Assert.Single(result.Errors));
        AssertUnchanged(player);
    }

    [Fact]
    public void REQ_046_new_inactive_format_is_invalid()
    {
        var player = APlayer();

        var result = player.UpdateProfile("Ana", null, "mtg", Ids("pauper"), ActiveWithoutPauper);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid", "formatIds"), Assert.Single(result.Errors));
        Assert.Empty(player.Formats.Ids);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_015_every_invalid_field_is_reported_in_fixed_order()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ab", new string('a', 281), "Pokemon", Ids("FormatoInventado"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal(
            [
                new DomainError("length", "displayName"),
                new DomainError("too_long", "bio"),
                new DomainError("invalid", "favoriteTcg"),
                new DomainError("invalid", "formatIds"),
            ],
            result.Errors);
        Assert.Equal(result.Errors[0], result.Error);
        AssertUnchanged(player);
    }

    [Fact]
    public void REQ_015_a_valid_field_next_to_an_invalid_one_is_not_applied()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Nome Novo", "Nova bio", "mtg", Ids("FormatoInventado"), AllActive);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(result.Errors, error => error.Field is "displayName" or "bio" or "favoriteTcg");
        AssertUnchanged(player);
    }

    // REQ-016

    [Fact]
    public void REQ_016_empty_bio_is_accepted_and_becomes_absent()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ana Mage", string.Empty, "mtg", Ids("modern", "pauper"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Null(player.Bio);
        var evt = Assert.IsType<ProfileUpdated>(Assert.Single(player.DomainEvents));
        Assert.Equal(["bio"], evt.ChangedFields);
    }

    [Fact]
    public void REQ_016_null_bio_is_accepted()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ana Mage", null, "mtg", Ids("modern", "pauper"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Null(player.Bio);
    }

    [Fact]
    public void REQ_016_empty_formats_are_accepted_and_clear_the_active_selection()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", [], AllActive);

        Assert.True(result.IsSuccess);
        Assert.Empty(player.Formats.Ids);
    }

    [Fact]
    public void REQ_016_profile_with_no_bio_and_no_formats_is_accepted_from_the_start()
    {
        var player = APlayer();

        var result = player.UpdateProfile("Ana", null, "mtg", [], AllActive);

        Assert.True(result.IsSuccess);
        Assert.Null(player.Bio);
        Assert.Empty(player.Formats.Ids);
        Assert.Empty(player.DomainEvents);
    }

    // REQ-046

    [Fact]
    public void REQ_046_inactive_format_is_preserved_when_the_request_omits_it()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("modern"), ActiveWithoutPauper);

        Assert.True(result.IsSuccess);
        Assert.Equal(["modern", "pauper"], Values(player.Formats.Ids));
        Assert.Equal(["modern"], Values(player.Formats.Visible(ActiveWithoutPauper)));
    }

    [Fact]
    public void REQ_046_inactive_format_reappears_when_it_is_reactivated()
    {
        var player = AnEditedPlayer();
        player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("modern"), ActiveWithoutPauper);

        Assert.Equal(["modern", "pauper"], Values(player.Formats.Visible(AllActive)));
    }

    [Fact]
    public void REQ_046_resending_a_stored_inactive_format_is_accepted()
    {
        var player = AnEditedPlayer();

        var result = player.UpdateProfile("Ana Mage", "Jogo Commander", "mtg", Ids("modern", "pauper"), ActiveWithoutPauper);

        Assert.True(result.IsSuccess);
        Assert.Equal(["modern", "pauper"], Values(player.Formats.Ids));
    }

    // REQ-037

    [Fact]
    public void REQ_037_repeated_display_name_is_accepted()
    {
        var first = APlayer();
        var second = APlayer();
        first.UpdateProfile("Ana Mage", null, "mtg", [], AllActive);

        var result = second.UpdateProfile("Ana Mage", null, "mtg", [], AllActive);

        Assert.True(result.IsSuccess);
        Assert.Equal(first.DisplayName, second.DisplayName);
    }
}
