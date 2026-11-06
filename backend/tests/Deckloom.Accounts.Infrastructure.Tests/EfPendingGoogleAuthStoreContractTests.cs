using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-143: o contrato de IPendingGoogleAuthStore contra EfPendingGoogleAuthStore (SQLite em memoria, sem banco real).
public sealed class EfPendingGoogleAuthStoreContractTests : PendingGoogleAuthStoreContract
{
    protected override AccountsContractFixture CreateFixture() => new EfFixture();

    private sealed class EfFixture : AccountsContractFixture
    {
        private readonly SqliteInMemoryFixture _db = new();

        public override AccountsContractScope CreateScope()
        {
            var context = _db.CreateContext();
            return new AccountsContractScope(
                new EfUnitOfWork(context),
                new EfPlayerRepository(context),
                new PostgresAvatarStorage(context),
                () => context.Database.CurrentTransaction is not null,
                context.DisposeAsync,
                new EfSessionRepository(context),
                new EfPendingGoogleAuthStore(context, Clock));
        }

        protected override ValueTask DisposeCoreAsync()
        {
            _db.Dispose();
            return base.DisposeCoreAsync();
        }
    }
}
