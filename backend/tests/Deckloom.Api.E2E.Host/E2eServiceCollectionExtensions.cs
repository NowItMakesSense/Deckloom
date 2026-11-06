using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;
using Deckloom.Api.Composition;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Api.E2E.Host;

// Ponto inicial do relogio falso, para POST /__test/clock/reset.
internal sealed record E2eClockOrigin(DateTimeOffset Start);

internal static class E2eServiceCollectionExtensions
{
    private static readonly string[] PresetIds =
    [
        "circulo-azul", "triangulo-vermelho", "quadrado-verde", "estrela-roxa",
        "onda-ciano", "losango-laranja", "folha-verde", "chama-amarela",
    ];

    // O host de teste nunca fala com banco: a verificacao do papel de conexao (DB7) so existe com o adapter PostgreSQL.
    public static IServiceCollection RemoveDatabaseRoleCheck(this IServiceCollection services)
    {
        services.RemoveAll<IDatabaseRoleInspector>();
        RemoveHostedService<DatabaseRoleStartupCheck>(services);
        return services;
    }

    // Persistencia em memoria, relogio falso, notificador que grava o link e processador de imagem real (Skia).
    public static IServiceCollection AddE2eAdapters(this IServiceCollection services)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var unitOfWork = new InMemoryUnitOfWork();
        var notifier = new RecordingPasswordResetNotifier();

        Replace<TimeProvider>(services, clock);
        services.AddSingleton(clock);
        services.AddSingleton(new E2eClockOrigin(clock.GetUtcNow()));
        services.AddSingleton(notifier);

        Replace<IUnitOfWork>(services, unitOfWork);
        Replace<IPlayerRepository>(services, new InMemoryPlayerRepository(unitOfWork));
        Replace<ISessionRepository>(services, new InMemorySessionRepository(unitOfWork));
        Replace<IAccessAttemptStore>(services, new InMemoryAccessAttemptStore(unitOfWork));
        Replace<IPendingGoogleAuthStore>(services, new InMemoryPendingGoogleAuthStore(clock, unitOfWork));
        Replace<IAvatarStorage>(services, new InMemoryAvatarStorage(unitOfWork));
        Replace<IFormatCatalog>(services, new InMemoryFormatCatalog(Formats()));
        Replace<IAvatarPresetCatalog>(services, new InMemoryAvatarPresetCatalog(Presets()));
        Replace<IPlayerDecksPurger>(services, new RecordingPlayerDecksPurger(unitOfWork));

        // Sem espera real: o relogio falso nao avanca sozinho; o atraso progressivo so e registrado.
        Replace<IDelayer>(services, new RecordingDelayer());

        // Notificacao sincrona com registro do link; sem SMTP e sem o worker da fila.
        Replace<IPasswordResetNotifier>(services, notifier);
        services.RemoveAll<IPasswordResetSender>();
        RemoveHostedService<PasswordResetEmailWorker>(services);
        return services;
    }

    private static void Replace<T>(IServiceCollection services, T instance)
        where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton(instance);
    }

    private static void RemoveHostedService<T>(IServiceCollection services)
    {
        foreach (var descriptor in services
            .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T))
            .ToList())
        {
            services.Remove(descriptor);
        }
    }

    private static IEnumerable<FormatEntry> Formats() =>
    [
        new(new FormatId("standard"), "Standard", 1, true),
        new(new FormatId("modern"), "Modern", 2, true),
        new(new FormatId("commander"), "Commander", 4, true),
    ];

    private static IEnumerable<AvatarPresetEntry> Presets() =>
        PresetIds.Select((id, index) => new AvatarPresetEntry(
            id, id.Replace('-', ' '), $"/avatars/presets/{id}.svg", index + 1, index == 0, true));
}
