using System.Net;

namespace Deckloom.Api.Composition;

// Proxies reversos cujo X-Forwarded-For e aceito (R-14), lidos de Forwarding:TrustedProxies. Cada entrada e um IP ou um CIDR.
internal sealed record TrustedProxyList(IReadOnlyList<IPAddress> Addresses, IReadOnlyList<IPNetwork> Networks)
{
    public const string SectionName = "Forwarding:TrustedProxies";

    private const string ScalarValueMessage =
        "Forwarding:TrustedProxies deve ser uma lista indexada (ex.: Forwarding:TrustedProxies:0), nao um valor unico.";

    private const string InvalidEntryMessageSuffix =
        " e uma entrada invalida: use um IP (10.0.0.1) ou um CIDR (172.28.10.0/24).";

    public static TrustedProxyList Parse(IConfigurationSection section)
    {
        var entries = section.GetChildren().ToList();
        if (entries.Count == 0 && !string.IsNullOrEmpty(section.Value))
        {
            // Valor escalar em vez de lista indexada: falha fechada, sem ecoar o valor configurado.
            throw new InvalidOperationException(ScalarValueMessage);
        }

        if (entries.Count == 0)
        {
            return new TrustedProxyList([IPAddress.Loopback, IPAddress.IPv6Loopback], []);
        }

        var addresses = new List<IPAddress>();
        var networks = new List<IPNetwork>();
        foreach (var child in entries)
        {
            var entry = child.Value;
            if (entry is not null && entry.Contains('/', StringComparison.Ordinal) && IPNetwork.TryParse(entry, out var network))
            {
                networks.Add(network);
            }
            else if (entry is not null && !entry.Contains('/', StringComparison.Ordinal) && IPAddress.TryParse(entry, out var address))
            {
                addresses.Add(address);
            }
            else
            {
                // Nomeia a chave indexada, nunca o valor configurado.
                throw new InvalidOperationException($"{child.Path}{InvalidEntryMessageSuffix}");
            }
        }

        return new TrustedProxyList(addresses, networks);
    }
}
