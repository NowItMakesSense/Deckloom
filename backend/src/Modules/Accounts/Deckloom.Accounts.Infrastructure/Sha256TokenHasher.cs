using System.Security.Cryptography;
using System.Text;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure;

public sealed class Sha256TokenHasher : ITokenHasher
{
    public TokenHash Hash(string token) =>
        TokenHash.Create(SHA256.HashData(Encoding.UTF8.GetBytes(token))).Value;

    public override string ToString() => nameof(Sha256TokenHasher);
}
