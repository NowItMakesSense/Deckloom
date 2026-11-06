using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-146: o contrato de IAvatarStorage contra PostgresAvatarStorage (SQLite em memoria, sem banco real).
public sealed class EfAvatarStorageContractTests : AvatarStorageContract
{
    protected override AccountsContractFixture CreateFixture() => new EfFixture();

    private sealed class EfFixture : AccountsContractFixture
    {
        private readonly SqliteInMemoryFixture _db = new();

        // A tabela format pertence ao deck_suggestion (ADR-0001); aqui so o alvo da FK ao remover o Jogador.
        public EfFixture() => _db.SeedFormats();

        public override AccountsContractScope CreateScope()
        {
            var context = _db.CreateContext();
            return new AccountsContractScope(
                new EfUnitOfWork(context),
                new EfPlayerRepository(context),
                new PostgresAvatarStorage(context),
                () => context.Database.CurrentTransaction is not null,
                context.DisposeAsync);
        }

        protected override ValueTask DisposeCoreAsync()
        {
            _db.Dispose();
            return base.DisposeCoreAsync();
        }
    }
}
