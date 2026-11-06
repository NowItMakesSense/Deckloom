using System.Net;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// T-081 (RED): endpoints publicos de catalogo e legal (design, tabela "Endpoints"):
//   GET /legal/terms             -> 200 {version, termsUrl, privacyUrl}                      (REQ-033)
//   GET /catalog/formats         -> 200 [{id, name}] (ativos, ordenados)                     (REQ-037)
//   GET /catalog/favorite-tcgs   -> 200 [{id: "mtg", name: "Magic: The Gathering"}]          (REQ-017)
//   GET /catalog/avatar-presets  -> 200 [{id, label, imageUrl, isDefault}] (ativos)          (REQ-018)
// Prefixo /api/v1 igual ao de /api/v1/csrf. Sem cookie de sessao. Os catalogos vem dos fakes do Testing.
// Lacuna: o design nao fixa o tratamento de catalogo indisponivel (503 unavailable), entao nao ha teste dele.
public sealed class CatalogEndpointsTests : IDisposable
{
    private const string TermsUrl = "https://deckloom.example.test/termos";
    private const string PrivacyUrl = "https://deckloom.example.test/privacidade";

    private readonly InMemoryFormatCatalog _formats = new(
    [
        new FormatEntry(new FormatId("pioneer"), "Pioneer", 30, true),
        new FormatEntry(new FormatId("standard"), "Standard", 10, true),
        new FormatEntry(new FormatId("modern"), "Modern", 20, true),
        new FormatEntry(new FormatId("legacy"), "Legacy", 5, false),
    ]);

    private readonly InMemoryAvatarPresetCatalog _presets = new(
    [
        new AvatarPresetEntry("p-c", "Cavaleiro", "/avatars/c.webp", 30, false, true),
        new AvatarPresetEntry("p-a", "Mago", "/avatars/a.webp", 10, true, true),
        new AvatarPresetEntry("p-b", "Elfo", "/avatars/b.webp", 20, false, true),
        new AvatarPresetEntry("p-x", "Antigo", "/avatars/x.webp", 1, false, false),
    ]);

    private readonly CatalogFactory _factory;

    public CatalogEndpointsTests() => _factory = new CatalogFactory(_formats, _presets);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task REQ037_formats_lists_only_active_ordered_by_sort_order_with_id_and_name_only()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/api/v1/catalog/formats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadArray(response);
        Assert.Equal(["standard", "modern", "pioneer"], items.Select(i => i.GetProperty("id").GetString()));
        Assert.Equal(["Standard", "Modern", "Pioneer"], items.Select(i => i.GetProperty("name").GetString()));
        Assert.All(items, i => Assert.Equal(["id", "name"], PropertyNames(i)));
    }

    [Fact]
    public async Task REQ037_format_added_to_the_catalog_appears_without_code_change()
    {
        _formats.Add(new FormatEntry(new FormatId("pauper"), "Pauper", 40, true));
        using var client = NewClient();

        var response = await client.GetAsync("/api/v1/catalog/formats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadArray(response);
        Assert.Equal("pauper", items[^1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task REQ017_favorite_tcgs_lists_only_magic_the_gathering()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/api/v1/catalog/favorite-tcgs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(await ReadArray(response));
        Assert.Equal("mtg", item.GetProperty("id").GetString());
        Assert.Equal("Magic: The Gathering", item.GetProperty("name").GetString());
        Assert.Equal(["id", "name"], PropertyNames(item));
    }

    [Fact]
    public async Task REQ018_avatar_presets_lists_only_active_ordered_with_default_flag_and_image_url()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/api/v1/catalog/avatar-presets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadArray(response);
        Assert.Equal(["p-a", "p-b", "p-c"], items.Select(i => i.GetProperty("id").GetString()));
        Assert.Equal(["Mago", "Elfo", "Cavaleiro"], items.Select(i => i.GetProperty("label").GetString()));
        Assert.Equal(
            ["/avatars/a.webp", "/avatars/b.webp", "/avatars/c.webp"],
            items.Select(i => i.GetProperty("imageUrl").GetString()));
        Assert.Equal([true, false, false], items.Select(i => i.GetProperty("isDefault").GetBoolean()));
        Assert.All(items, i => Assert.Equal(["id", "imageUrl", "isDefault", "label"], PropertyNames(i)));
    }

    [Fact]
    public async Task REQ033_legal_terms_returns_version_and_urls_only()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/api/v1/legal/terms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("2026-10", root.GetProperty("version").GetString());
        Assert.Equal(TermsUrl, root.GetProperty("termsUrl").GetString());
        Assert.Equal(PrivacyUrl, root.GetProperty("privacyUrl").GetString());
        Assert.Equal(["privacyUrl", "termsUrl", "version"], PropertyNames(root));
    }

    [Theory]
    [InlineData("/api/v1/legal/terms")]
    [InlineData("/api/v1/catalog/formats")]
    [InlineData("/api/v1/catalog/favorite-tcgs")]
    [InlineData("/api/v1/catalog/avatar-presets")]
    public async Task REQ017_REQ018_REQ033_REQ037_catalog_and_legal_are_public_json_without_session(string path)
    {
        using var client = NewClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static async Task<List<JsonElement>> ReadArray(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private sealed class CatalogFactory(InMemoryFormatCatalog formats, InMemoryAvatarPresetCatalog presets)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.RemoveAll<IFormatCatalog>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<ITermsVersionProvider>();
                services.AddSingleton<IFormatCatalog>(formats);
                services.AddSingleton<IAvatarPresetCatalog>(presets);
                services.AddSingleton<ITermsVersionProvider>(
                    new FixedTermsVersionProvider("2026-10", new Uri(TermsUrl), new Uri(PrivacyUrl)));
            });
        }
    }
}
