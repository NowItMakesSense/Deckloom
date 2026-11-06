using System.Text;
using Deckloom.Accounts.Domain;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Deckloom.Api.Composition;

// As mensagens de validacao nomeiam a CHAVE de configuracao e nunca o valor configurado.
internal static class ValidationHelpers
{
    public static void Require(List<string> failures, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{key} e obrigatoria.");
        }
    }

    public static void RequireAbsoluteUri(List<string> failures, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{key} e obrigatoria.");
        }
        else if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{key} deve ser uma URL absoluta http(s).");
        }
    }

    // SEC-06: fora de Development, o transporte de saida e o das URLs de identidade falham fechado (https obrigatorio).
    public static void RequireHttps(List<string> failures, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{key} deve usar https fora de Development.");
        }
    }

    public static ValidateOptionsResult Result(List<string> failures) =>
        failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
}

internal sealed class AuthOptionsValidator(bool isDevelopment) : IValidateOptions<AuthOptions>
{
    private const int MinHmacKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();
        ValidationHelpers.RequireAbsoluteUri(failures, "Auth:PublicOrigin", options.PublicOrigin);
        ValidationHelpers.Require(failures, "Auth:RateLimitHmacKey", options.RateLimitHmacKey);
        if (!string.IsNullOrWhiteSpace(options.RateLimitHmacKey)
            && Encoding.UTF8.GetByteCount(options.RateLimitHmacKey) < MinHmacKeyBytes)
        {
            failures.Add($"Auth:RateLimitHmacKey deve ter ao menos {MinHmacKeyBytes} bytes.");
        }

        ValidationHelpers.RequireAbsoluteUri(failures, "Auth:Google:Authority", options.Google.Authority);
        ValidationHelpers.Require(failures, "Auth:Google:ClientId", options.Google.ClientId);
        ValidationHelpers.Require(failures, "Auth:Google:ClientSecret", options.Google.ClientSecret);
        if (!isDevelopment)
        {
            ValidationHelpers.RequireHttps(failures, "Auth:PublicOrigin", options.PublicOrigin);
            ValidationHelpers.RequireHttps(failures, "Auth:Google:Authority", options.Google.Authority);
        }

        return ValidationHelpers.Result(failures);
    }
}

internal sealed class LegalOptionsValidator : IValidateOptions<LegalOptions>
{
    public ValidateOptionsResult Validate(string? name, LegalOptions options)
    {
        var failures = new List<string>();
        ValidationHelpers.Require(failures, "Legal:TermsVersion", options.TermsVersion);
        ValidationHelpers.RequireAbsoluteUri(failures, "Legal:TermsUrl", options.TermsUrl);
        ValidationHelpers.RequireAbsoluteUri(failures, "Legal:PrivacyUrl", options.PrivacyUrl);
        return ValidationHelpers.Result(failures);
    }
}

internal sealed class EmailOptionsValidator(bool isDevelopment) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var failures = new List<string>();
        ValidationHelpers.Require(failures, "Email:From", options.From);
        ValidationHelpers.Require(failures, "Email:Smtp:Host", options.Smtp.Host);
        if (options.Smtp.Port is < 1 or > 65535)
        {
            failures.Add("Email:Smtp:Port deve estar entre 1 e 65535.");
        }

        // O email leva o link de redefinicao (credencial equivalente por 60 min): sem TLS so em Development.
        if (!isDevelopment && !options.Smtp.UseTls)
        {
            failures.Add("Email:Smtp:UseTls deve ser true fora de Development.");
        }

        // SEC-22: a senha SMTP iria em claro; com usuario configurado, sem TLS nao inicia em nenhum ambiente.
        if (!string.IsNullOrWhiteSpace(options.Smtp.User) && !options.Smtp.UseTls)
        {
            failures.Add("Email:Smtp:User exige Email:Smtp:UseTls=true.");
        }

        return ValidationHelpers.Result(failures);
    }
}

