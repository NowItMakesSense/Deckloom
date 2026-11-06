using System.Reflection;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deckloom.Accounts.Infrastructure;

public class AccountsDbContext : DbContext
{
    public const string SchemaName = "accounts";

    private static readonly PropertyInfo AvatarProperty =
        typeof(Player).GetProperty(nameof(Player.Avatar))!;

    private readonly HashSet<PlayerId> _formatsLoaded = [];

    public AccountsDbContext(DbContextOptions<AccountsDbContext> options)
        : base(options)
    {
        ChangeTracker.Tracked += RestoreAvatar;
    }

    public DbSet<Player> Players => Set<Player>();

    // Jogadores cujo Formats veio do banco (ou foi montado pelo repositorio): so esses sao reconciliados,
    // para nunca apagar linhas de um Jogador rastreado sem os formatos carregados.
    internal bool AreFormatsLoaded(PlayerId id) => _formatsLoaded.Contains(id);

    internal void MarkFormatsLoaded(PlayerId id) => _formatsLoaded.Add(id);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAvatarAndTimestamps();
        await SyncPreferredFormats(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    // Padrao de producao: coluna de sistema xmin do PostgreSQL. Os testes (SQLite) sobrescrevem.
    protected virtual void ConfigureConcurrencyToken(EntityTypeBuilder<Player> player)
    {
        ArgumentNullException.ThrowIfNull(player);

        player.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfiguration(new PlayerEntityConfiguration(ConfigureConcurrencyToken));
        modelBuilder.ApplyConfiguration(new ReferencedFormatConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerPreferredFormatConfiguration());
        modelBuilder.ApplyConfiguration(new SessionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PendingGoogleAuthEntityConfiguration());
        modelBuilder.ApplyConfiguration(new AccessAttemptEntityConfiguration());
        modelBuilder.ApplyConfiguration(new AvatarImageEntityConfiguration());
        modelBuilder.ApplyConfiguration(new AvatarPresetEntityConfiguration());
    }

    // Player.Formats nao e rastreado pelo EF: as linhas de player_preferred_format sao reconciliadas com o conjunto
    // atual, inserindo as novas e removendo so as que sairam (formatos inativos permanecem no conjunto, ADR-0007).
    private async Task SyncPreferredFormats(CancellationToken cancellationToken)
    {
        var players = ChangeTracker.Entries<Player>()
            .Where(e => e.State == EntityState.Added
                || (e.State is EntityState.Modified or EntityState.Unchanged && _formatsLoaded.Contains(e.Entity.Id)))
            .Select(e => (e.Entity.Id, Wanted: e.Entity.Formats.Ids.Select(f => f.Value).ToHashSet(), IsNew: e.State == EntityState.Added))
            .ToList();

        foreach (var (playerId, wanted, isNew) in players)
        {
            var existing = isNew
                ? []
                : await Set<PlayerPreferredFormat>()
                    .Where(r => r.PlayerId == playerId)
                    .ToListAsync(cancellationToken);

            Set<PlayerPreferredFormat>().RemoveRange(existing.Where(r => !wanted.Contains(r.FormatId)));
            var present = existing.Select(r => r.FormatId).ToHashSet();
            Set<PlayerPreferredFormat>().AddRange(
                wanted.Where(f => !present.Contains(f)).Select(f => new PlayerPreferredFormat { PlayerId = playerId, FormatId = f }));
        }
    }

    private static void StampAvatar(EntityEntry<Player> entry)
    {
        var (kind, presetId, customKey) = entry.Entity.Avatar switch
        {
            PresetAvatar preset => (PlayerEntityConfiguration.PresetKind, preset.PresetId, (string?)null),
            CustomAvatar custom => (PlayerEntityConfiguration.CustomKind, (string?)null, custom.StorageKey),
            _ => throw new InvalidOperationException("Unknown avatar kind."),
        };

        entry.Property<string>(PlayerEntityConfiguration.AvatarKindColumn).CurrentValue = kind;
        entry.Property<string?>(PlayerEntityConfiguration.AvatarPresetIdColumn).CurrentValue = presetId;
        entry.Property<string?>(PlayerEntityConfiguration.CustomAvatarKeyColumn).CurrentValue = customKey;
    }

    private static void RestoreAvatar(object? sender, EntityTrackedEventArgs args)
    {
        if (!args.FromQuery || args.Entry.Entity is not Player player)
        {
            return;
        }

        var presetId = args.Entry.Property(PlayerEntityConfiguration.AvatarPresetIdColumn).CurrentValue as string;
        var customKey = args.Entry.Property(PlayerEntityConfiguration.CustomAvatarKeyColumn).CurrentValue as string;

        Avatar avatar = presetId is not null
            ? PresetAvatar.Create(presetId).Value
            : CustomAvatar.Create(customKey).Value;
        AvatarProperty.SetValue(player, avatar);
    }

    private void StampAvatarAndTimestamps()
    {
        ChangeTracker.DetectChanges();
        var now = TimeProvider.System.GetUtcNow();

        foreach (var entry in ChangeTracker.Entries<ExternalIdentity>().Where(e => e.State == EntityState.Added))
        {
            entry.Property(PlayerEntityConfiguration.LinkedAtColumn).CurrentValue = now;
        }

        foreach (var entry in ChangeTracker.Entries<Player>().Where(e => e.State != EntityState.Deleted))
        {
            StampAvatar(entry);
            if (entry.State == EntityState.Added)
            {
                entry.Property(PlayerEntityConfiguration.CreatedAtColumn).CurrentValue = now;
            }

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Property(PlayerEntityConfiguration.UpdatedAtColumn).CurrentValue = now;
            }
        }
    }
}
