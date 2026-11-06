using System.Security.Cryptography;

namespace Deckloom.Accounts.Infrastructure;

internal sealed class Rfc2898Pbkdf2Deriver : IPbkdf2Deriver
{
    public byte[] Derive(byte[] password, byte[] salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);
}
