using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-044 (REQ-017, 018, 033, 037): consultas de catalogo. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do Logout, T-034: consulta sem erro possivel nao devolve Result; a saida e o valor direto):
//   sealed class ListFormats(IFormatCatalog)               Task<IReadOnlyList<FormatItem>> ExecuteAsync(CancellationToken ct = default)
//   sealed class ListAvatarPresets(IAvatarPresetCatalog)   Task<IReadOnlyList<AvatarPresetItem>> ExecuteAsync(CancellationToken ct = default)
//   sealed class ListFavoriteTcgs()                        Task<IReadOnlyList<FavoriteTcgItem>> ExecuteAsync(CancellationToken ct = default)
//   sealed class GetTermsInfo(ITermsVersionProvider)       Task<TermsInfo> ExecuteAsync(CancellationToken ct = default)
//   sealed record FavoriteTcgItem(string Id, string Name)  (contrato: [{id, name}], design.md linha 1559)
//   sealed record TermsInfo(string Version, Uri TermsUrl, Uri PrivacyUrl)  (contrato: {version, termsUrl, privacyUrl}, linha 1539)
// FormatItem e AvatarPresetItem ja existentes; DTOs de saida proprios ficam para o REFACTOR da tarefa.
public class CatalogQueriesTests
{
    private static FormatEntry Format(string id, string name, int order, bool active = true) =>
        new(new FormatId(id), name, order, active);

    private static InMemoryFormatCatalog InitialFormats() => new(
    [
        Format("pauper", "Pauper", 5),
        Format("standard", "Standard", 1),
        Format("commander", "Commander", 4),
        Format("modern", "Modern", 2),
        Format("pioneer", "Pioneer", 3),
    ]);

    [Fact]
    public async Task REQ037_list_formats_returns_the_initial_active_formats_in_catalog_order()
    {
        var formats = await new ListFormats(InitialFormats()).ExecuteAsync();

        Assert.Equal(["Standard", "Modern", "Pioneer", "Commander", "Pauper"], formats.Select(f => f.Name));
    }

    [Fact]
    public async Task REQ037_a_format_added_to_the_catalog_appears_without_code_change()
    {
        var catalog = InitialFormats();
        catalog.Add(Format("legacy", "Legacy", 6));

        var formats = await new ListFormats(catalog).ExecuteAsync();

        Assert.Equal(
            ["Standard", "Modern", "Pioneer", "Commander", "Pauper", "Legacy"],
            formats.Select(f => f.Name));
    }

    [Fact]
    public async Task REQ037_list_formats_omits_deactivated_formats()
    {
        var catalog = InitialFormats();
        catalog.Deactivate(new FormatId("pauper"));

        var formats = await new ListFormats(catalog).ExecuteAsync();

        Assert.DoesNotContain(formats, f => f.Name == "Pauper");
        Assert.Equal(4, formats.Count);
    }

    [Fact]
    public async Task REQ017_list_favorite_tcgs_offers_only_magic_the_gathering()
    {
        var tcgs = await new ListFavoriteTcgs().ExecuteAsync();

        var only = Assert.Single(tcgs);
        Assert.Equal("mtg", only.Id);
        Assert.Equal("Magic: The Gathering", only.Name);
    }

    [Fact]
    public async Task REQ018_list_avatar_presets_returns_active_presets_with_exactly_the_default_flagged()
    {
        var catalog = new InMemoryAvatarPresetCatalog(
        [
            new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
            new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
            new AvatarPresetEntry("preset-03", "Goblin", "/avatars/preset-03.png", 3, false, false),
        ]);

        var presets = await new ListAvatarPresets(catalog).ExecuteAsync();

        Assert.Equal(["preset-01", "preset-02"], presets.Select(p => p.Id));
        Assert.Equal("/avatars/preset-02.png", Assert.Single(presets, p => p.IsDefault).ImagePath);
        Assert.Equal("Planeswalker", Assert.Single(presets, p => p.IsDefault).Label);
    }

    [Fact]
    public async Task REQ033_get_terms_info_returns_the_current_version_and_the_provider_urls()
    {
        var provider = new FixedTermsVersionProvider(
            "2026-10-v1",
            new Uri("https://deckloom.app/termos"),
            new Uri("https://deckloom.app/privacidade"));

        var info = await new GetTermsInfo(provider).ExecuteAsync();

        Assert.Equal("2026-10-v1", info.Version);
        Assert.Equal(new Uri("https://deckloom.app/termos"), info.TermsUrl);
        Assert.Equal(new Uri("https://deckloom.app/privacidade"), info.PrivacyUrl);
    }
}
