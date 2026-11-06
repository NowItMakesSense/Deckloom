using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Api.Http;
using Deckloom.Api.Problems;
using Deckloom.Api.Security;

namespace Deckloom.Api.Auth;

public sealed record RegisterRequest(
    string? Email,
    string? Password,
    string? DisplayName,
    bool AcceptTerms,
    string? TermsVersion,
    bool AdultDeclaration);

public sealed record LoginRequest(string? Email, string? Password, bool RememberMe);

public sealed record AvatarResponse(string Kind, string? PresetId, string ImageUrl, string Alt);

public sealed record SessionSummaryResponse(string DisplayName, AvatarResponse Avatar, bool HasPassword, bool HasGoogle);

public sealed record PasswordResetRequestBody(string? Email);

public sealed record PasswordResetConfirmBody(string? Token, string? NewPassword);

public static class AuthEndpoints
{
    // Design: duracao minima fixa do pedido de redefinicao (REQ-028).
    private static readonly TimeSpan MinimumResetRequestDuration = TimeSpan.FromMilliseconds(500);

    // Design: limite de tamanho de corpo de 16 KB em JSON; acima disso a requisicao e recusada com 413.
    public const int MaxJsonBodyBytes = 16 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/api/v1/auth");

        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapGet("/session", GetSessionAsync);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapPost("/password-reset/request", RequestPasswordResetAsync);
        auth.MapPost("/password-reset/confirm", ConfirmPasswordResetAsync);

        return routes;
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        HttpContext context,
        RequestPasswordReset requestReset,
        IDelayer delayer,
        TimeProvider time,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var startedAt = time.GetUtcNow();
        var request = await ReadJsonAsync<PasswordResetRequestBody>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var ip = ClientIpKey.From(context);
        var result = await requestReset.ExecuteAsync(new RequestPasswordResetCommand(request.Value!.Email, ip), ct);

        // Duracao minima fixa: a resposta demora o mesmo com ou sem conta (anti-enumeracao, REQ-028).
        var remaining = MinimumResetRequestDuration - (time.GetUtcNow() - startedAt);
        await delayer.DelayAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, ct);

        return result.IsFailure ? DomainErrorProblemMapper.ToResult(result.Errors) : Results.Accepted();
    }

    private static async Task<IResult> ConfirmPasswordResetAsync(
        HttpContext context, ResetPassword resetPassword, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var request = await ReadJsonAsync<PasswordResetConfirmBody>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var result = await resetPassword.ExecuteAsync(
            new ResetPasswordCommand(request.Value!.Token, request.Value.NewPassword), ct);
        return result.IsFailure ? DomainErrorProblemMapper.ToResult(result.Errors) : Results.NoContent();
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, RegisterWithPassword register, SessionCookieService cookies, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var body = await ReadJsonAsync<RegisterRequest>(context.Request, ct);
        if (body.Failure is not null)
        {
            return body.Failure;
        }

        var request = body.Value!;
        var ip = ClientIpKey.From(context);
        var result = await register.ExecuteAsync(
            new RegisterWithPasswordCommand(
                request.Email,
                request.Password,
                request.DisplayName,
                request.AcceptTerms,
                request.TermsVersion,
                request.AdultDeclaration,
                ip),
            ct);
        if (result.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(result.Errors);
        }

        cookies.Issue(context, result.Value, rememberMe: false);
        return Results.Json(ToResponse(result.Value.Summary), JsonOptions, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context, LoginWithPassword login, SessionCookieService cookies, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var request = await ReadJsonAsync<LoginRequest>(context.Request, ct);
        if (request.Failure is not null)
        {
            return request.Failure;
        }

        var ip = ClientIpKey.From(context);
        var result = await login.ExecuteAsync(
            new LoginWithPasswordCommand(request.Value!.Email, request.Value.Password, request.Value.RememberMe, ip), ct);
        if (result.IsFailure)
        {
            return DomainErrorProblemMapper.ToResult(result.Errors);
        }

        cookies.Issue(context, result.Value, request.Value.RememberMe);
        return Results.Json(ToResponse(result.Value.Summary), JsonOptions);
    }

    private static async Task<IResult> GetSessionAsync(
        HttpContext context, AuthenticateSession authenticate, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        var token = context.Request.Cookies[SessionCookieService.CookieName];
        var result = await authenticate.ExecuteAsync(new AuthenticateSessionCommand(token), ct);
        return result.IsFailure
            ? DomainErrorProblemMapper.ToResult(result.Errors)
            : Results.Json(ToResponse(result.Value.Summary), JsonOptions);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, Logout logout, SessionCookieService cookies, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";

        await logout.ExecuteAsync(new LogoutCommand(context.Request.Cookies[SessionCookieService.CookieName]), ct);
        cookies.Expire(context);
        return Results.NoContent();
    }

    internal static async Task<(T? Value, IResult? Failure)> ReadJsonAsync<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        if (HasBody(request) && !IsJsonMediaType(request.ContentType))
        {
            return (null, DomainErrorProblemMapper.UnsupportedMediaTypeResult());
        }

        var body = await ReadBoundedBodyAsync(request, ct);
        if (body is null)
        {
            return (null, DomainErrorProblemMapper.PayloadTooLargeResult());
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return value is null ? (null, DomainErrorProblemMapper.MalformedBodyResult()) : (value, null);
        }
        catch (JsonException)
        {
            return (null, DomainErrorProblemMapper.MalformedBodyResult());
        }
    }

    private static bool HasBody(HttpRequest request) =>
        request.ContentLength > 0
        || request.Headers.TransferEncoding.ToString().Contains("chunked", StringComparison.OrdinalIgnoreCase);

    // Media type sem parametros (charset etc.), sem diferenciar maiusculas.
    private static bool IsJsonMediaType(string? contentType) =>
        contentType is not null
        && Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && string.Equals(parsed.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase);

    // Le no maximo MaxJsonBodyBytes; devolve null quando o corpo excede o limite.
    private static async Task<byte[]?> ReadBoundedBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxJsonBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxJsonBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    internal static SessionSummaryResponse ToResponse(SessionSummary summary) =>
        new(
            summary.DisplayName,
            new AvatarResponse(summary.Avatar.Kind, summary.Avatar.PresetId, summary.Avatar.ImageUrl, summary.Avatar.Alt),
            summary.HasPassword,
            summary.HasGoogle);
}
