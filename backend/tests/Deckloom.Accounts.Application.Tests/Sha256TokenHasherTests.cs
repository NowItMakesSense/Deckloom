using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-025 (REQ-029, S2): SHA-256 real do token opaco; vetor conhecido (FIPS 180-2, "abc").
public class Sha256TokenHasherTests
{
    private readonly Sha256TokenHasher _hasher = new();

    [Fact]
    public void Hashes_a_known_vector()
    {
        var hash = _hasher.Hash("abc");

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Convert.ToHexStringLower(hash.ToBytes()));
    }

    [Fact]
    public void Is_deterministic_and_fixed_size()
    {
        Assert.Equal(_hasher.Hash("tok-0001"), _hasher.Hash("tok-0001"));
        Assert.Equal(32, _hasher.Hash("tok-0001").ToBytes().Length);
    }

    [Fact]
    public void Different_tokens_have_different_hashes()
    {
        Assert.NotEqual(_hasher.Hash("tok-0001"), _hasher.Hash("tok-0002"));
    }
}
