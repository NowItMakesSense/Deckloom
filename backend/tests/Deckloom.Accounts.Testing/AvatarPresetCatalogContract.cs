using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

// T-145 (retro item 1): contrato de IAvatarPresetCatalog (REQ-017, REQ-018). Roda contra o fake e contra EfAvatarPresetCatalog.
// O cache de 5 min do adapter so pode atrasar a leitura ate o prazo: depois dele a mudanca tem de aparecer, e a primeira
// leitura nunca e mascarada. Cobertura da porta: ListActiveAsync, GetDefaultAsync, ContainsActiveAsync.
public abstract class AvatarPresetCatalogContract : IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly List<IDisposable> _harnesses = [];

    protected abstract CatalogHarness<IAvatarPresetCatalog> Create(IReadOnlyList<AvatarPresetEntry> seed);

    public void Dispose()
    {
        _harnesses.ForEach(h => h.Dispose());
        GC.SuppressFinalize(this);
    }

    private CatalogHarness<IAvatarPresetCatalog> Seeded()
    {
        var harness = Create(CatalogContractSeed.Presets);
        _harnesses.Add(harness);
        return harness;
    }

    [Fact]
    public async Task Preset_contract_ListActive_returns_only_active_presets_in_a_stable_order()
    {
        var harness = Seeded();

        var first = await harness.Catalog.ListActiveAsync();
        var second = await harness.Catalog.ListActiveAsync();

        Assert.Equal(["guerreiro", "mago-azul", "arqueira"], first.Select(p => p.Id));
        Assert.Equal(first.Select(p => p.Id), second.Select(p => p.Id));
    }

    [Fact]
    public async Task Preset_contract_ListActive_carries_label_image_path_and_default_flag()
    {
        var harness = Seeded();

        var items = await harness.Catalog.ListActiveAsync();

        var mage = Assert.Single(items, p => p.Id == "mago-azul");
        Assert.Equal("Mago azul", mage.Label);
        Assert.Equal("/avatars/presets/mago-azul.svg", mage.ImagePath);
        Assert.True(mage.IsDefault);
        Assert.Single(items, p => p.IsDefault);
    }

    [Fact]
    public async Task Preset_contract_ContainsActive_accepts_active_and_refuses_inactive_unknown_and_differently_cased_ids()
    {
        var harness = Seeded();

        Assert.True(await harness.Catalog.ContainsActiveAsync("guerreiro"));
        Assert.False(await harness.Catalog.ContainsActiveAsync("antigo"));
        Assert.False(await harness.Catalog.ContainsActiveAsync("desconhecido"));
        Assert.False(await harness.Catalog.ContainsActiveAsync("Guerreiro"));
        Assert.False(await harness.Catalog.ContainsActiveAsync(string.Empty));
    }

    [Fact]
    public async Task Preset_contract_the_default_preset_exists_and_is_active()
    {
        var harness = Seeded();

        var preset = await harness.Catalog.GetDefaultAsync();

        Assert.Equal("mago-azul", preset.Id);
        Assert.True(preset.IsDefault);
        Assert.True(await harness.Catalog.ContainsActiveAsync(preset.Id));
        Assert.Contains(await harness.Catalog.ListActiveAsync(), p => p.Id == preset.Id);
    }

    [Fact]
    public async Task Preset_contract_a_deactivated_preset_leaves_the_catalog_once_the_cache_lifetime_has_passed()
    {
        var harness = Seeded();
        Assert.True(await harness.Catalog.ContainsActiveAsync("guerreiro"));

        await harness.SetActive("guerreiro", false);
        harness.Clock.Advance(CacheLifetime);

        Assert.False(await harness.Catalog.ContainsActiveAsync("guerreiro"));
        Assert.Equal(["mago-azul", "arqueira"], (await harness.Catalog.ListActiveAsync()).Select(p => p.Id));
    }

    [Fact]
    public async Task Preset_contract_an_activated_preset_enters_the_catalog_once_the_cache_lifetime_has_passed()
    {
        var harness = Seeded();
        Assert.False(await harness.Catalog.ContainsActiveAsync("antigo"));

        await harness.SetActive("antigo", true);
        harness.Clock.Advance(CacheLifetime);

        Assert.True(await harness.Catalog.ContainsActiveAsync("antigo"));
        Assert.Equal(
            ["guerreiro", "mago-azul", "arqueira", "antigo"],
            (await harness.Catalog.ListActiveAsync()).Select(p => p.Id));
    }

    [Fact]
    public async Task Preset_contract_a_change_made_before_the_first_read_is_never_masked()
    {
        var harness = Seeded();

        await harness.SetActive("antigo", true);

        Assert.True(await harness.Catalog.ContainsActiveAsync("antigo"));
    }
}
