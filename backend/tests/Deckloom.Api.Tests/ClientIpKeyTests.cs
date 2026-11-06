using System.Net;
using Deckloom.Api.Http;

namespace Deckloom.Api.Tests;

// SEC-02 (ASVS V2.2.1, V11.1.4): a chave de IP do atraso progressivo e do limitador de concorrencia e unica e
// normalizada. IPv6 e truncado em /64 (um cliente comum controla o /64 inteiro); IPv4 e IPv4 mapeado em IPv6 valem
// o IPv4 inteiro.
public sealed class ClientIpKeyTests
{
    [Fact]
    public void SEC02_two_addresses_in_the_same_slash_64_share_the_key()
    {
        var first = ClientIpKey.From(IPAddress.Parse("2001:db8:1:2::1"));
        var second = ClientIpKey.From(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff"));

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:3::1")]
    [InlineData("2001:db8:1:2::1", "2001:db8:2:2::1")]
    [InlineData("2001:db8:1:2::1", "2001:db9:1:2::1")]
    public void SEC02_addresses_in_different_slash_64_prefixes_do_not_share_the_key(string a, string b)
    {
        Assert.NotEqual(ClientIpKey.From(IPAddress.Parse(a)), ClientIpKey.From(IPAddress.Parse(b)));
    }

    [Fact]
    public void SEC02_the_ipv6_key_is_the_slash_64_prefix_in_canonical_form()
    {
        Assert.Equal("2001:db8:1:2::/64", ClientIpKey.From(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd")));
    }

    [Fact]
    public void SEC02_ipv4_keeps_the_full_address_so_neighbours_do_not_share_the_key()
    {
        Assert.Equal("203.0.113.7", ClientIpKey.From(IPAddress.Parse("203.0.113.7")));
        Assert.NotEqual(ClientIpKey.From(IPAddress.Parse("203.0.113.7")), ClientIpKey.From(IPAddress.Parse("203.0.113.8")));
    }

    [Fact]
    public void SEC02_an_ipv4_mapped_ipv6_address_has_the_same_key_as_the_ipv4()
    {
        Assert.Equal(
            ClientIpKey.From(IPAddress.Parse("203.0.113.7")),
            ClientIpKey.From(IPAddress.Parse("::ffff:203.0.113.7")));
    }

    [Fact]
    public void SEC02_an_unknown_address_keeps_the_unknown_key()
    {
        Assert.Equal("unknown", ClientIpKey.From((IPAddress?)null));
    }

    [Fact]
    public void SEC02_the_ipv4_key_never_collides_with_an_ipv6_prefix_key()
    {
        Assert.NotEqual(ClientIpKey.From(IPAddress.Parse("0.0.0.1")), ClientIpKey.From(IPAddress.Parse("::1")));
    }
}
