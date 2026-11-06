using System.Reflection;
using System.Text.Json;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-014 (REQ-001, REQ-013): value object PasswordHash.
// Design (tabela de invariantes): "Formato `v1$pbkdf2-sha256$...` (ADR-0003); nunca exposto em leitura".
// ADR-0003: formato `v1$pbkdf2-sha256$<iteracoes>$<salt base64>$<hash base64>`. J3: "o hash nunca sai em leitura de Perfil".
// Task T-014: "ToString/serializacao nunca imprimem o valor"; REFACTOR: "PasswordHash.Parse isolado para o adapter usar".
// Contrato assumido para o GREEN (ver relatorio):
//   PasswordHash.Parse(string? stored) : Result<PasswordHash>; falha = DomainError("invalid") sem Field
//   (o hash nao e campo da API). `.ToStoredString()` (metodo, string) devolve o valor armazenado para o adapter, mas nao pode ser
//   serializado nem impresso (ToString mascarado; System.Text.Json nao emite o valor).
public class PasswordHashTests
{
    private const string Salt = "c2FsdHNhbHRzYWx0c2FsdA==";
    private const string Digest = "aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string Valid = $"v1$pbkdf2-sha256$600000${Salt}${Digest}";

    [Fact]
    public void REQ_001_accepts_the_versioned_pbkdf2_format()
    {
        var result = PasswordHash.Parse(Valid);

        Assert.True(result.IsSuccess);
        Assert.Equal(Valid, result.Value.ToStoredString());
    }

    [Fact]
    public void REQ_001_accepts_reduced_iterations_used_in_tests()
    {
        Assert.True(PasswordHash.Parse($"v1$pbkdf2-sha256$1000${Salt}${Digest}").IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("senha-em-claro")]
    [InlineData("v2$pbkdf2-sha256$600000$c2FsdA==$aGFzaA==")]
    [InlineData("v1$bcrypt$600000$c2FsdA==$aGFzaA==")]
    [InlineData("V1$pbkdf2-sha256$600000$c2FsdA==$aGFzaA==")]
    [InlineData("v1$pbkdf2-sha256$abc$c2FsdA==$aGFzaA==")]
    [InlineData("v1$pbkdf2-sha256$0$c2FsdA==$aGFzaA==")]
    [InlineData("v1$pbkdf2-sha256$-5$c2FsdA==$aGFzaA==")]
    [InlineData("v1$pbkdf2-sha256$$c2FsdA==$aGFzaA==")]
    [InlineData("v1$pbkdf2-sha256$600000$$aGFzaA==")]
    [InlineData("v1$pbkdf2-sha256$600000$c2FsdA==$")]
    [InlineData("v1$pbkdf2-sha256$600000$!!!$aGFzaA==")]
    [InlineData("v1$pbkdf2-sha256$600000$c2FsdA==$***")]
    [InlineData("v1$pbkdf2-sha256$600000$c2FsdA==")]
    [InlineData("v1$pbkdf2-sha256$600000$c2FsdA==$aGFzaA==$extra")]
    public void REQ_001_rejects_anything_outside_the_format(string? stored)
    {
        var result = PasswordHash.Parse(stored);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
        Assert.Null(result.Error.Field);
    }

    [Fact]
    public void REQ_013_password_hash_never_in_string_representation()
    {
        var hash = PasswordHash.Parse(Valid).Value;

        var text = hash.ToString();

        Assert.DoesNotContain(Salt, text);
        Assert.DoesNotContain(Digest, text);
        Assert.DoesNotContain("pbkdf2", text);
        Assert.DoesNotContain(Valid, $"{hash}");
    }

    [Fact]
    public void REQ_013_password_hash_never_in_serialization()
    {
        var hash = PasswordHash.Parse(Valid).Value;

        var json = JsonSerializer.Serialize(hash);
        var wrapped = JsonSerializer.Serialize(new { PasswordHash = hash });

        Assert.DoesNotContain(Salt, json);
        Assert.DoesNotContain(Digest, json);
        Assert.DoesNotContain(Salt, wrapped);
        Assert.DoesNotContain(Digest, wrapped);
    }

    [Fact]
    public void REQ_013_failure_does_not_echo_the_rejected_value()
    {
        var result = PasswordHash.Parse("senha-em-claro-12345");

        Assert.DoesNotContain("senha-em-claro-12345", result.Error.ToString());
    }

    [Fact]
    public void REQ_013_serialization_contains_no_part_of_the_stored_string()
    {
        var hash = PasswordHash.Parse(Valid).Value;

        var json = JsonSerializer.Serialize(hash);
        var wrapped = JsonSerializer.Serialize(new { PasswordHash = hash });

        foreach (var output in new[] { json, wrapped })
        {
            Assert.DoesNotContain(Valid, output);
            Assert.DoesNotContain("pbkdf2", output);
            Assert.DoesNotContain("600000", output);
            Assert.DoesNotContain(Salt, output);
            Assert.DoesNotContain(Digest, output);
        }
    }

    [Fact]
    public void REQ_001_is_immutable_without_public_setters_or_constructors()
    {
        var properties = typeof(PasswordHash).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.All(properties, property => Assert.Null(property.SetMethod));
        Assert.Empty(typeof(PasswordHash).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }
}
