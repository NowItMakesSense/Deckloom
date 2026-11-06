using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure;

// ADR-0003: v1$pbkdf2-sha256$<iteracoes>$<salt base64>$<hash base64>.
public sealed class Pbkdf2PasswordHasher : IPasswordHasher, IDisposable
{
    private const string Prefix = "v1$pbkdf2-sha256$";
    private const int SaltSize = 16;
    private const int DigestSize = 32;

    private readonly PasswordHashingOptions _options;
    private readonly IPbkdf2Deriver _deriver;
    private readonly SemaphoreSlim _gate;

    public Pbkdf2PasswordHasher(PasswordHashingOptions options)
        : this(options, new Rfc2898Pbkdf2Deriver())
    {
    }

    public Pbkdf2PasswordHasher(PasswordHashingOptions options, IPbkdf2Deriver deriver)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(deriver);
        _options = options;
        _deriver = deriver;
        _gate = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
    }

    public async ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iterations = _options.Iterations;
        var digest = await DeriveAsync(password, salt, iterations, ct).ConfigureAwait(false);

        var stored = string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(digest)}");
        return PasswordHash.Parse(stored).Value;
    }

    public async ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default)
    {
        var (iterations, salt, expected) = Decode(hash);
        if (expected.Length != DigestSize)
        {
            return false;
        }

        var actual = await DeriveAsync(password, salt, iterations, ct).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(PasswordHash hash) => Decode(hash).Iterations < _options.Iterations;

    public void Dispose() => _gate.Dispose();

    private static (int Iterations, byte[] Salt, byte[] Digest) Decode(PasswordHash hash)
    {
        var segments = hash.ToStoredString().Split('$');
        return (
            int.Parse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture),
            Convert.FromBase64String(segments[3]),
            Convert.FromBase64String(segments[4]));
    }

    private async Task<byte[]> DeriveAsync(Password password, byte[] salt, int iterations, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(password.Reveal());
            return await Task.Run(() => _deriver.Derive(bytes, salt, iterations, DigestSize), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
