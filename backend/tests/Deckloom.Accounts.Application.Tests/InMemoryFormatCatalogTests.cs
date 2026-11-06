using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-026 (REQ-037, REQ-046): catalogo de Formatos em memoria.
public class InMemoryFormatCatalogTests
{
    private static FormatId Id(string v) => new(v);

    private static InMemoryFormatCatalog Catalog() => new(
    [
        new FormatEntry(Id("modern"), "Modern", 2, true),
        new FormatEntry(Id("standard"), "Standard", 1, true),
        new FormatEntry(Id("pauper"), "Pauper", 5, false),
    ]);

    private static async Task<string[]> ActiveIds(InMemoryFormatCatalog c) =>
        (await c.ListActiveAsync()).Select(f => f.Id.Value).ToArray();

    [Fact]
    public async Task Lists_only_active_formats_ordered_by_sort_order()
    {
        var items = await Catalog().ListActiveAsync();

        Assert.Equal([new FormatItem(Id("standard"), "Standard"), new FormatItem(Id("modern"), "Modern")], items);
    }

    [Fact]
    public async Task Ties_on_sort_order_are_broken_by_ordinal_id()
    {
        var c = new InMemoryFormatCatalog(
        [
            new FormatEntry(Id("b"), "B", 1, true),
            new FormatEntry(Id("A"), "A", 1, true),
            new FormatEntry(Id("a"), "a", 1, true),
        ]);

        Assert.Equal(["A", "a", "b"], await ActiveIds(c));
    }

    [Fact]
    public async Task Contains_is_true_only_for_active_formats()
    {
        var c = Catalog();

        Assert.True(await c.ContainsAsync(Id("standard")));
        Assert.False(await c.ContainsAsync(Id("pauper")));
        Assert.False(await c.ContainsAsync(Id("unknown")));
    }

    [Fact]
    public async Task Deactivate_removes_from_listing_and_contains_then_activate_restores()
    {
        var c = Catalog();

        c.Deactivate(Id("standard"));
        Assert.Equal(["modern"], await ActiveIds(c));
        Assert.False(await c.ContainsAsync(Id("standard")));

        c.Activate(Id("standard"));
        Assert.Equal(["standard", "modern"], await ActiveIds(c));
        Assert.True(await c.ContainsAsync(Id("standard")));
    }

    [Fact]
    public async Task Activating_an_inactive_seed_makes_it_listed_in_order()
    {
        var c = Catalog();

        c.Activate(Id("pauper"));

        Assert.Equal(["standard", "modern", "pauper"], await ActiveIds(c));
    }

    [Fact]
    public async Task Added_format_appears_without_code_change()
    {
        var c = Catalog();

        c.Add(new FormatEntry(Id("legacy"), "Legacy", 0, true));

        Assert.Equal(["legacy", "standard", "modern"], await ActiveIds(c));
    }

    [Fact]
    public void Activate_and_deactivate_unknown_id_throw_KeyNotFound()
    {
        var c = Catalog();

        Assert.Throws<KeyNotFoundException>(() => c.Activate(Id("nope")));
        Assert.Throws<KeyNotFoundException>(() => c.Deactivate(Id("nope")));
    }
}
