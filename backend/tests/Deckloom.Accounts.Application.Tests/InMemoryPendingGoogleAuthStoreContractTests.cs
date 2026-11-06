using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-143: o contrato de IPendingGoogleAuthStore contra o fake em memoria.
public sealed class InMemoryPendingGoogleAuthStoreContractTests : PendingGoogleAuthStoreContract
{
    protected override AccountsContractFixture CreateFixture() => new InMemoryFixture();

    private sealed class InMemoryFixture : AccountsContractFixture
    {
        private readonly InMemoryUnitOfWork _uow = new();
        private readonly InMemoryPlayerRepository _players;
        private readonly InMemoryAvatarStorage _avatars;
        private readonly InMemorySessionRepository _sessions;
        private readonly InMemoryPendingGoogleAuthStore _pendings;

        public InMemoryFixture()
        {
            _players = new InMemoryPlayerRepository(_uow);
            _avatars = new InMemoryAvatarStorage(_uow);
            _sessions = new InMemorySessionRepository(_uow);
            _pendings = new InMemoryPendingGoogleAuthStore(Clock, _uow);
        }

        public override AccountsContractScope CreateScope() =>
            new(_uow, _players, _avatars, () => _uow.IsTransactionOpen, null, _sessions, _pendings);
    }
}
