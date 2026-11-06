using System.Security.Cryptography;
using System.Text;
using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

public sealed class FakeKeyedHasher : IKeyedHasher
{
    private readonly byte[] _key;

    public FakeKeyedHasher(string secret = "fake-keyed-hasher-secret") => _key = Encoding.UTF8.GetBytes(secret);

    public byte[] Hash(string value) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value));
}
