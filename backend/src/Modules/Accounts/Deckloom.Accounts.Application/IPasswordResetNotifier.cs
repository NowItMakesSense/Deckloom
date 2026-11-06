using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public interface IPasswordResetNotifier
{
    Task NotifyAsync(PlayerId playerId, Email email, string resetLink, CancellationToken ct = default);
}
