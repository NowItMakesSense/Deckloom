using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public interface IPasswordHasher
{
    ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default);

    ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default);

    bool NeedsRehash(PasswordHash hash);
}
