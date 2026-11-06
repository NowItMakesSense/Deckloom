using Microsoft.EntityFrameworkCore;

namespace Deckloom.Catalog.Infrastructure;

// Contexto somente leitura da tabela tcg: sem DbSet publico, sem rastreamento, nunca grava.
public sealed class TcgCatalogDbContext : DbContext
{
    public const string SchemaName = "catalog";

    public TcgCatalogDbContext(DbContextOptions<TcgCatalogDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    internal IQueryable<TcgRow> Tcgs => Set<TcgRow>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default) => throw ReadOnly();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.Entity<TcgRow>(tcg =>
        {
            tcg.ToTable("tcg");
            tcg.HasKey(t => t.Id).HasName("pk_tcg");
            tcg.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            tcg.Property(t => t.Name).HasColumnName("name").IsRequired();
            tcg.Property(t => t.IsActive).HasColumnName("is_active");
            tcg.Property(t => t.SortOrder).HasColumnName("sort_order");
            tcg.HasData(InitialTcgs());
        });
    }

    // Carga inicial (REQ-P02): ids estaveis de catalog.tcg; "mtg" coincide com o FavoriteTcg existente.
    private static TcgRow[] InitialTcgs() =>
    [
        new() { Id = "mtg", Name = "Magic: The Gathering", IsActive = true, SortOrder = 1 },
        new() { Id = "pokemon", Name = "Pokemon TCG", IsActive = true, SortOrder = 2 },
        new() { Id = "yugioh", Name = "Yu-Gi-Oh!", IsActive = true, SortOrder = 3 },
        new() { Id = "onepiece", Name = "One Piece", IsActive = true, SortOrder = 4 },
        new() { Id = "lorcana", Name = "Lorcana", IsActive = true, SortOrder = 5 },
    ];

    private static InvalidOperationException ReadOnly() =>
        new("TcgCatalogDbContext is read-only: the tcg table is owned by Catalog.");
}
