namespace Deckloom.Api.E2E.Host;

internal static class E2eEnvironment
{
    public const string Name = "E2E";

    public static bool IsE2E(IHostEnvironment environment) => environment.IsEnvironment(Name);

    // Valor sintetico: o validador de opcoes exige a chave, mas nenhuma conexao e aberta (persistencia em memoria).
    // So entra se nada configurou a chave; nao e uma credencial.
    public static IEnumerable<KeyValuePair<string, string?>> PlaceholderSettings(IConfiguration configuration)
    {
        const string key = "ConnectionStrings:Deckloom";
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            yield return new(
                key,
                "Host=e2e-never-connected.invalid;Database=e2e;Username=deckloom_app;SSL Mode=VerifyFull;Root Certificate=/e2e/unused.pem");
        }
    }
}