// ADR-0005 (emenda SEC-01): orcamento por conta entre 1 e 100 (limite da ASVS 2.2.1). A regra mora na
// ProgressiveDelayPolicy; aqui ela vira falha de start que nomeia a chave, sem repetir o valor.
internal sealed class ProgressiveDelayOptionsValidator : IValidateOptions<ProgressiveDelayOptions>
{
    public ValidateOptionsResult Validate(string? name, ProgressiveDelayOptions options)
    {
        var failures = new List<string>();
        try
        {
            _ = new ProgressiveDelayPolicy(options);
        }
        catch (ArgumentException)
        {
            if (options.AccountHourlyBudget is < 1 or > 100)
            {
                failures.Add("Auth:ProgressiveDelay:AccountHourlyBudget deve estar entre 1 e 100.");
            }
            else
            {
                failures.Add("Auth:ProgressiveDelay contem valores invalidos (limiares, BaseDelay ou MaxDelay).");
            }
        }

        return ValidationHelpers.Result(failures);
    }
}

internal sealed class AvatarOptionsValidator : IValidateOptions<AvatarOptions>
{
    public ValidateOptionsResult Validate(string? name, AvatarOptions options)
    {
        var failures = new List<string>();
        if (options.MaxBytes < 1)
        {
            failures.Add("Avatar:MaxBytes deve ser maior que zero.");
        }

        if (options.MaxDimension < 1)
        {
            failures.Add("Avatar:MaxDimension deve ser maior que zero.");
        }

        if (options.MaxPixels < 1)
        {
            failures.Add("Avatar:MaxPixels deve ser maior que zero.");
        }

        return ValidationHelpers.Result(failures);
    }
}

// Controle de acesso ao banco (design DB1/DB7/DB8): so o papel deckloom_app; TLS completo fora de Development.
// A lista de chaves proibidas vem de GetChildren() da secao, entao o valor nunca e lido para a mensagem.
internal sealed class DatabaseOptionsValidator(IConfiguration configuration, bool isDevelopment)
    : IValidateOptions<DatabaseOptions>
{
    public const string AppRole = "deckloom_app";

    private const string Key = "ConnectionStrings:Deckloom";

    private static readonly string[] ForbiddenRoleKeys = ["Migrator", "Owner", "Postgres", "Admin"];

    private static readonly string[] ForbiddenKeyPrefixes = ["POSTGRES_", "DECKLOOM_MIGRATOR_"];

    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var failures = new List<string>();
        foreach (var child in configuration.GetSection("ConnectionStrings").GetChildren())
        {
            if (ForbiddenRoleKeys.Contains(child.Key, StringComparer.OrdinalIgnoreCase))
            {
                failures.Add($"ConnectionStrings:{child.Key} nao e permitida: a API so usa o papel {AppRole}.");
            }
        }

        CollectForbiddenPrefixKeys(configuration, failures);

        if (string.IsNullOrWhiteSpace(options.Deckloom))
        {
            failures.Add($"{Key} e obrigatoria.");
        }
        else
        {
            ValidateConnection(options.Deckloom, failures);
        }

        return ValidationHelpers.Result(failures);
    }

    // Varre a configuracao inteira (qualquer nivel); so a CHAVE entra na mensagem, nunca o valor.
    private static void CollectForbiddenPrefixKeys(IConfiguration section, List<string> failures)
    {
        foreach (var child in section.GetChildren())
        {
            if (ForbiddenKeyPrefixes.Any(prefix => child.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"A chave de configuracao {child.Path} nao e permitida: a API nao recebe credencial do servidor nem do migrador.");
            }

            CollectForbiddenPrefixKeys(child, failures);
        }
    }

    private void ValidateConnection(string connection, List<string> failures)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connection);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            failures.Add($"{Key} nao e uma cadeia de conexao valida.");
            return;
        }

        if (!string.Equals(builder.Username, AppRole, StringComparison.Ordinal))
        {
            failures.Add($"{Key} deve usar o papel {AppRole}.");
        }

        if (isDevelopment)
        {
            return;
        }

        if (builder.SslMode != SslMode.VerifyFull)
        {
            failures.Add($"{Key} deve usar SSL Mode=VerifyFull fora de Development.");
        }
        else if (string.IsNullOrWhiteSpace(builder.RootCertificate))
        {
            failures.Add($"{Key} exige Root Certificate fora de Development.");
        }
    }
}
