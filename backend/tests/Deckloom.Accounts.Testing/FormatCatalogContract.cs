using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

// T-145 (retro item 1): contrato de IFormatCatalog (REQ-037, REQ-046). Roda contra o fake e contra EfFormatCatalog (catalogo
// de Sugestao de Deck, somente leitura). A porta tem so ListActiveAsync e ContainsAsync; "obter por id" do enunciado da
// tarefa nao existe na porta. Cobertura: ListActiveAsync, ContainsAsync.
public abstract class FormatCatalogContract : IDisposable
{
    private readonly List<IDisposable> _harnesses = [];

    protected abstract CatalogHarness<IFormatCatalog> Create(IReadOnlyList<FormatEntry> seed);

    public void Dispose()
    {
        _harnesses.ForEach(h => h.Dispose());
        GC.SuppressFinalize(this);
    }

    private CatalogHarness<IFormatCatalog> Seeded()
    {
        var harness = Create(CatalogContractSeed.Formats);
        _harnesses.Add(harness);
        return harness;
    }

    [Fact]
    public async Task Format_contract_ListActive_returns_only_active_formats_ordered_by_sort_order_with_their_names()
    {
        var harness = Seeded();

        var items = await harness.Catalog.ListActiveAsync();

        Assert.Equal(["standard", "modern", "pauper", "commander"], items.Select(f => f.Id.Value));
        Assert.Equal(["Standard", "Modern", "Pauper", "Commander"], items.Select(f => f.Name));
    }

    [Fact]
    public async Task Format_contract_ListActive_breaks_ties_by_ordinal_id()
    {
        var harness = Create(
        [
            new(new FormatId("b"), "B", 1, true),
            new(new FormatId("a"), "A", 1, true),
            new(new FormatId("c"), "C", 0, true),
        ]);
        _harnesses.Add(harness);

        var items = await harness.Catalog.ListActiveAsync();

        Assert.Equal(["c", "a", "b"], items.Select(f => f.Id.Value));
    }

    [Fact]
    public async Task Format_contract_Contains_is_true_only_for_active_formats()
    {
        var harness = Seeded();

        Assert.True(await harness.Catalog.ContainsAsync(new FormatId("modern")));
        Assert.False(await harness.Catalog.ContainsAsync(new FormatId("legacy")));
        Assert.False(await harness.Catalog.ContainsAsync(new FormatId("inexistente")));
        Assert.False(await harness.Catalog.ContainsAsync(new FormatId("Modern")));
        Assert.False(await harness.Catalog.ContainsAsync(new FormatId(string.Empty)));
    }

    [Fact]
    public async Task Format_contract_an_inactive_format_is_recognized_as_inactive_REQ_046()
    {
        var harness = Seeded();

        var listed = await harness.Catalog.ListActiveAsync();

        Assert.DoesNotContain(listed, f => f.Id == new FormatId("legacy"));
        Assert.False(await harness.Catalog.ContainsAsync(new FormatId("legacy")));
    }

    [Fact]
    public async Task Format_contract_activation_changes_are_visible_immediately_there_is_no_cache()
    {
        var harness = Seeded();
        Assert.False(await harness.Catalog.ContainsAsync(new FormatId("legacy")));

        await harness.SetActive("legacy", true);
        await harness.SetActive("modern", false);

        Assert.True(await harness.Catalog.ContainsAsync(new FormatId("legacy")));
        Assert.False(await harness.Catalog.ContainsAsync(new FormatId("modern")));
        Assert.Equal(
            ["standard", "pauper", "commander", "legacy"],
            (await harness.Catalog.ListActiveAsync()).Select(f => f.Id.Value));
    }

    [Fact]
    public void Format_contract_the_port_is_read_only_it_has_no_write_method()
    {
        var methods = typeof(IFormatCatalog).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["ContainsAsync", "ListActiveAsync"], methods);
    }
}
