using System.Security.Cryptography;
using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenBytes = 32;

    public string Generate() => ToBase64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    public override string ToString() => nameof(SecureTokenGenerator);

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
