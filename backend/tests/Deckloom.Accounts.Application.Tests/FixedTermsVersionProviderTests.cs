using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-025 (REQ-033): devolve o que recebeu e aceita qualquer valor (a validacao e do TermsAcceptance).
public class FixedTermsVersionProviderTests
{
    [Fact]
    public void Returns_the_configured_version_and_urls()
    {
        var terms = new Uri("https://deckloom.example/termos");
        var privacy = new Uri("https://deckloom.example/privacidade");

        var provider = new FixedTermsVersionProvider("2026-10", terms, privacy);

        Assert.Equal("2026-10", provider.CurrentVersion);
        Assert.Equal(terms, provider.TermsUrl);
        Assert.Equal(privacy, provider.PrivacyPolicyUrl);
    }

    [Fact]
    public void Accepts_a_blank_version_without_throwing()
    {
        var url = new Uri("https://deckloom.example/x");

        var provider = new FixedTermsVersionProvider(" ", url, url);

        Assert.Equal(" ", provider.CurrentVersion);
    }
}
