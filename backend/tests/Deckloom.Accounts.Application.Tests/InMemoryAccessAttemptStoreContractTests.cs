using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-144: o contrato de IAccessAttemptStore contra o fake em memoria.
public sealed class InMemoryAccessAttemptStoreContractTests : AccessAttemptStoreContract
{
    protected override AccountsContractFixture CreateFixture() => new InMemoryFixture();

    private sealed class InMemoryFixture : AccountsContractFixture
    {
        private readonly InMemoryUnitOfWork _uow = new();
        private readonly InMemoryPlayerRepository _players;
        private readonly InMemoryAvatarStorage _avatars;
        private readonly InMemoryAccessAttemptStore _attempts;

        public InMemoryFixture()
        {
            _players = new InMemoryPlayerRepository(_uow);
            _avatars = new InMemoryAvatarStorage(_uow);
            _attempts = new InMemoryAccessAttemptStore(_uow);
        }

        public override AccountsContractScope CreateScope() =>
            new(_uow, _players, _avatars, () => _uow.IsTransactionOpen, accessAttempts: _attempts);
    }
}
