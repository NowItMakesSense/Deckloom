using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Deckloom.DeckSuggestion.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Deckloom.Api.Composition;

// Raiz de composicao do modulo Accounts: portas -> adapters de producao e opcoes tipadas validadas no start.
// Nada aqui abre conexao com o banco; a cadeia de conexao so e lida quando um contexto e criado.
public static class AccountsModuleExtensions
{
    private const int PasswordResetQueueCapacity = 1000;

    private const string ConnectionStringsSection = "ConnectionStrings";

    private const string ProgressiveDelaySection = "Auth:ProgressiveDelay";

    public static IServiceCollection AddAccountsModule(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        AddOptions(services, configuration, environment);
        AddPersistence(services);
        AddSecurityAdapters(services, configuration);
        AddExternalAdapters(services);
        AddUseCases(services);
        AddBackgroundWork(services);
        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // SEC-06: transporte sem TLS e URLs de identidade em http so valem em Development.
        AddValidated<AuthOptions>(
            services, configuration.GetSection(AuthOptions.SectionName), new AuthOptionsValidator(environment.IsDevelopment()));
        AddValidated<LegalOptions>(
            services, configuration.GetSection(LegalOptions.SectionName), new LegalOptionsValidator());
        AddValidated<EmailOptions>(
            services, configuration.GetSection(EmailOptions.SectionName), new EmailOptionsValidator(environment.IsDevelopment()));
        AddValidated<AvatarOptions>(
            services, configuration.GetSection(AvatarOptions.SectionName), new AvatarOptionsValidator());
        AddValidated<ProgressiveDelayOptions>(
            services, configuration.GetSection(ProgressiveDelaySection), new ProgressiveDelayOptionsValidator());

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(ConnectionStringsSection)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>>(
            new DatabaseOptionsValidator(configuration, environment.IsDevelopment()));
    }

