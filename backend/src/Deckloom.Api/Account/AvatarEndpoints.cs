using Deckloom.Accounts.Application;
using Deckloom.Api.Auth;
using Deckloom.Api.Problems;
using Deckloom.Api.Security;

namespace Deckloom.Api.Account;

public sealed record ChooseAvatarPresetRequest(string? PresetId);

public sealed record AvatarEnvelopeResponse(AvatarResponse Avatar);

// T-089: avatar do proprio Jogador. O dono vem sempre da sessao; nao ha DELETE (a spec nao o define:
// o customizado e removido na troca para preset ou por outro upload).
public static class AvatarEndpoints
{
    // Limite do corpo multipart (design: 2,5 MB), aplicado antes de ler o corpo.
    public const long UploadLimitBytes = 2_621_440;

    private const string FileField = "file";
    private const string WebpContentType = "image/webp";

    public static IEndpointRouteBuilder MapAvatarEndpoints(this IEndpointRouteBuilder routes)
    {
        var avatar = routes.MapGroup("/api/v1/me/avatar");

        avatar.MapPut("/preset", ChoosePresetAsync);
        avatar.MapPost("/custom", UploadCustomAsync);
        avatar.MapGet("/custom", GetCustomAsync);

        return routes;
    }

    private static async Task<IResult> ChoosePresetAsync(
        HttpContext context, AuthenticateSession authenticate, ChooseAvatarPreset choosePreset, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var caller = await AuthenticateAsync(context, authenticate, ct);
        if (caller.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(caller.Errors);
        }

        var request = await AuthEndpoints.ReadJsonAsync<ChooseAvatarPresetRequest>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var result = await choosePreset.ExecuteAsync(new ChooseAvatarPresetCommand(caller.Value, request.Value!.PresetId), ct);
        return ToResult(result);
    }

    private static async Task<IResult> UploadCustomAsync(
        HttpContext context, AuthenticateSession authenticate, UploadCustomAvatar upload, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var caller = await AuthenticateAsync(context, authenticate, ct);
        if (caller.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(caller.Errors);
        }

        if (context.Request.ContentLength > UploadLimitBytes)
        {
            return DomainErrorProblemMapper.AvatarRejectedResult("size");
        }

        var sizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = UploadLimitBytes;
        }

        byte[]? image;
        try
        {
            image = await ReadFileAsync(context.Request, ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return DomainErrorProblemMapper.AvatarRejectedResult("size");
        }

        var result = await upload.ExecuteAsync(new UploadCustomAvatarCommand(caller.Value, image), ct);
        return ToResult(result);
    }

    private static async Task<IResult> GetCustomAsync(
        HttpContext context, AuthenticateSession authenticate, GetMyCustomAvatar getCustom, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var caller = await AuthenticateAsync(context, authenticate, ct);
        if (caller.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(caller.Errors);
        }

        var result = await getCustom.ExecuteAsync(new GetMyCustomAvatarCommand(caller.Value), ct);
        if (result.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(result.Errors);
        }

        // A URL e versionada pela chave (`?v=`): o conteudo daquela URL nunca muda.
        context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        context.Response.Headers.ContentDisposition = "inline";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Bytes(result.Value, WebpContentType);
    }

    private static Task<Deckloom.SharedKernel.Result<AuthenticatedPlayer>> AuthenticateAsync(
        HttpContext context, AuthenticateSession authenticate, CancellationToken ct) =>
        authenticate.ExecuteAsync(
            new AuthenticateSessionCommand(context.Request.Cookies[SessionCookieService.CookieName]), ct);

    private static async Task<byte[]?> ReadFileAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return null;
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile(FileField);
        if (file is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await using var stream = file.OpenReadStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static IResult ToResult(Deckloom.SharedKernel.Result<AvatarView> result) =>
        result.IsFailure
            ? DomainErrorProblemMapper.ToResult(result.Errors)
            : Results.Json(
                new AvatarEnvelopeResponse(
                    new AvatarResponse(result.Value.Kind, result.Value.PresetId, result.Value.ImageUrl, result.Value.Alt)),
                AuthEndpoints.JsonOptions);
}
