using Deckloom.Api.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Deckloom.Api.Tests;

// T-136 (RED): TLS do banco e configuracao sem credencial privilegiada.
// Todas as connection strings sao FICTICIAS, escritas aqui (nunca lidas de .env). Nada conecta ao banco:
// a validacao roda por IStartupValidator sobre um ServiceCollection, sem executar IHostedService.
// Complementa CompositionTests (T-094), que ja cobre Require/VerifyCA/Prefer/Disable em Production, VerifyFull sem
// Root Certificate, Disable em Development, papeis proibidos e ConnectionStrings:{Migrator,Owner,Postgres,Admin}.
public sealed class DatabaseConnectionOptionsTests
{
    private const string Host = "db-host-synthetic.internal";
    private const string DbPassword = "test-db-password-9f3a";
    private const string CertPath = "/certs/synthetic-root-ca.pem";
    private const string AppUser = "deckloom_app";
    private const string ConnectionKey = "ConnectionStrings:Deckloom";

    private static string Connection(string? sslMode, bool rootCertificate, string user = AppUser)
    {
        var text = $"Host={Host};Database=deckloom;Username={user};Password={DbPassword}";
        if (sslMode is not null)
        {
            text += $";SSL Mode={sslMode}";
        }
        if (rootCertificate)
        {
            text += $";Root Certificate={CertPath}";
        }
        return text;
    }

    private static Dictionary<string, string?> Config(string connection) => new()
    {
        ["Auth:PublicOrigin"] = "https://deckloom.example.test",
        ["Auth:Google:ClientId"] = "test-client-id.apps.example.test",
        ["Auth:Google:ClientSecret"] = "test-google-client-secret-7c1d",
        ["Auth:RateLimitHmacKey"] = "test-hmac-key-0123456789abcdef0123456789abcdef",
        ["Legal:TermsVersion"] = "2026-10",
        ["Legal:TermsUrl"] = "https://deckloom.example.test/termos",
        ["Legal:PrivacyUrl"] = "https://deckloom.example.test/privacidade",
        ["Email:Smtp:Host"] = "smtp.example.test",
        ["Email:Smtp:Port"] = "587",
        ["Email:Smtp:UseTls"] = "true",
        ["Email:Smtp:User"] = "mailer",
        ["Email:Smtp:Password"] = "test-smtp-password-5b2e",
        ["Email:From"] = "no-reply@deckloom.example.test",
        ["Avatar:MaxBytes"] = "123456",
        [ConnectionKey] = connection,
    };

    // ---- SSL Mode fora de Development ------------------------------------------------------------------------

