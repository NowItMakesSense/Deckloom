using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-140: o contrato de IUnitOfWork contra o fake em memoria.
public sealed class InMemoryUnitOfWorkContractTests : UnitOfWorkContract
{
    protected override AccountsContractFixture CreateFixture() => new InMemoryFixture();

    private sealed class InMemoryFixture : AccountsContractFixture
    {
        private readonly InMemoryUnitOfWork _uow = new();
        private readonly InMemoryPlayerRepository _players;
        private readonly InMemoryAvatarStorage _avatars;

        public InMemoryFixture()
        {
            _players = new InMemoryPlayerRepository(_uow);
            _avatars = new InMemoryAvatarStorage(_uow);
        }

        // O fake guarda o estado confirmado no proprio objeto: todo escopo enxerga o mesmo armazenamento.
        public override AccountsContractScope CreateScope() =>
            new(_uow, _players, _avatars, () => _uow.IsTransactionOpen);
    }
}
