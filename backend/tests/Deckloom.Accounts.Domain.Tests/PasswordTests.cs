using System.Reflection;
using System.Text.Json;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-007 (REQ-003, REQ-030, REQ-031, REQ-038): value object Password (transitório, nunca persistido).
// Contrato assumido: Password.Create(string?) : Result<Password>; erros por código de campo
// (required, too_short, too_long). Comprimento em code points Unicode; sem trim, sem normalização,
// sem regra de composição.
public class PasswordTests
{
    private const int MinLength = 8;
    private const int MaxLength = 128;

    // U+1F600: fora do BMP, 2 unidades UTF-16 mas 1 code point.
    private const string Emoji = "\U0001F600";

    [Theory]
    [InlineData(MinLength)]
    [InlineData(MaxLength)]
    public void REQ_038_boundary_lengths_are_accepted(int length)
    {
        var result = Password.Create(new string('a', length));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_030_seven_characters_is_too_short()
    {
        var result = Password.Create(new string('a', MinLength - 1));

        Assert.True(result.IsFailure);
        Assert.Equal("too_short", result.Error.Code);
    }

    [Fact]
    public void REQ_038_129_characters_is_too_long()
    {
        var result = Password.Create(new string('a', MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("too_long", result.Error.Code);
    }

    [Theory]
    [InlineData("aaaaaaaa")]
    [InlineData("12345678")]
    [InlineData("        ")]
    public void REQ_003_no_composition_rules_are_applied(string input)
    {
        var result = Password.Create(input);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_038_non_bmp_character_counts_as_one_code_point_at_max()
    {
        // 128 emojis = 256 unidades UTF-16: contado em UTF-16 seria too_long.
        var result = Password.Create(string.Concat(Enumerable.Repeat(Emoji, MaxLength)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_038_129_non_bmp_code_points_are_too_long()
    {
        var result = Password.Create(string.Concat(Enumerable.Repeat(Emoji, MaxLength + 1)));

        Assert.True(result.IsFailure);
        Assert.Equal("too_long", result.Error.Code);
    }

    [Fact]
    public void REQ_030_four_non_bmp_code_points_are_too_short()
    {
        // 4 emojis = 8 unidades UTF-16: contado em UTF-16 seria aceito.
        var result = Password.Create(string.Concat(Enumerable.Repeat(Emoji, MinLength / 2)));

        Assert.True(result.IsFailure);
        Assert.Equal("too_short", result.Error.Code);
    }

    [Fact]
    public void REQ_030_eight_non_bmp_code_points_are_accepted()
    {
        var result = Password.Create(string.Concat(Enumerable.Repeat(Emoji, MinLength)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_030_leading_and_trailing_spaces_count_and_are_kept()
    {
        // 6 letras + 2 espaços = 8; com trim seriam 6 (too_short).
        var result = Password.Create("  abcdef");

        Assert.True(result.IsSuccess);
        Assert.Equal("  abcdef", result.Value.Reveal());
    }

    [Fact]
    public void REQ_030_trailing_spaces_are_kept()
    {
        var result = Password.Create("abcdef  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("abcdef  ", result.Value.Reveal());
    }

    [Fact]
    public void REQ_038_spaces_count_toward_max_length()
    {
        // 128 + 1 espaço: com trim seria aceito.
        var result = Password.Create(new string('a', MaxLength) + " ");

        Assert.True(result.IsFailure);
        Assert.Equal("too_long", result.Error.Code);
    }

    [Fact]
    public void REQ_031_value_is_not_unicode_normalized()
    {
        // "é" decomposto (e + U+0301) deve permanecer decomposto.
        var decomposed = "cafécafe";

        var result = Password.Create(decomposed);

        Assert.True(result.IsSuccess);
        Assert.Equal(decomposed, result.Value.Reveal());
    }

    [Fact]
    public void REQ_031_normalization_does_not_change_length()
    {
        // "abcdefg" + U+0301 = 8 code points crus; após NFC seriam 7 (too_short).
        var result = Password.Create("abcdefǵ");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    public void REQ_003_empty_password_is_required(string input)
    {
        var result = Password.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("required", result.Error.Code);
    }

    [Fact]
    public void REQ_003_null_password_is_required()
    {
        var result = Password.Create(null);

        Assert.True(result.IsFailure);
        Assert.Equal("required", result.Error.Code);
    }

    [Fact]
    public void REQ_038_to_string_does_not_expose_the_value()
    {
        var password = Password.Create("s3gredo-muito-longo").Value;

        Assert.DoesNotContain("s3gredo", password.ToString());
        Assert.DoesNotContain("s3gredo", $"{password}");
    }

    [Fact]
    public void REQ_038_serialization_never_contains_the_plaintext()
    {
        var password = Password.Create("s3gredo-muito-longo").Value;

        var json = JsonSerializer.Serialize(password);
        var wrapped = JsonSerializer.Serialize(new { Password = password });

        Assert.DoesNotContain("s3gredo", json);
        Assert.DoesNotContain("muito-longo", json);
        Assert.DoesNotContain("s3gredo", wrapped);
        Assert.DoesNotContain("muito-longo", wrapped);
    }

    [Fact]
    public void REQ_038_has_no_public_instance_property_returning_string()
    {
        var properties = typeof(Password).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(string));
    }

    [Fact]
    public void REQ_038_reveal_returns_the_exact_original_value()
    {
        const string original = "  s3gredo cafécafe😀  ";

        var password = Password.Create(original).Value;

        Assert.Equal(original, password.Reveal());
    }
}
