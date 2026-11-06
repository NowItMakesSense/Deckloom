using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-142: o contrato de ISessionRepository contra EfSessionRepository (SQLite em memoria, sem banco real).
public sealed class EfSessionRepositoryContractTests : SessionRepositoryContract
{
    protected override AccountsContractFixture CreateFixture() => new EfFixture();

    private sealed class EfFixture : AccountsContractFixture
    {
        private readonly SqliteInMemoryFixture _db = new();
        private readonly FakeTimeProvider _clock = new(ContractNow);

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
                new EfPendingGoogleAuthStore(context, _clock));
        }

        protected override ValueTask DisposeCoreAsync()
        {
            _db.Dispose();
            return base.DisposeCoreAsync();
        }
    }
}
