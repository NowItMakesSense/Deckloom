using System.Text;
using Deckloom.Api.Composition;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Deckloom.Api.Tests;

// T-149 (RED): R-36 / REQ-032, REQ-041 (ADR-0016, nota de 2026-11-01).
// Contrato: Forwarding:TrustedProxies so e aceita como lista (Forwarding__TrustedProxies__0, __1...). Um valor
// escalar direto na chave, em qualquer fonte de configuracao, derruba o start com InvalidOperationException cuja
// mensagem traz so o nome da chave (nunca o valor configurado).
// Lacunas assumidas: (1) a variavel de ambiente sem indice (Forwarding__TrustedProxies=...) vira, no provedor de
// ambiente, exatamente a chave escalar "Forwarding:TrustedProxies"; o teste a injeta como configuracao em memoria
// com essa chave, para nao alterar variaveis de ambiente do processo (testes rodam em paralelo); (2) appsettings.json
// e simulado por um fluxo JSON com "Forwarding": { "TrustedProxies": "..." }; (3) o start e exercitado por
// WebApplicationFactory e a excecao e procurada na cadeia de InnerException.
public sealed class TrustedProxyListTests
{
    private const string Key = "Forwarding:TrustedProxies";
    private const string ScalarValue = "172.28.10.0/24";

    [Fact]
    public void R36_scalar_value_from_an_environment_style_source_fails_the_start()
    {
        using var factory = new ProxyFactory(b => b.UseSetting(Key, ScalarValue));

        // SEC-24: so exige que o start falhe; a mensagem e testada direto em Parse (sem a corrida do WebApplicationFactory).
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void R36_scalar_string_in_appsettings_json_fails_the_start()
    {
        var json = $"{{ \"Forwarding\": {{ \"TrustedProxies\": \"{ScalarValue}\" }} }}";
        using var factory = new ProxyFactory(b => b.ConfigureAppConfiguration(
            (_, config) => config.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void R36_scalar_value_that_is_not_an_ip_also_fails_the_start()
    {
        using var factory = new ProxyFactory(b => b.UseSetting(Key, "segredo-que-nao-pode-vazar"));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void R36_missing_key_still_starts_with_the_loopback_default()
    {
        using var factory = new ProxyFactory(_ => { });

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void R36_indexed_list_still_starts()
    {
        using var factory = new ProxyFactory(b =>
        {
            b.UseSetting($"{Key}:0", ScalarValue);
            b.UseSetting($"{Key}:1", "10.0.0.9");
        });

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    // QA (fase 6): entradas invalidas dentro de uma lista indexada derrubam o start (falha fechada), mesmo ao lado
    // de entradas validas; nada cai em silencio no padrao loopback.
    [Theory]
    [InlineData("nao-e-ip")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/")]
    [InlineData("/24")]
    [InlineData("")]
    [InlineData(" 10.0.0.1")]
    [InlineData("10.0.0.1/24/8")]
    public void QA_invalid_entry_in_an_indexed_list_fails_the_start_even_next_to_a_valid_one(string invalidEntry)
    {
        using var factory = new ProxyFactory(b =>
        {
            b.UseSetting($"{Key}:0", "10.0.0.9");
            b.UseSetting($"{Key}:1", invalidEntry);
        });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void QA_ipv6_address_and_ipv6_cidr_entries_are_accepted()
    {
        using var factory = new ProxyFactory(b =>
        {
            b.UseSetting($"{Key}:0", "fd00:1::2");
            b.UseSetting($"{Key}:1", "fd00:2::/48");
            b.UseSetting($"{Key}:2", "172.28.10.0/24");
        });

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    // T-163 (RED): R-48 / REQ-032, REQ-041. A mensagem de falha de uma entrada indexada invalida traz a chave e o
    // indice (Forwarding:TrustedProxies:1), nunca o valor configurado (um segredo colado no lugar errado vazaria no log).
    [Theory]
    [InlineData("segredo-que-nao-pode-vazar")]
    [InlineData("10.0.0.0/segredo-33")]
    [InlineData("999.999.999.999/segredo")]
    public void R48_invalid_indexed_entry_fails_the_start(string invalidEntry)
    {
        using var factory = new ProxyFactory(b =>
        {
            b.UseSetting($"{Key}:0", "10.0.0.9");
            b.UseSetting($"{Key}:1", invalidEntry);
        });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    // SEC-24: a mensagem e verificada direto em Parse (deterministico, sem host). Chave e indice, nunca o valor.
    [Theory]
    [InlineData("segredo-que-nao-pode-vazar")]
    [InlineData("10.0.0.0/segredo-33")]
    [InlineData("999.999.999.999/segredo")]
    [InlineData("nao-e-ip")]
    [InlineData("10.0.0.0/33")]
    [InlineData("")]
    public void R48_parse_of_an_invalid_indexed_entry_names_key_and_index_but_never_the_value(string invalidEntry)
    {
        var section = SectionOf(($"{Key}:0", "10.0.0.9"), ($"{Key}:1", invalidEntry));

        var error = Assert.Throws<InvalidOperationException>(() => TrustedProxyList.Parse(section));

        Assert.Contains($"{Key}:1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("segredo", error.Message, StringComparison.Ordinal);
        if (invalidEntry.Length > 0)
        {
            Assert.DoesNotContain(invalidEntry, error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("172.28.10.0/24")]
    [InlineData("segredo-que-nao-pode-vazar")]
    public void R36_parse_of_a_scalar_value_names_only_the_key(string scalar)
    {
        var section = SectionOf((Key, scalar));

        var error = Assert.Throws<InvalidOperationException>(() => TrustedProxyList.Parse(section));

        Assert.Contains(Key, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(scalar, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R36_parse_of_a_scalar_string_in_json_names_only_the_key()
    {
        var json = $"{{ \"Forwarding\": {{ \"TrustedProxies\": \"{ScalarValue}\" }} }}";
        var config = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

        var error = Assert.Throws<InvalidOperationException>(() => TrustedProxyList.Parse(config.GetSection(Key)));

        Assert.Contains(Key, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("172.28.10.0", error.Message, StringComparison.Ordinal);
    }

    private static IConfigurationSection SectionOf(params (string Name, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Name, v.Value)))
            .Build()
            .GetSection(Key);

    // T-163 (RED): higiene. As mensagens de falha sao constantes (const string) e nao interpolam a entrada lida.
    [Fact]
    public void R48_failure_messages_are_constants_that_do_not_interpolate_the_configured_entry()
    {
        var source = File.ReadAllText(Path.Combine(
            BackendRoot(), "src", "Deckloom.Api", "Composition", "TrustedProxyList.cs"));

        Assert.DoesNotContain("{entry}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("{child.Value}", source, StringComparison.Ordinal);
        // SectionName + as duas mensagens (escalar e indexada).
        Assert.True(
            System.Text.RegularExpressions.Regex.Count(source, @"\bconst\s+string\b") >= 3,
            "TrustedProxyList deve declarar as mensagens de falha como const string.");
    }

    private static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Deckloom.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("backend/Deckloom.sln nao encontrada.");
    }

    private sealed class ProxyFactory(Action<IWebHostBuilder> configure) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", "https://deckloom.example.test");
            configure(builder);
            builder.ConfigureServices(services => services.RemoveDatabaseRoleCheck());
        }
    }
}
