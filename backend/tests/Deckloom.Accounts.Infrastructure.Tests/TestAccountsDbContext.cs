using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deckloom.Accounts.Infrastructure.Tests;

// SQLite nao tem xmin: o token de concorrencia vira uma propriedade sombra Guid regravada a cada escrita.
internal sealed class TestAccountsDbContext(DbContextOptions<AccountsDbContext> options) : AccountsDbContext(options)
{
    public const string RowVersion = "RowVersion";

    protected override void ConfigureConcurrencyToken(EntityTypeBuilder<Player> player) =>
        player.Property<Guid>(RowVersion).IsConcurrencyToken();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<Player>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property(RowVersion).CurrentValue = Guid.NewGuid();
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
