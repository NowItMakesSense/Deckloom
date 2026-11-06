using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-159 (R-44): consulta compartilhada de validade de sessao dos testes de exclusao de conta. A revogacao em si e provada
// pelo RecordingSessionRepository; aqui so se le o estado final.
internal sealed class SessionProbe(InMemorySessionRepository sessions, ITokenHasher tokenHasher, TimeProvider clock)
{
    // Sessao que DEVE existir: ausente e falha do teste (erro claro), nao "invalida" silenciosa.
    public async Task<bool> IsValidAsync(string token) =>
        (await sessions.FindByTokenHash(tokenHasher.Hash(token))
            ?? throw new InvalidOperationException($"Sessao '{token}' nao existe no repositorio."))
        .IsValid(clock.GetUtcNow());

    // Sessao apos a exclusao: a cascata do repositorio em memoria a apaga, entao ausente conta como nao utilizavel.
    public async Task<bool> IsUsableAsync(string token) =>
        (await sessions.FindByTokenHash(tokenHasher.Hash(token)))?.IsValid(clock.GetUtcNow()) == true;
}
