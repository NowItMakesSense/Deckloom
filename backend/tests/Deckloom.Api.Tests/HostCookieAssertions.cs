namespace Deckloom.Api.Tests;

// T-147: toda resposta com Set-Cookie de prefixo __Host- exige Secure, Path=/ e nenhum Domain (o navegador recusa o
// cookie por inteiro se faltar algum). Vale tambem para o cookie que apaga outro. A mensagem de falha traz so o nome do
// cookie e o atributo, nunca o valor.
internal static class HostCookieAssertions
{
    public const string HostPrefix = "__Host-";

    public static IReadOnlyList<string> AssertAllHostPrefixed(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var observed = new List<string>();
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return observed;
        }

        foreach (var header in headers)
        {
            var parts = header.Split(';', StringSplitOptions.TrimEntries);
            var name = parts[0].Split('=', 2)[0];
            if (!name.StartsWith(HostPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            observed.Add(name);
            var attributes = parts.Skip(1).ToList();
            Assert.True(
                attributes.Any(a => string.Equals(a, "secure", StringComparison.OrdinalIgnoreCase)),
                $"O cookie {name} saiu sem Secure.");
            Assert.True(
                attributes.Any(a => string.Equals(a, "path=/", StringComparison.OrdinalIgnoreCase)),
                $"O cookie {name} saiu sem Path=/.");
            Assert.False(
                attributes.Any(a => a.StartsWith("domain=", StringComparison.OrdinalIgnoreCase)),
                $"O cookie {name} saiu com Domain.");
        }

        return observed;
    }
}
