using System.Text.RegularExpressions;

namespace Deckloom.Api.Tests;

// T-P03 (APR-016, AC-P03b): o broker do compose de desenvolvimento nao publica porta (APR-020, SEC-17: so web e mail publicam) e recebe usuario e senha
// do ambiente (sem literal). Inspecao estatica de texto do docker-compose.yml: nada e executado.
public sealed class ComposeBrokerTests
{
    private static string[] ServiceBlock(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var lines = File.ReadAllLines(Path.Combine(dir.FullName, "docker-compose.yml"));
        var start = Array.FindIndex(lines, l => l == $"  {name}:");
        Assert.True(start >= 0, $"servico '{name}' ausente do docker-compose.yml");
        var block = lines.Skip(start + 1).TakeWhile(l => l.Length == 0 || l.StartsWith("    ", StringComparison.Ordinal)).ToArray();
        return block;
    }

    [Fact]
    public void AC_P03b_the_broker_uses_an_official_image_with_a_pinned_tag()
    {
        var image = Assert.Single(ServiceBlock("rabbitmq"), l => l.TrimStart().StartsWith("image:", StringComparison.Ordinal));

        var reference = image.Split(':', 2)[1].Trim();
        Assert.StartsWith("rabbitmq:", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("latest", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void AC_P03b_the_broker_publishes_no_ports_and_is_reached_only_through_the_internal_network()
    {
        var block = ServiceBlock("rabbitmq");

        // Pega as duas formas: "ports:" em bloco e "ports: [...]" em linha.
        Assert.DoesNotContain(block, l => l.TrimStart().StartsWith("ports:", StringComparison.Ordinal));
    }

    [Fact]
    public void AC_P03b_the_broker_credentials_come_from_the_environment_never_literals()
    {
        var block = ServiceBlock("rabbitmq");

        foreach (var key in new[] { "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS" })
        {
            var line = Assert.Single(block, l => l.TrimStart().StartsWith($"{key}:", StringComparison.Ordinal));
            Assert.Matches(@"^\s+" + key + @":\s*\$\{[A-Z0-9_]+\}\s*$", line);
        }

        Assert.DoesNotContain(block, l => l.Contains("guest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AC_P03b_the_broker_has_a_healthcheck()
    {
        Assert.Contains(ServiceBlock("rabbitmq"), l => l.Trim() == "healthcheck:");
    }
}
