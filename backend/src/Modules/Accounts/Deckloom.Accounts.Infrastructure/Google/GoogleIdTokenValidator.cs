using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

// Valida um id_token RS256: assinatura contra o JWKS, iss, aud, exp e nonce. Qualquer falha lanca
// GoogleVerificationFailedException sem carregar o token. Devolve os claims ja validados.
internal static class GoogleIdTokenValidator
{
    public static JsonElement Validate(
        string idToken, JsonElement jwks, IReadOnlyCollection<string> acceptedIssuers, string audience, string nonce, DateTimeOffset now)
    {
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw new GoogleVerificationFailedException();
        }

        try
        {
            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            VerifySignature(header.RootElement, jwks, parts, Base64UrlDecode(parts[2]));

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var claims = payload.RootElement.Clone();
            VerifyClaims(claims, acceptedIssuers, audience, nonce, now);
            return claims;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or KeyNotFoundException
            or InvalidOperationException)
        {
            throw new GoogleVerificationFailedException();
        }
    }

    // A documentacao OIDC do Google diz que o iss pode vir com ou sem o esquema https://. Aceita so esses dois valores,
    // por comparacao ordinal exata (nunca StartsWith/Contains).
    internal static string[] AcceptedIssuers(Uri authority)
    {
        var withScheme = authority.AbsoluteUri.TrimEnd('/');
        return [withScheme, withScheme[(authority.Scheme.Length + "://".Length)..]];
    }

    private static void VerifySignature(JsonElement header, JsonElement jwks, string[] parts, byte[] signature)
    {
        if (header.GetProperty("alg").GetString() != "RS256")
        {
            throw new GoogleVerificationFailedException();
        }

        var kid = header.TryGetProperty("kid", out var kidElement) ? kidElement.GetString() : null;
        foreach (var key in jwks.GetProperty("keys").EnumerateArray())
        {
            if (key.GetProperty("kty").GetString() != "RSA" || key.GetProperty("kid").GetString() != kid)
            {
                continue;
            }

            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Base64UrlDecode(key.GetProperty("n").GetString()!),
                Exponent = Base64UrlDecode(key.GetProperty("e").GetString()!),
            });
            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            if (rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return;
            }
        }

        throw new GoogleVerificationFailedException();
    }

    private static void VerifyClaims(JsonElement claims, IReadOnlyCollection<string> acceptedIssuers, string audience, string nonce, DateTimeOffset now)
    {
        var valid = claims.GetProperty("iss").GetString() is { } iss && acceptedIssuers.Contains(iss, StringComparer.Ordinal)
            && HasAudience(claims.GetProperty("aud"), audience)
            && claims.GetProperty("exp").GetInt64() > now.ToUnixTimeSeconds()
            && claims.TryGetProperty("nonce", out var n)
            && n.GetString() == nonce;
        if (!valid)
        {
            throw new GoogleVerificationFailedException();
        }
    }

    private static bool HasAudience(JsonElement aud, string audience) => aud.ValueKind == JsonValueKind.Array
        ? aud.EnumerateArray().Any(a => a.GetString() == audience)
        : aud.GetString() == audience;

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Convert.FromBase64String(s);
    }
}
