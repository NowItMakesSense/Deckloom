using System.Net;

namespace Deckloom.Api.Http;

// SEC-02: ponto unico que transforma o IP de conexao na chave usada pelo atraso progressivo (HMAC) e pelo limitador de
// concorrencia por IP. IPv4 vale inteiro; IPv4 mapeado em IPv6 vira o IPv4; IPv6 e truncado no prefixo /64, porque um
// cliente comum controla o /64 inteiro (2^64 enderecos) e nao pode trocar de chave a cada requisicao.
public static class ClientIpKey
{
    public const string Unknown = "unknown";

    private const int Ipv6PrefixBytes = 8;

    public static string From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return From(context.Connection.RemoteIpAddress);
    }

    public static string From(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[Ipv6PrefixBytes..].Clear();
        return $"{new IPAddress(bytes)}/64";
    }
}
