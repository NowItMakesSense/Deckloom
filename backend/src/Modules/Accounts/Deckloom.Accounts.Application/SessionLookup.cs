using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// Ponto unico que traduz o token bruto apresentado na Session persistida (busca sempre pelo hash).
internal sealed class SessionLookup(ISessionRepository sessions, ITokenHasher tokenHasher)
{
    public async Task<Session?> FindAsync(string? rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        return await sessions.FindByTokenHash(tokenHasher.Hash(rawToken), ct);
    }
}
