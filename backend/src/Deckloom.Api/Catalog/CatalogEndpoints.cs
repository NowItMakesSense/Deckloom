using Deckloom.Accounts.Application;

namespace Deckloom.Api.Catalog;

public sealed record LegalTermsResponse(string Version, string TermsUrl, string PrivacyUrl);

public sealed record CatalogItemResponse(string Id, string Name);

public sealed record AvatarPresetResponse(string Id, string Label, string ImageUrl, bool IsDefault);

public static class CatalogEndpoints
{
    private const string FavoriteTcgId = "mtg";
    private const string FavoriteTcgName = "Magic: The Gathering";

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1");

        api.MapGet("/legal/terms", (ITermsVersionProvider terms) =>
            Results.Ok(new LegalTermsResponse(
                terms.CurrentVersion,
                terms.TermsUrl.ToString(),
                terms.PrivacyPolicyUrl.ToString())));

        api.MapGet("/catalog/formats", async (IFormatCatalog formats, CancellationToken ct) =>
        {
            var items = await formats.ListActiveAsync(ct);
            return Results.Ok(items.Select(f => new CatalogItemResponse(f.Id.Value, f.Name)));
        });

        api.MapGet("/catalog/favorite-tcgs", () =>
            Results.Ok(new[] { new CatalogItemResponse(FavoriteTcgId, FavoriteTcgName) }));

        api.MapGet("/catalog/avatar-presets", async (IAvatarPresetCatalog presets, CancellationToken ct) =>
        {
            var items = await presets.ListActiveAsync(ct);
            return Results.Ok(items.Select(p => new AvatarPresetResponse(p.Id, p.Label, p.ImagePath, p.IsDefault)));
        });

        return routes;
    }
}
