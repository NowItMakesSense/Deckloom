namespace Deckloom.Api.Auth;

// R-34: o returnTo do login Google fica com a API (design: cookie de correlacao) e so passa se for caminho interno
// conhecido; espelha KNOWN_ROUTES do front (query e hash descartados). As rotas do fluxo Google ficam de fora (evita laco).
public static class SafeReturnTo
{
    private static readonly HashSet<string> KnownRoutes = new(StringComparer.Ordinal)
    {
        "/cadastro",
        "/entrar",
        "/esqueci-senha",
        "/redefinir-senha",
        "/perfil",
        "/perfil/editar",
    };

    // Devolve o caminho validado ou null (o front cai em /perfil).
    public static string? Validate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        var end = value.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? value : value[..end];
        return KnownRoutes.Contains(path) ? path : null;
    }
}