    private static void AddValidated<TOptions>(
        IServiceCollection services, IConfiguration section, IValidateOptions<TOptions> validator)
        where TOptions : class
    {
        services.AddOptions<TOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton(validator);
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContextFactory<AccountsDbContext>((sp, builder) =>
            builder.UseNpgsql(
                ConnectionString(sp),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AccountsDbContext.SchemaName)));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AccountsDbContext>>().CreateDbContext());
        services.AddDbContext<FormatCatalogDbContext>((sp, builder) =>
            builder.UseNpgsql(
                ConnectionString(sp),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", FormatCatalogDbContext.SchemaName)));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IPlayerRepository, EfPlayerRepository>();
        services.AddScoped<ISessionRepository, EfSessionRepository>();
        services.AddScoped<IAccessAttemptStore, EfAccessAttemptStore>();
        services.AddScoped<IPendingGoogleAuthStore, EfPendingGoogleAuthStore>();
        services.AddScoped<IFormatCatalog, EfFormatCatalog>();
        services.AddSingleton<IAvatarPresetCatalog, EfAvatarPresetCatalog>();
        services.AddScoped<IAvatarStorage>(sp => new PostgresAvatarStorage(
            sp.GetRequiredService<AccountsDbContext>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<AvatarOptions>>().Value.MaxBytes));
        services.AddSingleton<IPlayerDecksPurger, NoDecksYetPurger>();

        // DB7/UA2: so existe com o adapter PostgreSQL; fabricas de teste e host E2E removem o inspetor.
        services.AddSingleton<IDatabaseRoleInspector, NpgsqlDatabaseRoleInspector>();
        services.AddHostedService<DatabaseRoleStartupCheck>();
    }

    private static string ConnectionString(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Deckloom;

    private static void AddSecurityAdapters(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(
            configuration.GetSection(PasswordHashingOptions.SectionName).Get<PasswordHashingOptions>()
            ?? new PasswordHashingOptions());
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IKeyedHasher>(sp => new HmacKeyedHasher(new RateLimitHmacOptions
        {
            RateLimitHmacKey = sp.GetRequiredService<IOptions<AuthOptions>>().Value.RateLimitHmacKey,
        }));
        services.AddSingleton<ITokenHasher, Sha256TokenHasher>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IDelayer, TimeProviderDelayer>();
        // Auth:ProgressiveDelay:* (limiares, atrasos, orcamento por conta). A ProgressiveDelayOptionsValidator
        // recusa valores invalidos no start, entao a construcao da politica nao falha depois.
        services.AddSingleton(sp => new ProgressiveDelayPolicy(
            sp.GetRequiredService<IOptions<ProgressiveDelayOptions>>().Value));
    }

    private static void AddExternalAdapters(IServiceCollection services)
    {
        services.AddSingleton<ITermsVersionProvider>(sp =>
        {
            var legal = sp.GetRequiredService<IOptions<LegalOptions>>().Value;
            return new ConfiguredTermsVersionProvider(legal.TermsVersion, new Uri(legal.TermsUrl), new Uri(legal.PrivacyUrl));
        });
        services.AddHttpClient<GoogleOidcClient>()
            .AddTypedClient((http, sp) =>
            {
                var auth = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
                var options = new GoogleOidcOptions
                {
                    Authority = new Uri(auth.Google.Authority),
                    ClientId = auth.Google.ClientId,
                    ClientSecret = auth.Google.ClientSecret,
                    PublicOrigin = auth.PublicOrigin,
                };
                return new GoogleOidcClient(
                    http, options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<GoogleOidcClient>>());
            });
        services.AddSingleton<IGoogleIdentityVerifier, GoogleOidcIdentityVerifier>();

        services.AddSingleton<IAvatarImageProcessor>(sp =>
        {
            var avatar = sp.GetRequiredService<IOptions<AvatarOptions>>().Value;
            return new SkiaAvatarImageProcessor(avatar.MaxDimension, avatar.MaxPixels);
        });

        services.AddSingleton(sp => new ChannelPasswordResetNotifier(
            PasswordResetQueueCapacity, sp.GetRequiredService<ILogger<ChannelPasswordResetNotifier>>()));
        services.AddSingleton<IPasswordResetNotifier>(sp => sp.GetRequiredService<ChannelPasswordResetNotifier>());
        services.AddSingleton<IPasswordResetSender>(sp =>
        {
            var email = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
            return new SmtpPasswordResetSender(
                new MailKitSmtpTransport(email.Smtp.Host, email.Smtp.Port, email.Smtp.User, email.Smtp.Password),
                new SmtpEmailOptions { From = email.From, UseTls = email.Smtp.UseTls },
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<SmtpPasswordResetSender>>());
        });
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddSingleton<IAccountsEventLog, AccountsEventLog>();
        services.AddScoped(sp => new PasswordResetOptions
        {
            PublicBaseUrl = sp.GetRequiredService<IOptions<AuthOptions>>().Value.PublicOrigin,
        });
        services.AddScoped<AuthenticateSession>();
        services.AddScoped<Logout>();
        services.AddScoped<RegisterWithPassword>();
        services.AddScoped<LoginWithPassword>();
        services.AddScoped<AccessThrottle>();
        services.AddScoped<RequestPasswordReset>();
        services.AddScoped<ResetPassword>();
        services.AddScoped<HandleGoogleCallback>();
        services.AddScoped<CompleteGoogleSignup>();
        services.AddScoped<LinkGoogleWithPassword>();
        services.AddScoped<GetPendingGoogleAuth>();
        services.AddScoped<CancelPendingGoogleAuth>();
        services.AddScoped<GetMyProfile>();
        services.AddScoped<UpdateMyProfile>();
        services.AddScoped<ChooseAvatarPreset>();
        services.AddScoped<UploadCustomAvatar>();
        services.AddScoped<GetMyCustomAvatar>();
        services.AddScoped<DeleteMyAccount>();
        // T-138: lote padrao 1000 (valor de engenharia). A leitura de Cleanup:BatchSize fica pendente.
        services.AddScoped(sp => new PurgeExpiredData(
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<IPlayerRepository>(),
            sp.GetRequiredService<IPendingGoogleAuthStore>(),
            sp.GetRequiredService<IAccessAttemptStore>(),
            sp.GetRequiredService<TimeProvider>(),
            batchSize: 1000));
    }

    private static void AddBackgroundWork(IServiceCollection services)
    {
        services.AddHostedService<PasswordResetEmailWorker>();
        services.AddHostedService(sp => new ExpiredDataCleanupService(
            async ct =>
            {
                await using var scope = sp.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<PurgeExpiredData>().ExecuteAsync(ct);
            },
            sp.GetRequiredService<TimeProvider>(),
            interval: null,
            logger: sp.GetRequiredService<ILogger<ExpiredDataCleanupService>>()));
    }
}
