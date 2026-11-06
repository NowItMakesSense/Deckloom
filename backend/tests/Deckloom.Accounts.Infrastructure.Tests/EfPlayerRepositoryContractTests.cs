using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-141: o contrato de IPlayerRepository contra EfPlayerRepository (SQLite em memoria, sem banco real).
public sealed class EfPlayerRepositoryContractTests : PlayerRepositoryContract
{
    protected override AccountsContractFixture CreateFixture() => new EfFixture();

    private sealed class EfFixture : AccountsContractFixture
    {
        private readonly SqliteInMemoryFixture _db = new();
        private readonly FakeTimeProvider _clock = new(ContractNow);

        // A tabela format pertence ao deck_suggestion (ADR-0001); aqui so o alvo da FK, com os ids que o contrato usa.
        public EfFixture() => _db.SeedFormats("commander", "modern", "pauper", "oldformat");

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
