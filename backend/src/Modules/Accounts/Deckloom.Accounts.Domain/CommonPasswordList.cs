using System.Reflection;

namespace Deckloom.Accounts.Domain;

// SEC-05 (ASVS 2.1.7): lista local de senhas comuns (recurso embutido, sem chamada externa), carregada uma vez.
// A comparacao e exata (Ordinal): so a forma armazenada na lista e recusada. Usada apenas ao DEFINIR senha
// (cadastro e redefinicao); nunca no login.
public static class CommonPasswordList
{
    private const string ResourceName = "Deckloom.Accounts.Domain.Resources.common-passwords.txt";

    private static readonly HashSet<string> Entries = Load();

    public static int Count => Entries.Count;

    public static bool Contains(string? password) =>
        !string.IsNullOrEmpty(password) && Entries.Contains(password);

    private static HashSet<string> Load()
    {
        using var stream = typeof(CommonPasswordList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Recurso embutido ausente: {ResourceName}.");
        using var reader = new StreamReader(stream);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
            {
                entries.Add(line);
            }
        }

        return entries;
    }
}
