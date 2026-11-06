using System.Reflection;
using System.Text.Json;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-019 (REQ-029, REQ-030, REQ-038): value object TokenHash.
// Design (emenda 2026-10-23, "Redefinicao de senha"): SHA-256 do token, exatamente 32 bytes (senao `invalid`),
// copia defensiva na entrada e na saida, igualdade por valor e ToString() mascarado (padrao do PasswordHash).
// Contrato assumido para o GREEN: TokenHash.Create(byte[]? sha256) : Result<TokenHash>
// (falha = DomainError("invalid") sem Field) e `.ToBytes()` devolve uma copia dos 32 bytes para o adapter.
public class TokenHashTests
{
    private static byte[] Bytes(byte seed = 1, int length = 32) =>
        Enumerable.Range(0, length).Select(i => (byte)(seed + i)).ToArray();

    [Fact]
    public void REQ_029_accepts_exactly_32_bytes_and_returns_them()
    {
        var input = Bytes();

        var result = TokenHash.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input, result.Value.ToBytes());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void REQ_029_rejects_any_length_other_than_32_bytes_as_invalid(int length)
    {
        var result = TokenHash.Create(Bytes(1, length));

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
        Assert.Null(result.Error.Field);
    }

    [Fact]
    public void REQ_029_rejects_null_as_invalid()
    {
        var result = TokenHash.Create(null);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
        Assert.Null(result.Error.Field);
    }

    [Fact]
    public void REQ_029_mutating_the_input_array_afterwards_does_not_change_the_hash()
    {
        var input = Bytes();
        var hash = TokenHash.Create(input).Value;

        input[0] = 0xFF;

        Assert.Equal(Bytes(), hash.ToBytes());
        Assert.Equal(TokenHash.Create(Bytes()).Value, hash);
    }

    [Fact]
    public void REQ_029_mutating_the_returned_array_does_not_change_the_hash()
    {
        var hash = TokenHash.Create(Bytes()).Value;

        var leaked = hash.ToBytes();
        leaked[0] = 0xFF;

        Assert.Equal(Bytes(), hash.ToBytes());
        Assert.NotSame(hash.ToBytes(), hash.ToBytes());
    }

    [Fact]
    public void REQ_029_equal_bytes_are_equal_values_with_the_same_hash_code()
    {
        var a = TokenHash.Create(Bytes()).Value;
        var b = TokenHash.Create(Bytes()).Value;

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void REQ_029_different_bytes_are_different_values_even_when_only_the_last_byte_differs()
    {
        var a = TokenHash.Create(Bytes()).Value;
        var lastDiffers = Bytes();
        lastDiffers[31] ^= 0x01;
        var b = TokenHash.Create(lastDiffers).Value;

        Assert.NotEqual(a, b);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void REQ_029_is_not_equal_to_null_or_to_another_type()
    {
        var hash = TokenHash.Create(Bytes()).Value;

        Assert.False(hash.Equals((TokenHash?)null));
        Assert.False(hash.Equals((object?)null));
        Assert.False(hash.Equals(Bytes()));
    }

    [Fact]
    public void REQ_029_ToString_is_masked_and_leaks_no_byte_of_the_hash()
    {
        var bytes = Bytes(200);
        var hash = TokenHash.Create(bytes).Value;

        var text = hash.ToString();

        Assert.Equal("TokenHash(***)", text);
        Assert.DoesNotContain(Convert.ToHexString(bytes), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(bytes), text);
        Assert.DoesNotContain(Convert.ToHexString(bytes), $"{hash}", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void REQ_029_serialization_contains_no_part_of_the_hash()
    {
        var bytes = Bytes(200);
        var hash = TokenHash.Create(bytes).Value;

        var json = JsonSerializer.Serialize(new { Hash = hash });

        Assert.DoesNotContain(Convert.ToBase64String(bytes), json);
        Assert.DoesNotContain(Convert.ToHexString(bytes), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void REQ_029_is_sealed_immutable_and_has_no_public_constructor()
    {
        var type = typeof(TokenHash);

        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.Null(property.SetMethod));
    }
}
