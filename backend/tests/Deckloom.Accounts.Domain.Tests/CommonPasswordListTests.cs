using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// SEC-05 (ASVS 2.1.7): lista local de senhas comuns, carregada uma vez do recurso embutido; comparacao exata (Ordinal).
public class CommonPasswordListTests
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("password1")]
    [InlineData("qwerty123")]
    public void A_listed_password_is_reported_as_common(string password) =>
        Assert.True(CommonPasswordList.Contains(password));

    [Theory]
    [InlineData("correct horse battery")]
    [InlineData("brand new password")]
    [InlineData("aaaaaaaa")]
    [InlineData("a8-chars")]
    public void An_unlisted_password_is_not_common(string password) =>
        Assert.False(CommonPasswordList.Contains(password));

    [Fact]
    public void Matching_is_case_sensitive()
    {
        Assert.True(CommonPasswordList.Contains("password1"));
        Assert.False(CommonPasswordList.Contains("PASSWORD1"));
    }

    [Fact]
    public void Null_or_empty_is_not_common()
    {
        Assert.False(CommonPasswordList.Contains(null));
        Assert.False(CommonPasswordList.Contains(""));
    }

    [Fact]
    public void The_list_has_a_reasonable_size()
    {
        Assert.InRange(CommonPasswordList.Count, 300, 800);
    }

    [Fact]
    public void The_code_is_too_common()
    {
        Assert.Equal("too_common", Password.TooCommonCode);
    }
}
