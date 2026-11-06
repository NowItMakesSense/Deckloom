using System.Text.Json;
using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento isolado de falhas do fluxo OIDC para (Stage, Reason). Nunca carrega tokens, codigos ou segredos.
internal static class GoogleFailureClassifier
{
    // Null quando a excecao nao e uma falha de transporte classificavel (inclui cancelamento do chamador).
    public static GoogleAuthFailed? ClassifyTransport(Exception ex, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested && ex is OperationCanceledException)
        {
            return null;
        }

        return ex switch
        {
            OperationCanceledException => new GoogleAuthFailed(GoogleAuthFailureStage.Token, GoogleAuthFailureReason.Timeout),
            HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException =>
                new GoogleAuthFailed(GoogleAuthFailureStage.Token, GoogleAuthFailureReason.ProviderError),
            _ => null,
        };
    }

    public static GoogleAuthFailed ClassifyTokenError(string responseBody) =>
        new(GoogleAuthFailureStage.Token, ErrorCode(responseBody) == "access_denied"
            ? GoogleAuthFailureReason.Cancelled
            : GoogleAuthFailureReason.ProviderError);

    public static string? ExtractIdToken(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("id_token", out var token) ? token.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static GoogleAuthFailed? ClassifyIdentity(
        GoogleVerifiedIdentity identity, GoogleAuthorizationIntent intent, DateTimeOffset now, int reauthMaxAgeSeconds)
    {
        if (!identity.EmailVerified)
        {
            return new GoogleAuthFailed(GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.UnverifiedEmail);
        }

        var stale = intent == GoogleAuthorizationIntent.Reauth
            && (identity.AuthTime is not { } authTime || (now - authTime).TotalSeconds > reauthMaxAgeSeconds);
        return stale
            ? new GoogleAuthFailed(GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError)
            : null;
    }

    private static string? ErrorCode(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
