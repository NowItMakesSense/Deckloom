using System.Text.Json;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Api.E2E.Host;

// Rotas SO de teste (/__test), alcancadas apenas no ambiente E2E (ver E2eHostProgram).
internal static class TestEndpoints
{
    public const string Prefix = "/__test";

    private const string LastLinkPath = "/__test/password-reset/last-link";
    private const string GoogleIdentityPath = "/__test/google/identity";
    private const string AdvanceClockPath = "/__test/clock/advance";
    private const string ResetClockPath = "/__test/clock/reset";

    public static Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (HttpMethods.IsGet(context.Request.Method) && path == LastLinkPath)
        {
            return LastLinkAsync(context);
        }

        if (HttpMethods.IsPost(context.Request.Method) && path == AdvanceClockPath)
        {
            return AdvanceClockAsync(context);
        }

        if (HttpMethods.IsPost(context.Request.Method) && path == ResetClockPath)
        {
            ResetClock(context);
            return Task.CompletedTask;
        }

        if (HttpMethods.IsPost(context.Request.Method) && path == GoogleIdentityPath)
        {
            return SetGoogleIdentityAsync(context);
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private static async Task SetGoogleIdentityAsync(HttpContext context)
    {
        GoogleIdentityRequest? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<GoogleIdentityRequest>();
        }
        catch (JsonException)
        {
            body = null;
        }

        var outcome = body?.Outcome ?? "approve";
        if (body is null || string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Email)
            || outcome is not ("approve" or "deny"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        context.RequestServices.GetRequiredService<StubGoogleAuthority>().SetIdentity(
            body.Subject, body.Email, body.Name, body.EmailVerified ?? true, outcome == "approve");
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task LastLinkAsync(HttpContext context)
    {
        var email = context.Request.Query["email"].ToString();
        var notifier = context.RequestServices.GetRequiredService<RecordingPasswordResetNotifier>();
        var sent = notifier.Sent.LastOrDefault(
            s => string.Equals(s.Email.Value, email, StringComparison.OrdinalIgnoreCase));
        if (sent is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await context.Response.WriteAsJsonAsync(new { link = sent.Link });
    }

    private static async Task AdvanceClockAsync(HttpContext context)
    {
        AdvanceRequest? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<AdvanceRequest>();
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null || body.Seconds < 0)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        context.RequestServices.GetRequiredService<FakeTimeProvider>().Advance(TimeSpan.FromSeconds(body.Seconds));
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static void ResetClock(HttpContext context)
    {
        var origin = context.RequestServices.GetRequiredService<E2eClockOrigin>();
        RewindTo(context.RequestServices.GetRequiredService<FakeTimeProvider>(), origin.Start);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    // FakeTimeProvider.SetUtcNow nao volta no tempo ("Cannot go back in time"); o reset E2E precisa disso,
    // entao grava o instante diretamente. So existe no host de teste.
    private static void RewindTo(FakeTimeProvider clock, DateTimeOffset start)
    {
        var field = typeof(FakeTimeProvider)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(DateTimeOffset) && f.Name == "_now");
        field.SetValue(clock, start);
    }

    private sealed record AdvanceRequest(long Seconds);

    private sealed record GoogleIdentityRequest(
        string? Subject, string? Email, string? Name, bool? EmailVerified, string? Outcome);
}
