using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// Decide entre o hash real e o ficticio e faz exatamente uma verificacao, para que o tempo de resposta
// nao revele se a conta existe ou tem senha local (REQ-005, T3).
internal sealed class CredentialVerifier(IPasswordHasher passwordHasher)
{
    // Parametros no nivel corrente; nao corresponde a nenhuma senha real.
    private static readonly PasswordHash DummyHash = PasswordHash.Parse(
        "v1$pbkdf2-sha256$600000$ZHVtbXktc2FsdC1mb3ItdGltaW5n$ZHVtbXktaGFzaC1mb3ItdGltaW5nLW5ldmVyLW1hdGNoZXM=").Value;

    public async Task<bool> VerifyAsync(Password password, PasswordHash? storedHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        var matches = await passwordHasher.VerifyAsync(password, storedHash ?? DummyHash, ct);
        return matches && storedHash is not null;
    }
}