    public static TheoryData<string, string?> WeakTlsOutsideDevelopment()
    {
        var data = new TheoryData<string, string?>();
        foreach (var environment in new[] { "Production", "Staging", "Qa-Synthetic-Unknown" })
        {
            foreach (var sslMode in new string?[] { null, "Disable", "Allow", "Prefer", "Require", "VerifyCA" })
            {
                data.Add(environment, sslMode);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(WeakTlsOutsideDevelopment))]
    public void T136_outside_development_weak_or_missing_ssl_mode_is_refused(string environment, string? sslMode)
    {
        var config = Config(Connection(sslMode, rootCertificate: true));

        var message = ValidationFailureMessage(config, environment);

        Assert.Contains(ConnectionKey, message, StringComparison.Ordinal);
        AssertNoSensitiveValue(message, config);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Qa-Synthetic-Unknown")]
    public void T136_outside_development_verify_full_without_root_certificate_is_refused(string environment)
    {
        var config = Config(Connection("VerifyFull", rootCertificate: false));

        var message = ValidationFailureMessage(config, environment);

        Assert.Contains(ConnectionKey, message, StringComparison.Ordinal);
        AssertNoSensitiveValue(message, config);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Qa-Synthetic-Unknown")]
    public void T136_outside_development_verify_full_with_root_certificate_is_accepted(string environment)
    {
        using var provider = BuildProvider(Config(Connection("VerifyFull", rootCertificate: true)), environment);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void T136_development_accepts_ssl_mode_disable()
    {
        using var provider = BuildProvider(Config(Connection("Disable", rootCertificate: false)), "Development");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    // ---- Papel privilegiado em qualquer ambiente --------------------------------------------------------------

    [Theory]
    [InlineData("Development", "postgres")]
    [InlineData("Development", "deckloom_owner")]
    [InlineData("Development", "deckloom_migrator")]
    [InlineData("Staging", "postgres")]
    [InlineData("Staging", "deckloom_owner")]
    [InlineData("Staging", "deckloom_migrator")]
    [InlineData("Qa-Synthetic-Unknown", "postgres")]
    [InlineData("Production", "deckloom_owner")]
    [InlineData("Production", "deckloom_migrator")]
    public void T136_privileged_role_is_refused_in_any_environment_without_echoing_the_role(string environment, string user)
    {
        var sslMode = environment == "Development" ? "Disable" : "VerifyFull";
        var config = Config(Connection(sslMode, rootCertificate: environment != "Development", user: user));

        var message = ValidationFailureMessage(config, environment);

        Assert.Contains(ConnectionKey, message, StringComparison.Ordinal);
        Assert.DoesNotContain(user, message, StringComparison.OrdinalIgnoreCase);
        AssertNoSensitiveValue(message, config);
    }

    // ---- Chaves proibidas na IConfiguration (inclui variaveis de ambiente achatadas) ---------------------------

    [Theory]
    [InlineData("POSTGRES_PASSWORD")]
    [InlineData("POSTGRES_USER")]
    [InlineData("POSTGRES_DB")]
    [InlineData("DECKLOOM_MIGRATOR_DB_PASSWORD")]
    [InlineData("DECKLOOM_MIGRATOR_ANYTHING")]
    public void T136_configuration_key_with_forbidden_prefix_makes_startup_fail_without_leaking_its_value(string key)
    {
        const string secret = "test-forbidden-secret-value-77aa";
        var config = Config(Connection("VerifyFull", rootCertificate: true));
        config[key] = secret;

        var message = ValidationFailureMessage(config, "Production");

        Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        AssertNoSensitiveValue(message, config);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void T136_forbidden_prefix_fails_in_every_environment(string environment)
    {
        var sslMode = environment == "Development" ? "Disable" : "VerifyFull";
        var config = Config(Connection(sslMode, rootCertificate: environment != "Development"));
        config["POSTGRES_PASSWORD"] = "test-forbidden-secret-value-77aa";

        Assert.NotEmpty(ValidationFailureMessage(config, environment));
    }

    // ---- Mensagem diz so a regra -------------------------------------------------------------------------------

    [Fact]
    public void T136_failure_message_never_contains_host_user_password_or_certificate_path()
    {
        var config = Config(Connection("Require", rootCertificate: true, user: "deckloom_migrator"));
        config["POSTGRES_PASSWORD"] = "test-forbidden-secret-value-77aa";

        var message = ValidationFailureMessage(config, "Production");

        foreach (var forbidden in new[] { Host, DbPassword, CertPath, "deckloom_migrator", "test-forbidden-secret-value-77aa", "Password=" })
        {
            Assert.DoesNotContain(forbidden, message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Estatico: appsettings*.json sem credencial -------------------------------------------------------------

    [Fact]
    public void T136_no_api_appsettings_file_carries_a_password_connection_string_or_forbidden_key()
    {
        var apiDirectory = FindApiDirectory();

        foreach (var file in Directory.EnumerateFiles(apiDirectory, "appsettings*.json", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            Assert.False(text.Contains("Password=", StringComparison.OrdinalIgnoreCase), $"{name} contem 'Password='");
            Assert.False(text.Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase), $"{name} contem connection string");
            Assert.False(text.Contains("POSTGRES_", StringComparison.Ordinal), $"{name} contem chave POSTGRES_");
            Assert.False(text.Contains("DECKLOOM_MIGRATOR_", StringComparison.Ordinal), $"{name} contem chave DECKLOOM_MIGRATOR_");
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static string FindApiDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src", "Deckloom.Api");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("backend/src/Deckloom.Api nao encontrado a partir do diretorio de teste");
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values, string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountsModule(configuration, new TestEnvironment(environment));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static string ValidationFailureMessage(Dictionary<string, string?> config, string environment)
    {
        using var provider = BuildProvider(config, environment);
        var failure = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        return FullMessage(failure);
    }

    private static string FullMessage(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            parts.Add(e.Message);
            if (e is OptionsValidationException ove)
            {
                parts.AddRange(ove.Failures);
            }
        }
        return string.Join(" | ", parts);
    }

    private static void AssertNoSensitiveValue(string message, Dictionary<string, string?> config)
    {
        Assert.DoesNotContain(Host, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DbPassword, message, StringComparison.Ordinal);
        Assert.DoesNotContain(CertPath, message, StringComparison.Ordinal);
        foreach (var value in config.Where(kv => kv.Key != ConnectionKey).Select(kv => kv.Value)
                     .Where(v => !string.IsNullOrEmpty(v) && v.Length >= 6))
        {
            Assert.DoesNotContain(value!, message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Deckloom.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
