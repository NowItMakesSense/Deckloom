using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-053 / ADR-0003: PBKDF2-HMAC-SHA256, salt de 16 bytes, saida de 32 bytes, formato
// `v1$pbkdf2-sha256$<iteracoes>$<salt base64>$<hash base64>`; REQ-038 (limite de 128 caracteres)
// e garantido por `Password.Create`, o hasher so recebe `Password` valido.
public sealed class Pbkdf2PasswordHasherTests
{
    private const int TestIterations = 1_000;
    private const string PlainText = "correct horse battery staple";

    private static Password PasswordOf(string text) => Password.Create(text).Value;

    private static Pbkdf2PasswordHasher HasherWith(int iterations) =>
        new(new PasswordHashingOptions { Iterations = iterations });

    [Fact]
    public void REQ_001_production_default_is_600000_iterations_under_the_configuration_section()
    {
        Assert.Equal(600_000, new PasswordHashingOptions().Iterations);
        Assert.Equal("Auth:PasswordHashing", PasswordHashingOptions.SectionName);
    }

    [Fact]
    public void REQ_001_default_concurrency_limit_is_the_number_of_cores()
    {
        Assert.Equal(Environment.ProcessorCount, new PasswordHashingOptions().MaxConcurrency);
    }

    [Fact]
    public async Task REQ_001_hash_uses_the_versioned_format_with_configured_iterations_and_sizes()
    {
        var hash = await HasherWith(TestIterations).HashAsync(PasswordOf(PlainText));

        var segments = hash.ToStoredString().Split('$');
        Assert.Equal(5, segments.Length);
        Assert.Equal("v1", segments[0]);
        Assert.Equal("pbkdf2-sha256", segments[1]);
        Assert.Equal(TestIterations.ToString(CultureInfo.InvariantCulture), segments[2]);
        Assert.Equal(16, Convert.FromBase64String(segments[3]).Length);
        Assert.Equal(32, Convert.FromBase64String(segments[4]).Length);
    }

