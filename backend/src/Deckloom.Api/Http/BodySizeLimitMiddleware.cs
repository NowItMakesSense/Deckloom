using Microsoft.Extensions.Options;

namespace Deckloom.Api.Http;

// Recusa com 413, pelo Content-Length e antes de ler o corpo, JSON e multipart acima do limite em /api/**.
public sealed class BodySizeLimitMiddleware(RequestDelegate next, IOptions<HttpLimitsOptions> options)
{
    private static readonly PathString ApiPrefix = new("/api");

    private readonly HttpLimitsOptions _limits = options.Value;

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments(ApiPrefix)
            && request.ContentLength is { } length
            && length > LimitFor(request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return Task.CompletedTask;
        }

        return next(context);
    }

    private long LimitFor(string? contentType) =>
        contentType is not null && contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)
            ? _limits.MaxUploadBytes
            : _limits.MaxJsonBodyBytes;
}
