using System.Globalization;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class PasswordHash
{
    public const string InvalidCode = "invalid";

    private const string Version = "v1";
    private const string Algorithm = "pbkdf2-sha256";
    private const int SegmentCount = 5;

    private readonly string _stored;

    private PasswordHash(string stored) => _stored = stored;

    public static Result<PasswordHash> Parse(string? stored)
    {
        return IsWellFormed(stored)
            ? Result<PasswordHash>.Success(new PasswordHash(stored!))
            : Result<PasswordHash>.Failure(new DomainError(InvalidCode));
    }

    public string ToStoredString() => _stored;

    public override string ToString() => "PasswordHash(***)";

    private static bool IsWellFormed(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        var segments = stored.Split('$');

        return segments.Length == SegmentCount
            && segments[0] == Version
            && segments[1] == Algorithm
            && int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            && iterations > 0
            && IsBase64(segments[3])
            && IsBase64(segments[4]);
    }

    private static bool IsBase64(string segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        var buffer = new byte[segment.Length];
        return Convert.TryFromBase64String(segment, buffer, out _);
    }
}
