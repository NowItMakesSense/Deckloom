using System.Security.Cryptography;
using System.Text;
using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

public sealed class HmacKeyedHasher : IKeyedHasher
{
    private const int MinKeyBytes = 32;

    private readonly byte[] _key;

    public HmacKeyedHasher(RateLimitHmacOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RateLimitHmacKey))
        {
            throw new ArgumentException("A chave HMAC do rate limit nao foi configurada.", nameof(options));
        }

        _key = Encoding.UTF8.GetBytes(options.RateLimitHmacKey);
        if (_key.Length < MinKeyBytes)
        {
            throw new ArgumentException($"A chave HMAC do rate limit deve ter ao menos {MinKeyBytes} bytes.", nameof(options));
        }
    }

    public byte[] Hash(string value) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value));

    public override string ToString() => nameof(HmacKeyedHasher);
}
