using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure;

// Mapeia os claims validados para a identidade da aplicacao. O claim "picture" nunca e lido.
internal static class GoogleIdentityMapper
{
    public static GoogleVerifiedIdentity Map(JsonElement claims)
    {
        var email = Email.Create(Text(claims, "email"));
        var subject = Text(claims, "sub");
        if (subject is null || email.IsFailure)
        {
            throw new GoogleVerificationFailedException();
        }

        return new GoogleVerifiedIdentity(
            subject,
            email.Value,
            EmailVerified(claims),
            Text(claims, "name"),
            AuthTime(claims));
    }

    private static string? Text(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool EmailVerified(JsonElement claims) =>
        claims.TryGetProperty("email_verified", out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? AuthTime(JsonElement claims) =>
        claims.TryGetProperty("auth_time", out var v) && v.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(v.GetInt64())
            : null;
}
