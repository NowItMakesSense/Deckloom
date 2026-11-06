using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-025 (REQ-001): FakePasswordHasher(int currentIterations) gera de forma deterministica
// v1$pbkdf2-sha256$<currentIterations>$<salt fixo b64>$<base64 da senha>, aceito por PasswordHash.Parse.
public class FakePasswordHasherTests
{
    private static Password APassword(string text = "correct horse") => Password.Create(text).Value;

    [Fact]
    public async Task Hash_is_deterministic_and_has_the_documented_shape()
    {
        var hasher = new FakePasswordHasher(currentIterations: 7);

        var first = await hasher.HashAsync(APassword());
        var second = await hasher.HashAsync(APassword());

        Assert.Equal(first.ToStoredString(), second.ToStoredString());
        var segments = first.ToStoredString().Split('$');
        Assert.Equal(["v1", "pbkdf2-sha256", "7"], segments[..3]);
        Assert.Equal(Convert.ToBase64String("correct horse"u8.ToArray()), segments[4]);
        Assert.True(PasswordHash.Parse(first.ToStoredString()).IsSuccess);
    }

    [Fact]
    public async Task Different_passwords_produce_different_hashes()
    {
        var hasher = new FakePasswordHasher(1);

        var a = await hasher.HashAsync(APassword("password one"));
        var b = await hasher.HashAsync(APassword("password two"));

        Assert.NotEqual(a.ToStoredString(), b.ToStoredString());
    }

    [Fact]
    public async Task Verify_accepts_the_right_password_and_rejects_a_wrong_one()
    {
        var hasher = new FakePasswordHasher(1);
        var hash = await hasher.HashAsync(APassword("password one"));

        Assert.True(await hasher.VerifyAsync(APassword("password one"), hash));
        Assert.False(await hasher.VerifyAsync(APassword("password two"), hash));
    }

    [Fact]
    public async Task Verify_of_a_hash_that_did_not_come_from_the_fake_is_false_without_throwing()
    {
        var hasher = new FakePasswordHasher(1);
        var foreign = PasswordHash.Parse("v1$pbkdf2-sha256$600000$c2FsdA==$aGFzaA==").Value;

        Assert.False(await hasher.VerifyAsync(APassword(), foreign));
    }

    [Fact]
    public async Task NeedsRehash_is_true_only_when_the_hash_has_fewer_iterations_than_current()
    {
        var old = await new FakePasswordHasher(1).HashAsync(APassword());
        var hasher = new FakePasswordHasher(2);
        var current = await hasher.HashAsync(APassword());
        var newer = await new FakePasswordHasher(3).HashAsync(APassword());

        Assert.True(hasher.NeedsRehash(old));
        Assert.False(hasher.NeedsRehash(current));
        Assert.False(hasher.NeedsRehash(newer));
    }

    [Fact]
    public async Task Calls_are_recorded_with_the_verify_result()
    {
        var hasher = new FakePasswordHasher(1);
        var right = APassword("password one");
        var wrong = APassword("password two");

        var hash = await hasher.HashAsync(right);
        await hasher.VerifyAsync(right, hash);
        await hasher.VerifyAsync(wrong, hash);
        hasher.NeedsRehash(hash);

        Assert.Equal(["password one"], hasher.HashCalls.Select(p => p.Reveal()));
        Assert.Equal(
            [("password one", true), ("password two", false)],
            hasher.VerifyCalls.Select(c => (c.Password.Reveal(), c.Result)));
        Assert.All(hasher.VerifyCalls, c => Assert.Equal(hash.ToStoredString(), c.Hash.ToStoredString()));
        Assert.Single(hasher.NeedsRehashCalls);
        Assert.Equal(hash.ToStoredString(), hasher.NeedsRehashCalls[0].ToStoredString());
    }

    [Fact]
    public void A_new_hasher_has_no_recorded_calls()
    {
        var hasher = new FakePasswordHasher(1);

        Assert.Empty(hasher.HashCalls);
        Assert.Empty(hasher.VerifyCalls);
        Assert.Empty(hasher.NeedsRehashCalls);
    }
}
