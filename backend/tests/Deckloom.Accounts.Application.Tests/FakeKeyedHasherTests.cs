using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-025 (REQ-032, T3): HMAC-SHA256 com segredo no construtor; vetor RFC 4231, caso de teste 2.
public class FakeKeyedHasherTests
{
    [Fact]
    public void Matches_the_rfc_4231_vector()
    {
        var hasher = new FakeKeyedHasher("Jefe");

        Assert.Equal(
            "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
            Convert.ToHexStringLower(hasher.Hash("what do ya want for nothing?")));
    }

    [Fact]
    public void Is_deterministic_for_the_same_secret_and_value()
    {
        Assert.Equal(new FakeKeyedHasher().Hash("ana@example.com"), new FakeKeyedHasher().Hash("ana@example.com"));
    }

    [Fact]
    public void Differs_with_another_secret()
    {
        Assert.NotEqual(new FakeKeyedHasher("one").Hash("x"), new FakeKeyedHasher("two").Hash("x"));
    }

    [Fact]
    public void Differs_with_another_value()
    {
        var hasher = new FakeKeyedHasher();

        Assert.NotEqual(hasher.Hash("ana@example.com"), hasher.Hash("bia@example.com"));
    }
}
