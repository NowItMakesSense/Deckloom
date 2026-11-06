using Microsoft.EntityFrameworkCore;

namespace Deckloom.DeckSuggestion.Infrastructure;

// Contexto somente leitura da tabela format: sem DbSet publico, sem rastreamento, nunca grava.
public sealed class FormatCatalogDbContext : DbContext
{
    public const string SchemaName = "deck_suggestion";

    public FormatCatalogDbContext(DbContextOptions<FormatCatalogDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    internal IQueryable<FormatRow> Formats => Set<FormatRow>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default) => throw ReadOnly();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.Entity<FormatRow>(format =>
        {
            format.ToTable("format");
            format.HasKey(f => f.Id).HasName("pk_format");
            format.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
            format.Property(f => f.Name).HasColumnName("name").IsRequired();
            format.Property(f => f.IsActive).HasColumnName("is_active");
            format.Property(f => f.SortOrder).HasColumnName("sort_order");
            format.HasData(InitialFormats());
        });
    }

    // Carga inicial (REQ-037): ids estaveis de Database/tables/deck_suggestion.format.md.
    private static FormatRow[] InitialFormats() =>
    [
        new() { Id = "standard", Name = "Standard", IsActive = true, SortOrder = 1 },
        new() { Id = "modern", Name = "Modern", IsActive = true, SortOrder = 2 },
        new() { Id = "pioneer", Name = "Pioneer", IsActive = true, SortOrder = 3 },
        new() { Id = "commander", Name = "Commander", IsActive = true, SortOrder = 4 },
        new() { Id = "pauper", Name = "Pauper", IsActive = true, SortOrder = 5 },
    ];

    private static InvalidOperationException ReadOnly() =>
        new("FormatCatalogDbContext is read-only: the format table is owned by Deck Suggestion.");
}
