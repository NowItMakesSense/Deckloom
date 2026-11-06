using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-008 (REQ-015, REQ-037, REQ-039, REQ-040): value object DisplayName.
// Contrato: DisplayName.Create(string?) : Result<DisplayName>; valor normalizado em .Value;
// erros por código de campo (length, invalid_characters). Design (POST /auth/register e
// complete-signup): displayName admite apenas `length` e `invalid_characters`; logo nulo, vazio ou
// só espaços resulta em `length`. Caracteres de controle (Cc) são rejeitados em qualquer posição,
// inclusive nas bordas, verificados ANTES do trim, e têm precedência sobre `length`. Depois:
// normalização NFC, trim de espaços Unicode (não-Cc) nas bordas, comprimento em grapheme clusters
// (StringInfo.LengthInTextElements) de 3 a 30. Sem unicidade no tipo.
public class DisplayNameTests
{
    private const int MinLength = 3;
    private const int MaxLength = 30;

    // Família com ZWJ: 7 unidades UTF-16 / 5 code points, 1 grapheme cluster.
    private const string FamilyEmoji = "\U0001F468‍\U0001F469‍\U0001F467";

    [Theory]
    [InlineData(MinLength)]
    [InlineData(MaxLength)]
    public void REQ_015_boundary_lengths_are_accepted(int length)
    {
        var result = DisplayName.Create(new string('a', length));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(MinLength - 1)]
    [InlineData(MaxLength + 1)]
    public void REQ_015_lengths_outside_the_limits_are_rejected_with_length(int length)
    {
        var result = DisplayName.Create(new string('a', length));

        Assert.True(result.IsFailure);
        Assert.Equal("length", result.Error.Code);
    }

    [Fact]
    public void REQ_039_forty_character_name_is_rejected_without_truncation()
    {
        var result = DisplayName.Create(new string('a', 40));

        Assert.True(result.IsFailure);
        Assert.Equal("length", result.Error.Code);
    }

    [Fact]
    public void REQ_039_length_is_counted_after_trim()
    {
        var result = DisplayName.Create("  Bi  ");

        Assert.True(result.IsFailure);
        Assert.Equal("length", result.Error.Code);
    }

    [Fact]
    public void REQ_015_value_is_trimmed()
    {
        var result = DisplayName.Create("  Ana  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana", result.Value.Value);
    }

    [Theory]
    [InlineData(" Ana ")]
    [InlineData(" Ana ")]
    [InlineData("　Ana　")]
    public void REQ_039_unicode_whitespace_at_the_edges_is_trimmed(string input)
    {
        var result = DisplayName.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana", result.Value.Value);
    }

    [Fact]
    public void REQ_015_inner_spaces_are_kept()
    {
        var result = DisplayName.Create("Ana  Mage");

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana  Mage", result.Value.Value);
    }

    [Fact]
    public void REQ_039_value_is_nfc_normalized()
    {
        // "José" com e + U+0301 (decomposto) deve virar "José" composto (U+00E9).
        var result = DisplayName.Create("José");

        Assert.True(result.IsSuccess);
        Assert.Equal("José", result.Value.Value);
    }

    [Fact]
    public void REQ_039_composed_emoji_counts_as_one_text_element()
    {
        // 2 letras + 1 emoji composto = 3 elementos (contado em code points/UTF-16 seriam 7/9).
        var result = DisplayName.Create("Ab" + FamilyEmoji);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ab" + FamilyEmoji, result.Value.Value);
    }

    [Fact]
    public void REQ_039_composed_emoji_counts_as_one_toward_max()
    {
        // 29 letras + 1 emoji composto = 30 elementos.
        var result = DisplayName.Create(new string('a', MaxLength - 1) + FamilyEmoji);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_039_one_letter_plus_composed_emoji_is_too_short()
    {
        // 1 letra + 1 emoji = 2 elementos (em code points seriam 6, aceito).
        var result = DisplayName.Create("A" + FamilyEmoji);

        Assert.True(result.IsFailure);
        Assert.Equal("length", result.Error.Code);
    }

    [Fact]
    public void REQ_039_combining_mark_does_not_add_to_length()
    {
        // "Bi" + "e" + U+0301: após NFC são 3 elementos; "Bi" + "é" não pode contar 4.
        var result = DisplayName.Create("Bié");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("Ana\nMage")]
    [InlineData("Ana\r\nMage")]
    [InlineData("Ana\tMage")]
    [InlineData("Ana\u0000Mage")]
    [InlineData("Ana\u001FMage")]
    [InlineData("Ana\u007FMage")]
    [InlineData("Ana\u0085Mage")]
    public void REQ_040_control_characters_are_rejected_with_invalid_characters(string input)
    {
        var result = DisplayName.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Theory]
    [InlineData("Ana\n")]
    [InlineData("\tAna")]
    [InlineData("\r\nAna Bia")]
    [InlineData("Ana\u0085")]
    public void REQ_040_control_characters_at_the_edges_are_rejected_before_trim(string input)
    {
        var result = DisplayName.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Fact]
    public void REQ_040_control_character_in_an_otherwise_too_long_name_is_still_reported()
    {
        // Contrato assumido: caractere de controle tem precedência sobre comprimento.
        var result = DisplayName.Create(new string('a', 40) + "\n");

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Fact]
    public void REQ_037_the_same_name_can_be_created_more_than_once()
    {
        var first = DisplayName.Create("Ana");
        var second = DisplayName.Create("Ana");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Value, second.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("　 ")]
    public void REQ_015_empty_or_blank_name_is_rejected_with_length(string input)
    {
        var result = DisplayName.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("length", result.Error.Code);
    }

    [Fact]
    public void REQ_015_null_name_is_rejected_with_length()
    {
        var result = DisplayName.Create(null);

        Assert.True(result.IsFailure);
        Assert.Equal("length", result.Error.Code);
    }

    // R-05: igualdade por valor (ordinal)

    [Fact]
    public void REQ_015_display_names_with_the_same_value_are_equal()
    {
        var first = DisplayName.Create("Ana").Value;
        var second = DisplayName.Create("  Ana ").Value;

        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void REQ_015_display_names_equal_after_nfc_normalization()
    {
        var composed = DisplayName.Create("Jos\u00e9").Value;
        var decomposed = DisplayName.Create("Jose\u0301").Value;

        Assert.Equal(composed, decomposed);
        Assert.Equal(composed.GetHashCode(), decomposed.GetHashCode());
    }

    [Fact]
    public void REQ_015_display_name_comparison_is_ordinal_and_case_sensitive()
    {
        var lower = DisplayName.Create("ana").Value;
        var upper = DisplayName.Create("Ana").Value;

        Assert.False(lower.Equals(upper));
        Assert.True(lower != upper);
        Assert.False(lower.Equals(null));
        Assert.True(lower != null);
    }
}
