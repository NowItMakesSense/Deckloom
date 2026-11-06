using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-017 (REQ-018, REQ-021; J6): comportamentos de avatar do Player.
// Design (emenda 2026-10-23, "Avatar"): texto bruto, Result<Player>; em branco -> invalid com Field "avatar", sem
// alterar estado nem emitir evento; escolher o avatar ja definido e sucesso sem evento; toda troca real emite
// AvatarChanged(PlayerId, PreviousStorageKey?), com a chave so quando o avatar anterior era customizado.
public class PlayerAvatarTests
{
    private const string Current = "2026-10-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

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

    private static Player ACustomPlayer(string key = "avatars/ana-1")
    {
        var player = APlayer();
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        player.ClearDomainEvents();
        return player;
    }

    private static AvatarChanged TheOnlyEvent(Player player) =>
        Assert.IsType<AvatarChanged>(Assert.Single(player.DomainEvents));

    [Fact]
    public void REQ_018_choosing_a_preset_sets_the_avatar()
    {
        var player = APlayer();

        var result = player.ChooseAvatarPreset("mago-azul");

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
        Assert.Equal(PresetAvatar.Create("mago-azul").Value, player.Avatar);
    }

    [Fact]
    public void REQ_021_setting_a_custom_avatar_sets_the_avatar()
    {
        var player = APlayer();

        var result = player.SetCustomAvatar("avatars/ana-1");

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
        Assert.Equal(CustomAvatar.Create("avatars/ana-1").Value, player.Avatar);
    }

    [Fact]
    public void REQ_021_custom_to_another_custom_emits_AvatarChanged_with_the_previous_key()
    {
        var player = ACustomPlayer("avatars/ana-1");

        player.SetCustomAvatar("avatars/ana-2");

        var changed = TheOnlyEvent(player);
        Assert.Equal(player.Id, changed.PlayerId);
        Assert.Equal("avatars/ana-1", changed.PreviousStorageKey);
        Assert.Equal(CustomAvatar.Create("avatars/ana-2").Value, player.Avatar);
    }

    [Fact]
    public void REQ_021_custom_to_preset_emits_AvatarChanged_with_the_previous_key()
    {
        var player = ACustomPlayer("avatars/ana-1");

        player.ChooseAvatarPreset("mago-azul");

        var changed = TheOnlyEvent(player);
        Assert.Equal(player.Id, changed.PlayerId);
        Assert.Equal("avatars/ana-1", changed.PreviousStorageKey);
        Assert.Equal(PresetAvatar.Create("mago-azul").Value, player.Avatar);
    }

    [Fact]
    public void REQ_018_preset_to_preset_emits_AvatarChanged_without_a_previous_key()
    {
        var player = APlayer();

        player.ChooseAvatarPreset("mago-azul");

        var changed = TheOnlyEvent(player);
        Assert.Equal(player.Id, changed.PlayerId);
        Assert.Null(changed.PreviousStorageKey);
    }

    [Fact]
    public void REQ_021_preset_to_custom_emits_AvatarChanged_without_a_previous_key()
    {
        var player = APlayer();

        player.SetCustomAvatar("avatars/ana-1");

        var changed = TheOnlyEvent(player);
        Assert.Equal(player.Id, changed.PlayerId);
        Assert.Null(changed.PreviousStorageKey);
    }

    [Fact]
    public void REQ_018_choosing_the_same_preset_is_a_success_without_event()
    {
        var player = APlayer();

        var result = player.ChooseAvatarPreset("preset-01");

        Assert.True(result.IsSuccess);
        Assert.Equal(PresetAvatar.Create("preset-01").Value, player.Avatar);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_021_setting_the_same_custom_key_is_a_success_without_event()
    {
        var player = ACustomPlayer("avatars/ana-1");

        var result = player.SetCustomAvatar("avatars/ana-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(CustomAvatar.Create("avatars/ana-1").Value, player.Avatar);
        Assert.Empty(player.DomainEvents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_018_a_blank_preset_is_invalid_and_changes_nothing(string? presetId)
    {
        var player = ACustomPlayer("avatars/ana-1");

        var result = player.ChooseAvatarPreset(presetId);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("invalid", error.Code);
        Assert.Equal("avatar", error.Field);
        Assert.Equal(CustomAvatar.Create("avatars/ana-1").Value, player.Avatar);
        Assert.Empty(player.DomainEvents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_021_a_blank_storage_key_is_invalid_and_changes_nothing(string? storageKey)
    {
        var player = APlayer();

        var result = player.SetCustomAvatar(storageKey);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("invalid", error.Code);
        Assert.Equal("avatar", error.Field);
        Assert.Equal(PresetAvatar.Create("preset-01").Value, player.Avatar);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_021_the_event_carries_only_identifiers()
    {
        var properties = typeof(AvatarChanged)
            .GetProperties()
            .Where(property => property.DeclaringType == typeof(AvatarChanged))
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Equal(
            [("PlayerId", typeof(PlayerId)), ("PreviousStorageKey", typeof(string))],
            properties);
    }
}
