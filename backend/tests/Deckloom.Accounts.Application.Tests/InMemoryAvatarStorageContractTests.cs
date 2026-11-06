using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-146: o contrato de IAvatarStorage contra o fake em memoria.
public sealed class InMemoryAvatarStorageContractTests : AvatarStorageContract
{
    protected override AccountsContractFixture CreateFixture() => new InMemoryFixture();

    private sealed class InMemoryFixture : AccountsContractFixture
    {
        private readonly InMemoryPlayerRepository _players;
        private readonly InMemoryAvatarStorage _avatars;

        public InMemoryFixture()
        {
            // Estado confirmado compartilhado: o primeiro par e o "banco"; cada escopo recebe UoW propria.
            var uow = new InMemoryUnitOfWork();
            _players = new InMemoryPlayerRepository(uow);
            _avatars = new InMemoryAvatarStorage(uow);
        }

        public override AccountsContractScope CreateScope()
        {
            var uow = new InMemoryUnitOfWork();
            return new(uow, new InMemoryPlayerRepository(uow, _players), new InMemoryAvatarStorage(uow, _avatars),
                () => uow.IsTransactionOpen);
        }
    }
}
