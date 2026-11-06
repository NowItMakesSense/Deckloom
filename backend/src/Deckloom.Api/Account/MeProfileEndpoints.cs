using Deckloom.Accounts.Application;
using Deckloom.Api.Auth;
using Deckloom.Api.Problems;
using Deckloom.Api.Security;

namespace Deckloom.Api.Account;

public sealed record UpdateProfileRequest(
    string? DisplayName,
    string? Bio,
    string? FavoriteTcg,
    IReadOnlyList<string>? FormatIds);

public sealed record NamedItemResponse(string Id, string Name);

public sealed record ProfileResponse(
    string DisplayName,
    string? Bio,
    NamedItemResponse FavoriteTcg,
    IReadOnlyList<NamedItemResponse> PreferredFormats,
    AvatarResponse Avatar,
    bool HasPassword,
    bool HasGoogle);

// T-088: perfil do proprio Jogador. Nenhuma rota recebe PlayerId; o dono vem sempre da sessao.
public static class MeProfileEndpoints
{
    public static IEndpointRouteBuilder MapMeProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        var me = routes.MapGroup("/api/v1/me");

        me.MapGet("/profile", GetProfileAsync);
        me.MapPut("/profile", UpdateProfileAsync);

        return routes;
    }

    private static async Task<IResult> GetProfileAsync(
        HttpContext context, AuthenticateSession authenticate, GetMyProfile getProfile, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var caller = await authenticate.ExecuteAsync(
            new AuthenticateSessionCommand(context.Request.Cookies[SessionCookieService.CookieName]), ct);
        if (caller.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(caller.Errors);
        }

        var result = await getProfile.ExecuteAsync(new GetMyProfileCommand(caller.Value), ct);
        return ToResult(result);
    }

    private static async Task<IResult> UpdateProfileAsync(
        HttpContext context, AuthenticateSession authenticate, UpdateMyProfile updateProfile, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var caller = await authenticate.ExecuteAsync(
            new AuthenticateSessionCommand(context.Request.Cookies[SessionCookieService.CookieName]), ct);
        if (caller.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(caller.Errors);
        }

        var request = await AuthEndpoints.ReadJsonAsync<UpdateProfileRequest>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var body = request.Value!;
        var result = await updateProfile.ExecuteAsync(
            new UpdateMyProfileCommand(caller.Value, body.DisplayName, body.Bio, body.FavoriteTcg, body.FormatIds), ct);
        return ToResult(result);
    }

    private static IResult ToResult(Deckloom.SharedKernel.Result<Profile> result) =>
        result.IsFailure
            ? DomainErrorProblemMapper.ToResult(result.Errors)
            : Results.Json(ToResponse(result.Value), AuthEndpoints.JsonOptions);

    private static ProfileResponse ToResponse(Profile profile) =>
        new(
            profile.DisplayName,
            profile.Bio,
            new NamedItemResponse(profile.FavoriteTcg.Id, profile.FavoriteTcg.Name),
            profile.PreferredFormats.Select(f => new NamedItemResponse(f.Id, f.Name)).ToList(),
            new AvatarResponse(profile.Avatar.Kind, profile.Avatar.PresetId, profile.Avatar.ImageUrl, profile.Avatar.Alt),
            profile.HasPassword,
            profile.HasGoogle);
}
