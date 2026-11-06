using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-140: o contrato de IUnitOfWork contra EfUnitOfWork (SQLite em memoria, sem banco real).
public sealed class EfUnitOfWorkContractTests : UnitOfWorkContract
{
    protected override AccountsContractFixture CreateFixture() => new EfFixture();

    private sealed class EfFixture : AccountsContractFixture
    {
        private readonly SqliteInMemoryFixture _db = new();

        // Cada escopo e um DbContext novo sobre a mesma conexao em memoria: o segundo escopo so ve o que foi gravado.
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