    [Fact]
    public async Task REQ_001_hash_is_the_pbkdf2_sha256_of_the_password_with_the_stored_salt()
    {
        var hash = await HasherWith(TestIterations).HashAsync(PasswordOf(PlainText));

        var segments = hash.ToStoredString().Split('$');
        var expected = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(PlainText),
            Convert.FromBase64String(segments[3]),
            TestIterations,
            HashAlgorithmName.SHA256,
            32);
        Assert.Equal(Convert.ToBase64String(expected), segments[4]);
    }

    [Fact]
    public async Task REQ_001_same_password_gets_distinct_salts_and_hashes()
    {
        var hasher = HasherWith(TestIterations);
        var password = PasswordOf(PlainText);

        var first = await hasher.HashAsync(password);
        var second = await hasher.HashAsync(password);

        Assert.NotEqual(first.ToStoredString().Split('$')[3], second.ToStoredString().Split('$')[3]);
        Assert.NotEqual(first.ToStoredString(), second.ToStoredString());
    }

    [Fact]
    public async Task REQ_001_stored_hash_does_not_contain_the_plaintext_password()
    {
        var hash = await HasherWith(TestIterations).HashAsync(PasswordOf(PlainText));

        Assert.DoesNotContain(PlainText, hash.ToStoredString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(PlainText)),
            hash.ToStoredString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task REQ_004_verify_accepts_the_correct_password()
    {
        var hasher = HasherWith(TestIterations);
        var hash = await hasher.HashAsync(PasswordOf(PlainText));

        Assert.True(await hasher.VerifyAsync(PasswordOf(PlainText), hash));
    }

    [Fact]
    public async Task REQ_004_verify_rejects_a_wrong_password()
    {
        var hasher = HasherWith(TestIterations);
        var hash = await hasher.HashAsync(PasswordOf(PlainText));

        Assert.False(await hasher.VerifyAsync(PasswordOf("correct horse battery stapl3"), hash));
    }

    [Fact]
    public async Task REQ_004_verify_rejects_a_password_that_differs_only_by_case()
    {
        var hasher = HasherWith(TestIterations);
        var hash = await hasher.HashAsync(PasswordOf(PlainText));

        Assert.False(await hasher.VerifyAsync(PasswordOf(PlainText.ToUpperInvariant()), hash));
    }

    [Fact]
    public async Task REQ_004_verify_uses_the_iterations_stored_in_the_hash_not_the_configured_ones()
    {
        var oldHash = await HasherWith(TestIterations).HashAsync(PasswordOf(PlainText));
        var currentHasher = HasherWith(TestIterations * 2);

        Assert.True(await currentHasher.VerifyAsync(PasswordOf(PlainText), oldHash));
    }

    [Fact]
    public async Task REQ_004_verify_rejects_a_hash_whose_digest_has_an_unexpected_length()
    {
        var hasher = HasherWith(TestIterations);
        var truncated = PasswordHash.Parse(
            $"v1$pbkdf2-sha256${TestIterations}${Convert.ToBase64String(new byte[16])}${Convert.ToBase64String(new byte[8])}").Value;

        Assert.False(await hasher.VerifyAsync(PasswordOf(PlainText), truncated));
    }

    [Fact]
    public async Task REQ_004_NeedsRehash_is_true_when_stored_iterations_are_below_the_configured_ones()
    {
        var oldHash = await HasherWith(TestIterations).HashAsync(PasswordOf(PlainText));

        Assert.True(HasherWith(TestIterations + 1).NeedsRehash(oldHash));
    }

    [Fact]
    public async Task REQ_004_NeedsRehash_is_false_when_stored_iterations_match_or_exceed_the_configured_ones()
    {
        var hash = await HasherWith(TestIterations).HashAsync(PasswordOf(PlainText));

        Assert.False(HasherWith(TestIterations).NeedsRehash(hash));
        Assert.False(HasherWith(TestIterations - 1).NeedsRehash(hash));
    }

    [Fact]
    public async Task REQ_001_hasher_honours_cancellation_before_deriving()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await HasherWith(TestIterations).HashAsync(PasswordOf(PlainText), cts.Token));
    }

    [Fact]
    public async Task REQ_038_concurrent_derivations_never_exceed_the_configured_limit()
    {
        const int limit = 2;
        var deriver = new BlockingDeriver();
        var hasher = new Pbkdf2PasswordHasher(
            new PasswordHashingOptions { Iterations = TestIterations, MaxConcurrency = limit },
            deriver);

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => Task.Run(async () => await hasher.HashAsync(PasswordOf(PlainText))))
            .ToArray();

        // Espera (sem dormir) ate que as duas primeiras derivacoes estejam dentro do semaforo.
        for (var i = 0; i < limit; i++)
        {
            Assert.True(await deriver.Entered.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        Assert.Equal(limit, deriver.Current);

        deriver.Gate.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(3, deriver.Calls);
        Assert.Equal(limit, deriver.Max);
    }

    [Fact]
    public async Task REQ_038_a_permit_is_released_when_a_derivation_fails()
    {
        var deriver = new ThrowingOnceDeriver();
        var hasher = new Pbkdf2PasswordHasher(
            new PasswordHashingOptions { Iterations = TestIterations, MaxConcurrency = 1 },
            deriver);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await hasher.HashAsync(PasswordOf(PlainText)));

        var hash = await hasher.HashAsync(PasswordOf(PlainText)).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(hash);
    }

    // REQ-038 (entrada > 128 caracteres nunca chega ao hasher): coberto por `Password.Create`
    // (Domain.Tests `too_long`) e pelos casos de uso (Application.Tests); `IPasswordHasher` so aceita
    // `Password`, entao a camada de infra nao tem outro contrato a testar.
    private sealed class BlockingDeriver : IPbkdf2Deriver
    {
        private int _current;
        private int _max;
        private int _calls;

        public SemaphoreSlim Entered { get; } = new(0);

        public ManualResetEventSlim Gate { get; } = new(false);

        public int Current => Volatile.Read(ref _current);

        public int Max => Volatile.Read(ref _max);

        public int Calls => Volatile.Read(ref _calls);

        public byte[] Derive(byte[] password, byte[] salt, int iterations, int length)
        {
            var now = Interlocked.Increment(ref _current);
            Interlocked.Increment(ref _calls);
            int seen;
            while ((seen = Volatile.Read(ref _max)) < now
                && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }

            Entered.Release();
            Gate.Wait();
            Interlocked.Decrement(ref _current);
            return new byte[length];
        }
    }

    private sealed class ThrowingOnceDeriver : IPbkdf2Deriver
    {
        private int _calls;

        public byte[] Derive(byte[] password, byte[] salt, int iterations, int length) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("boom")
                : new byte[length];
    }
}
