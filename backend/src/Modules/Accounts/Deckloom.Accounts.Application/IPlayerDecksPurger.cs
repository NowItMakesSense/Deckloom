using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public interface IPlayerDecksPurger
{
    Task PurgeAsync(PlayerId playerId, CancellationToken ct = default);
}
