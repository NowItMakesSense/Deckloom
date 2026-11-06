using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-026 (REQ-018, REQ-022): galeria de presets em memoria com exatamente um padrao ativo.
public class InMemoryAvatarPresetCatalogTests
{
    private static AvatarPresetEntry P(string id, int order, bool isDefault = false, bool active = true) =>
        new(id, id.ToUpperInvariant(), $"/avatars/presets/{id}.svg", order, isDefault, active);

    private static InMemoryAvatarPresetCatalog Catalog() =>
        new([P("b", 2), P("a", 1, isDefault: true), P("c", 3, active: false)]);

    private static async Task<string[]> ActiveIds(InMemoryAvatarPresetCatalog c) =>
        (await c.ListActiveAsync()).Select(p => p.Id).ToArray();

    [Fact]
    public async Task Lists_only_active_presets_ordered_with_default_flagged()
    {
        var items = await Catalog().ListActiveAsync();

        Assert.Equal(
            [
                new AvatarPresetItem("a", "A", "/avatars/presets/a.svg", true),
                new AvatarPresetItem("b", "B", "/avatars/presets/b.svg", false),
            ],
            items);
    }

    [Fact]
    public async Task Ties_on_sort_order_are_broken_by_ordinal_id()
    {
        var c = new InMemoryAvatarPresetCatalog([P("b", 1), P("a", 1, isDefault: true)]);

        Assert.Equal(["a", "b"], await ActiveIds(c));
    }

    [Fact]
    public async Task GetDefault_returns_the_single_default()
    {
        var item = await Catalog().GetDefaultAsync();

        Assert.Equal(new AvatarPresetItem("a", "A", "/avatars/presets/a.svg", true), item);
    }

    [Fact]
    public void Constructor_rejects_zero_defaults()
    {
        Assert.Throws<ArgumentException>(() => new InMemoryAvatarPresetCatalog([P("a", 1), P("b", 2)]));
    }

    [Fact]
    public void Constructor_rejects_two_active_defaults()
    {
        Assert.Throws<ArgumentException>(() =>
            new InMemoryAvatarPresetCatalog([P("a", 1, isDefault: true), P("b", 2, isDefault: true)]));
    }

    [Fact]
    public void Constructor_rejects_a_second_default_even_when_inactive()
    {
        // ux_avatar_preset_single_default (WHERE is_default) proibe dois padroes, ativos ou nao.
        Assert.Throws<ArgumentException>(() =>
            new InMemoryAvatarPresetCatalog(
                [P("a", 1, isDefault: true), P("b", 2, isDefault: true, active: false)]));
    }

    [Fact]
    public void Constructor_rejects_an_inactive_default()
    {
        Assert.Throws<ArgumentException>(() =>
            new InMemoryAvatarPresetCatalog([P("a", 1, isDefault: true, active: false), P("b", 2)]));
    }

    [Fact]
    public async Task ContainsActive_is_false_for_inactive_and_unknown()
    {
        var c = Catalog();

        Assert.True(await c.ContainsActiveAsync("b"));
        Assert.False(await c.ContainsActiveAsync("c"));
        Assert.False(await c.ContainsActiveAsync("zzz"));
    }

    [Fact]
    public async Task Deactivate_and_activate_change_listing_and_contains()
    {
        var c = Catalog();

        c.Deactivate("b");
        Assert.Equal(["a"], await ActiveIds(c));
        Assert.False(await c.ContainsActiveAsync("b"));

        c.Activate("b");
        c.Activate("c");
        Assert.Equal(["a", "b", "c"], await ActiveIds(c));
        Assert.True(await c.ContainsActiveAsync("c"));
    }

    [Fact]
    public async Task Deactivating_the_default_throws_and_keeps_state()
    {
        var c = Catalog();

        Assert.Throws<InvalidOperationException>(() => c.Deactivate("a"));

        Assert.Equal("a", (await c.GetDefaultAsync()).Id);
        Assert.True(await c.ContainsActiveAsync("a"));
    }

    [Fact]
    public void Unknown_id_throws_KeyNotFound()
    {
        var c = Catalog();

        Assert.Throws<KeyNotFoundException>(() => c.Activate("nope"));
        Assert.Throws<KeyNotFoundException>(() => c.Deactivate("nope"));
    }
}
