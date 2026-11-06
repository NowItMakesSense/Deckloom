using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure;

// No-op documentado (REQ-025; ADR-0006): ainda nao existem decks. Pendencia: a feature 9 (Decks)
// substitui este adapter por um que apague os decks do jogador na transacao do chamador.
public sealed class NoDecksYetPurger : IPlayerDecksPurger
{
    public Task PurgeAsync(PlayerId playerId, CancellationToken ct = default) => Task.CompletedTask;
}
