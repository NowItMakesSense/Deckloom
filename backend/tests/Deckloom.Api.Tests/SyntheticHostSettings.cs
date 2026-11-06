using Microsoft.AspNetCore.Hosting;

namespace Deckloom.Api.Tests;

// T-094: a composicao valida as opcoes obrigatorias no start. As fabricas de teste que sobem o host real fornecem
// aqui valores FICTICIOS (literais do teste, nunca lidos de .env nem do ambiente). Nenhuma conexao de banco e aberta.
// Chame ANTES das configuracoes proprias da fabrica: a ultima UseSetting de uma chave vence.
internal static class SyntheticHostSettings
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["Auth:PublicOrigin"] = "https://deckloom.example.test",
        ["Auth:Google:ClientId"] = "test-client-id.apps.example.test",
        ["Auth:Google:ClientSecret"] = "test-google-client-secret-7c1d",
        ["Auth:RateLimitHmacKey"] = "test-hmac-key-0123456789abcdef0123456789abcdef",
        ["Legal:TermsVersion"] = "2026-10",
        ["Legal:TermsUrl"] = "https://deckloom.example.test/termos",
        ["Legal:PrivacyUrl"] = "https://deckloom.example.test/privacidade",
        ["Email:From"] = "no-reply@deckloom.example.test",
        ["Email:Smtp:Host"] = "smtp.example.test",
        ["ConnectionStrings:Deckloom"] =
            "Host=db-host-synthetic.internal;Database=deckloom;Username=deckloom_app;Password=test-db-secret-9f3a;SSL Mode=VerifyFull;Root Certificate=/certs/ca.pem",
    };

    public static IWebHostBuilder UseSyntheticModuleSettings(this IWebHostBuilder builder)
    {
        foreach (var (key, value) in Values)
        {
            builder.UseSetting(key, value);
        }

        return builder;
    }
}
