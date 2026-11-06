using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-009 (REQ-015, REQ-016, REQ-040): value object Bio.
// Contrato: Bio.Create(string?) : Result<Bio?>; sucesso com .Value == null significa bio ausente.
// Design (tabela de value objects): "NFC, `\r\n` vira `\n`, `trim` nas bordas, até 280 elementos de
// texto; vazia vira ausente; quebra de linha permitida, demais caracteres Cc rejeitados".
// Erros do contrato (PUT /me/profile): bio admite apenas `too_long` e `invalid_characters`.
// Ordem assumida: `\r\n` -> `\n`; rejeita Cc != `\n` (precedência sobre too_long); NFC; trim; conta.
public class BioTests
{
    private const int MaxLength = 280;

    // Família com ZWJ: 7 unidades UTF-16 / 5 code points, 1 grapheme cluster.
    private const string FamilyEmoji = "\U0001F468‍\U0001F469‍\U0001F467";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("　 ")]
    [InlineData("\n")]
    [InlineData(" \r\n \n ")]
    public void REQ_016_null_empty_or_blank_bio_becomes_absent(string? input)
    {
        var result = Bio.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void REQ_015_bio_with_280_elements_is_accepted()
    {
        var result = Bio.Create(new string('a', MaxLength));

        Assert.True(result.IsSuccess);
        Assert.Equal(new string('a', MaxLength), result.Value!.Value);
    }

    [Fact]
    public void REQ_015_bio_with_281_elements_is_rejected_with_too_long()
    {
        var result = Bio.Create(new string('a', MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("too_long", result.Error.Code);
    }

    [Fact]
    public void REQ_015_length_is_counted_after_trim()
    {
        var result = Bio.Create("  " + new string('a', MaxLength) + "  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(new string('a', MaxLength), result.Value!.Value);
    }

    [Fact]
    public void REQ_015_value_is_trimmed_and_inner_spaces_are_kept()
    {
        var result = Bio.Create("  Jogo  Commander \n");

        Assert.True(result.IsSuccess);
        Assert.Equal("Jogo  Commander", result.Value!.Value);
    }

    [Fact]
    public void REQ_040_line_breaks_are_accepted_and_kept()
    {
        var result = Bio.Create("Linha 1\nLinha 2\n\nLinha 4");

        Assert.True(result.IsSuccess);
        Assert.Equal("Linha 1\nLinha 2\n\nLinha 4", result.Value!.Value);
    }

    [Fact]
    public void REQ_040_crlf_is_normalized_to_lf()
    {
        var result = Bio.Create("Linha 1\r\nLinha 2");

        Assert.True(result.IsSuccess);
        Assert.Equal("Linha 1\nLinha 2", result.Value!.Value);
    }

    [Fact]
    public void REQ_015_crlf_counts_as_one_element_after_normalization()
    {
        // "a" + 278 quebras CRLF + "a": 280 elementos após a normalização (558 antes dela).
        var input = "a" + string.Concat(Enumerable.Repeat("\r\n", MaxLength - 2)) + "a";

        var result = Bio.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal("a" + new string('\n', MaxLength - 2) + "a", result.Value!.Value);
    }

    [Fact]
    public void REQ_015_281_elements_with_line_breaks_is_rejected_with_too_long()
    {
        var input = "a" + new string('\n', MaxLength - 1) + "a";

        var result = Bio.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("too_long", result.Error.Code);
    }

    [Theory]
    [InlineData("Oi\u0000mundo")]
    [InlineData("Oi\tmundo")]
    [InlineData("Oi\u001Fmundo")]
    [InlineData("Oi\u007Fmundo")]
    [InlineData("Oi\u0085mundo")]
    [InlineData("Oi\u000Bmundo")]
    [InlineData("Oi\u000Cmundo")]
    public void REQ_040_control_characters_other_than_line_break_are_rejected(string input)
    {
        var result = Bio.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Theory]
    [InlineData("\u0000Oi")]
    [InlineData("Oi\u0000")]
    [InlineData("\tOi")]
    [InlineData("Oi\u0085")]
    public void REQ_040_control_characters_at_the_edges_are_rejected_before_trim(string input)
    {
        var result = Bio.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Theory]
    [InlineData("Oi\rmundo")]
    [InlineData("\rOi")]
    [InlineData("Oi\r")]
    [InlineData("\r")]
    [InlineData("Oi\n\rmundo")]
    public void REQ_040_lone_carriage_return_is_rejected_with_invalid_characters(string input)
    {
        var result = Bio.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Theory]
    [InlineData("\t")]
    [InlineData(" \t ")]
    public void REQ_040_blank_bio_containing_tab_is_rejected_not_absent(string input)
    {
        var result = Bio.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Fact]
    public void REQ_040_control_character_in_an_otherwise_too_long_bio_is_still_reported()
    {
        var result = Bio.Create(new string('a', MaxLength + 20) + "\u0000");

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Fact]
    public void REQ_040_control_character_in_an_otherwise_blank_bio_is_rejected()
    {
        var result = Bio.Create("\u0000");

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_characters", result.Error.Code);
    }

    [Fact]
    public void REQ_015_value_is_nfc_normalized()
    {
        // "José" com e + U+0301 (decomposto) deve virar "José" composto (U+00E9).
        var result = Bio.Create("José");

        Assert.True(result.IsSuccess);
        Assert.Equal("José", result.Value!.Value);
    }

    [Fact]
    public void REQ_015_composed_emoji_counts_as_one_text_element()
    {
        // 279 letras + 1 emoji composto = 280 elementos (em code points seriam 284).
        var input = new string('a', MaxLength - 1) + FamilyEmoji;

        var result = Bio.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input, result.Value!.Value);
    }

    [Fact]
    public void REQ_015_composed_emoji_beyond_the_limit_is_too_long()
    {
        // 280 letras + 1 emoji composto = 281 elementos.
        var result = Bio.Create(new string('a', MaxLength) + FamilyEmoji);

        Assert.True(result.IsFailure);
        Assert.Equal("too_long", result.Error.Code);
    }

    [Fact]
    public void REQ_015_combining_mark_does_not_add_to_length()
    {
        // 279 "a" + "e" + U+0301: após NFC são 280 elementos.
        var result = Bio.Create(new string('a', MaxLength - 1) + "é");

        Assert.True(result.IsSuccess);
    }

    // R-05: igualdade por valor (ordinal)

    [Fact]
    public void REQ_015_bios_with_the_same_value_are_equal()
    {
        var first = Bio.Create("Jogo commander.\r\nGosto de combos.").Value!;
        var second = Bio.Create("  Jogo commander.\nGosto de combos. ").Value!;

        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void REQ_015_bio_comparison_is_ordinal_and_case_sensitive()
    {
        var lower = Bio.Create("combos").Value!;
        var upper = Bio.Create("Combos").Value!;

        Assert.False(lower.Equals(upper));
        Assert.True(lower != upper);
        Assert.False(lower.Equals(null));
        Assert.True(lower != null);
    }
}
