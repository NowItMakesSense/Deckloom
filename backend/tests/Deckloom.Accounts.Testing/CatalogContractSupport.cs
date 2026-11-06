using Deckloom.Accounts.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Testing;

// T-145: o que os contratos dos catalogos precisam de cada lado. O harness embrulha o catalogo sob teste e as duas
// alavancas que os testes usam: ativar/desativar uma entrada (a escrita e do operador do catalogo, nao da porta) e avancar o
// relogio (o cache de 5 min do preset). Cada lado monta o seu e o descarta no fim do teste.
public sealed class CatalogHarness<TCatalog>(
    TCatalog catalog,
    FakeTimeProvider clock,
    Func<string, bool, Task> setActive,
    IDisposable? owner = null) : IDisposable
{
    public TCatalog Catalog { get; } = catalog;

    public FakeTimeProvider Clock { get; } = clock;

    public Task SetActive(string id, bool isActive) => setActive(id, isActive);

    public void Dispose() => owner?.Dispose();
}

// Semente de dados unica para os dois lados (refactor da T-145).
public static class CatalogContractSeed
{
    public static IReadOnlyList<AvatarPresetEntry> Presets { get; } =
    [
        new("mago-azul", "Mago azul", "/avatars/presets/mago-azul.svg", 2, true, true),
        new("guerreiro", "Guerreiro", "/avatars/presets/guerreiro.svg", 1, false, true),
        new("arqueira", "Arqueira", "/avatars/presets/arqueira.svg", 3, false, true),
        new("antigo", "Antigo", "/avatars/presets/antigo.svg", 4, false, false),
    ];

    public static IReadOnlyList<FormatEntry> Formats { get; } =
    [
        new(new FormatId("modern"), "Modern", 2, true),
        new(new FormatId("standard"), "Standard", 1, true),
        new(new FormatId("commander"), "Commander", 4, true),
        new(new FormatId("pauper"), "Pauper", 3, true),
        new(new FormatId("legacy"), "Legacy", 5, false),
    ];
}
