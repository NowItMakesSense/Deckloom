using System.Security.Cryptography;
using System.Text;
using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-054 (REQ-029, REQ-047; design.md "Portas" e ADR-0005). Contrato fixado aqui para o implementer:
//   namespace Deckloom.Accounts.Infrastructure
//   sealed class SecureTokenGenerator : ISecureTokenGenerator          // ctor sem argumentos; 32 bytes de RandomNumberGenerator, base64url sem padding
//   sealed class Sha256TokenHasher : ITokenHasher                      // ctor sem argumentos; SHA-256 dos bytes UTF-8 do token
//   sealed class RateLimitHmacOptions { const string SectionName = "Auth"; string? RateLimitHmacKey { get; init; } }
//                                                                       // chave de configuracao "Auth:RateLimitHmacKey"; SEM valor padrao (null)
//   sealed class HmacKeyedHasher : IKeyedHasher                        // ctor(RateLimitHmacOptions); HMAC-SHA256 com a chave em UTF-8
// Chave nula, vazia, so espaco ou com menos de 32 bytes UTF-8 (tamanho da saida do HMAC-SHA256): ArgumentException na construcao.
// A normalizacao do email (Trim + ToLowerInvariant) e feita por AccessThrottle, nao pelo hasher.
// Nao importar Deckloom.Accounts.Testing aqui: o nome Sha256TokenHasher existe nos dois namespaces.
public sealed class TokenAdaptersTests
{
    private const string Key = "test-hmac-key-0123456789abcdef!!";
    private const string OtherKey = "another-hmac-key-0123456789abcd!";

    private static HmacKeyedHasher HasherWith(string? key) =>
        new(new RateLimitHmacOptions { RateLimitHmacKey = key });

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    // SecureTokenGenerator

    [Fact]
    public void REQ_029_generated_token_is_256_bits_in_base64url_without_padding()
    {
        var token = new SecureTokenGenerator().Generate();

        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.Equal(32, FromBase64Url(token).Length);
    }

    [Fact]
    public void REQ_029_10000_generated_tokens_have_no_repetition()
    {
        var generator = new SecureTokenGenerator();

        var tokens = Enumerable.Range(0, 10_000).Select(_ => generator.Generate()).ToHashSet();

        Assert.Equal(10_000, tokens.Count);
    }

    [Fact]
    public void REQ_029_token_generator_to_string_has_no_token()
    {
        var generator = new SecureTokenGenerator();
        var token = generator.Generate();

        Assert.DoesNotContain(token, generator.ToString(), StringComparison.Ordinal);
    }

    // Sha256TokenHasher

    [Fact]
    public void REQ_029_token_hash_of_abc_is_the_known_sha256_vector()
    {
        var hash = new Sha256TokenHasher().Hash("abc");

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Convert.ToHexStringLower(hash.ToBytes()));
    }

    [Fact]
    public void REQ_029_token_hash_is_deterministic_and_distinguishes_tokens()
    {
        var hasher = new Sha256TokenHasher();

        Assert.Equal(hasher.Hash("token-a"), hasher.Hash("token-a"));
        Assert.NotEqual(hasher.Hash("token-a"), hasher.Hash("token-b"));
        Assert.Equal(32, hasher.Hash("token-a").ToBytes().Length);
    }

    [Fact]
    public void REQ_029_token_hash_to_string_has_no_token()
    {
        var hasher = new Sha256TokenHasher();

        Assert.DoesNotContain("secret-token-value", hasher.Hash("secret-token-value").ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token-value", hasher.ToString(), StringComparison.Ordinal);
    }

    // HmacKeyedHasher

    [Fact]
    public void REQ_047_hmac_matches_the_known_hmac_sha256_vector()
    {
        var hash = HasherWith(Key).Hash("player@example.com");

        Assert.Equal(
            "f6f2cd85f223f30bdae8214170c42f5889571c17bf303afb31565b28e2514da7",
            Convert.ToHexStringLower(hash));
        Assert.Equal(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), Encoding.UTF8.GetBytes("player@example.com")),
            hash);
    }

    [Fact]
    public void REQ_047_hmac_is_deterministic_for_the_same_secret()
    {
        Assert.Equal(HasherWith(Key).Hash("203.0.113.7"), HasherWith(Key).Hash("203.0.113.7"));
    }

    [Fact]
    public void REQ_047_hmac_differs_with_a_different_secret()
    {
        Assert.NotEqual(HasherWith(Key).Hash("203.0.113.7"), HasherWith(OtherKey).Hash("203.0.113.7"));
    }

    [Fact]
    public void REQ_047_hmac_is_equal_for_the_same_normalized_email()
    {
        var hasher = HasherWith(Key);

        Assert.Equal(hasher.Hash(Normalize("Player@Example.com")), hasher.Hash(Normalize("  player@example.COM ")));
        Assert.NotEqual(hasher.Hash(Normalize("player@example.com")), hasher.Hash(Normalize("other@example.com")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("test-hmac-key-0123456789abcde!")] // 31 bytes
    public void REQ_047_empty_or_short_secret_is_rejected_at_construction(string? key)
    {
        Assert.Throws<ArgumentException>(() => HasherWith(key));
    }

    [Fact]
    public void REQ_047_secret_of_32_bytes_is_accepted()
    {
        Assert.Equal(32, Encoding.UTF8.GetByteCount(Key));
        Assert.Equal(32, HasherWith(Key).Hash("x").Length);
    }

    [Fact]
    public void REQ_047_options_have_no_default_secret_and_read_from_auth_rate_limit_hmac_key()
    {
        Assert.Null(new RateLimitHmacOptions().RateLimitHmacKey);
        Assert.Equal("Auth", RateLimitHmacOptions.SectionName);
    }

    [Fact]
    public void REQ_047_no_input_or_secret_value_appears_in_to_string()
    {
        var options = new RateLimitHmacOptions { RateLimitHmacKey = Key };
        var hasher = new HmacKeyedHasher(options);
        _ = hasher.Hash("player@example.com");

        foreach (var text in new[] { hasher.ToString(), options.ToString() })
        {
            Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
            Assert.DoesNotContain("player@example.com", text, StringComparison.Ordinal);
        }
    }
}
