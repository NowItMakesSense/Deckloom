using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-006 (REQ-003): value object Email. Regras em design.md > Value objects.
// Contrato assumido: Email.Create(string) : Result<Email>; Email.Value : string (normalizado).
public class EmailTests
{
    private const int MaxLength = 254;

    private static string EmailWithLength(int length)
    {
        const string suffix = "@b.co";
        return new string('a', length - suffix.Length) + suffix;
    }

    [Fact]
    public void REQ_003_valid_email_is_accepted()
    {
        var result = Email.Create("ana@example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("ana@example.com", result.Value.Value);
    }

    [Fact]
    public void REQ_003_email_is_trimmed_and_lowercased()
    {
        var result = Email.Create("  Ana.Silva@Example.COM \t");

        Assert.True(result.IsSuccess);
        Assert.Equal("ana.silva@example.com", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_003_empty_or_blank_email_is_required(string input)
    {
        var result = Email.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("required", result.Error.Code);
    }

    [Fact]
    public void REQ_003_null_email_is_required()
    {
        var result = Email.Create(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("required", result.Error.Code);
    }

    [Theory]
    [InlineData("ana-sem-arroba")]
    [InlineData("ana@@example.com")]
    [InlineData("ana@exa@mple.com")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana@example")]
    [InlineData("ana@exam ple.com")]
    public void REQ_003_malformed_email_is_invalid_format(string input)
    {
        var result = Email.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_format", result.Error.Code);
    }

    [Theory]
    [InlineData("ana silva@example.com")]
    [InlineData("ana\tsilva@example.com")]
    [InlineData("ana silva@example.com")]
    public void REQ_003_whitespace_in_local_part_is_invalid_format(string input)
    {
        var result = Email.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_format", result.Error.Code);
    }

    [Fact]
    public void REQ_003_to_string_returns_a_mask_and_never_the_address()
    {
        var email = Email.Create("  Ana.Silva@Example.COM ").Value;

        var text = email.ToString();

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("ana.silva@example.com", text);
        Assert.DoesNotContain("ana.silva", text);
        Assert.DoesNotContain("ana.silva@example.com", $"{email}");
    }

    [Fact]
    public void REQ_003_to_string_of_a_short_address_does_not_reveal_it()
    {
        var email = Email.Create("a@b.co").Value;

        Assert.NotEqual("a@b.co", email.ToString());
        Assert.DoesNotContain("a@b.co", email.ToString());
    }

    [Fact]
    public void REQ_003_value_still_exposes_the_normalized_address()
    {
        var email = Email.Create("  Ana.Silva@Example.COM ").Value;

        Assert.Equal("ana.silva@example.com", email.Value);
    }

    [Fact]
    public void REQ_003_email_with_254_characters_is_accepted()
    {
        var result = Email.Create(EmailWithLength(MaxLength));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_003_email_with_255_characters_is_invalid_format()
    {
        var result = Email.Create(EmailWithLength(MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_format", result.Error.Code);
    }

    [Fact]
    public void REQ_003_length_is_measured_after_trim()
    {
        var result = Email.Create("  " + EmailWithLength(MaxLength) + "  ");

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("ana.silva@example.com")]
    [InlineData("ana+tcg@example.com")]
    [InlineData("a.n.a+x.y@mail.example.com.br")]
    public void REQ_003_dots_and_plus_are_preserved(string input)
    {
        var result = Email.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input, result.Value.Value);
    }

    [Fact]
    public void REQ_003_emails_with_same_normalized_value_are_equal()
    {
        var first = Email.Create("Ana@Example.com").Value;
        var second = Email.Create("  ana@example.COM ").Value;

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void REQ_003_emails_with_different_values_are_not_equal()
    {
        var plain = Email.Create("ana@example.com").Value;
        var tagged = Email.Create("ana+tcg@example.com").Value;
        var dotted = Email.Create("a.na@example.com").Value;

        Assert.NotEqual(plain, tagged);
        Assert.NotEqual(plain, dotted);
        Assert.True(plain != tagged);
    }

    // R-02: rotulo de dominio vazio e invalid_format

    [Theory]
    [InlineData("a@.com")]
    [InlineData("a@b..com")]
    [InlineData("a@b.com.")]
    public void REQ_003_empty_domain_label_is_invalid_format(string input)
    {
        var result = Email.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_format", result.Error.Code);
    }
}
