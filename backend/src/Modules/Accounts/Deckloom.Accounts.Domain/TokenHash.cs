using System.Security.Cryptography;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class TokenHash : IEquatable<TokenHash>
{
    public const string InvalidCode = "invalid";
    public const int Length = 32;

    private readonly byte[] _sha256;

    private TokenHash(byte[] sha256) => _sha256 = sha256;

    public static Result<TokenHash> Create(byte[]? sha256)
    {
        return sha256 is { Length: Length }
            ? Result<TokenHash>.Success(new TokenHash((byte[])sha256.Clone()))
            : Result<TokenHash>.Failure(new DomainError(InvalidCode));
    }

    public byte[] ToBytes() => (byte[])_sha256.Clone();

    public bool Equals(TokenHash? other) =>
        other is not null && CryptographicOperations.FixedTimeEquals(_sha256, other._sha256);

    public override bool Equals(object? obj) => Equals(obj as TokenHash);

    public override int GetHashCode() => BitConverter.ToInt32(_sha256, 0);

    public override string ToString() => "TokenHash(***)";
}
