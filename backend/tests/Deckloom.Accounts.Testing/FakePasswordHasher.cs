using System.Globalization;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed record VerifyCall(Password Password, PasswordHash Hash, bool Result);

public sealed class FakePasswordHasher : IPasswordHasher
{
    private static readonly string FixedSalt = Convert.ToBase64String("fake-salt"u8.ToArray());

    private readonly int _currentIterations;
    private readonly List<Password> _hashCalls = [];
    private readonly List<VerifyCall> _verifyCalls = [];
    private readonly List<PasswordHash> _needsRehashCalls = [];

    public FakePasswordHasher(int currentIterations) => _currentIterations = currentIterations;

    public TransactionProbe TransactionProbe { get; } = new();

    public IReadOnlyList<Password> HashCalls => _hashCalls;

    public IReadOnlyList<VerifyCall> VerifyCalls => _verifyCalls;

    public IReadOnlyList<PasswordHash> NeedsRehashCalls => _needsRehashCalls;

    public ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default)
    {
        TransactionProbe.Record("Hash");
        _hashCalls.Add(password);
        return ValueTask.FromResult(Compute(password, _currentIterations));
    }

    public ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default)
    {
        TransactionProbe.Record("Verify");
        var result = TryReadIterations(hash, out var iterations)
            && Compute(password, iterations).ToStoredString() == hash.ToStoredString();
        _verifyCalls.Add(new VerifyCall(password, hash, result));
        return ValueTask.FromResult(result);
    }

    public bool NeedsRehash(PasswordHash hash)
    {
        _needsRehashCalls.Add(hash);
        return TryReadIterations(hash, out var iterations) && iterations < _currentIterations;
    }

    private static PasswordHash Compute(Password password, int iterations)
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password.Reveal()));
        var stored = string.Create(
            CultureInfo.InvariantCulture,
            $"v1$pbkdf2-sha256${iterations}${FixedSalt}${encoded}");
        return PasswordHash.Parse(stored).Value;
    }

    private static bool TryReadIterations(PasswordHash hash, out int iterations) =>
        int.TryParse(
            hash.ToStoredString().Split('$')[2],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out iterations);
}
