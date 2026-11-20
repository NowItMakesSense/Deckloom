import { useRef } from "react";

import { GAMES } from "./games";
import { useRevealOnEnter } from "./useRevealOnEnter";

import "./GamesSection.css";

export interface GamesListProps {
  /** Marca cada logo para entrar com anime.js (so no fallback, onde a secao rola com a pagina). */
  reveal?: boolean;
}

/** Os logos oficiais locais dos cinco jogos, juntos e na ordem do REQ-L01 (nenhum vem antes dos outros). */
export function GamesList({ reveal = false }: GamesListProps) {
  return (
    <ul className="games-panel__list">
      {GAMES.map((game) => (
        <li
          key={game.id}
          className="games-panel__item"
          data-plate={game.plate}
          data-reveal-item={reveal ? "" : undefined}
        >
          <img
            className="games-panel__logo"
            src={game.src}
            alt={game.name}
            width={game.width}
            height={game.height}
            loading="lazy"
          />
        </li>
      ))}
    </ul>
  );
}

/** Fallback estatico: os cinco jogos em grade, depois dos ganchos em lista (sem WebGL ou com movimento reduzido). */
export function GamesSection() {
  const ref = useRef<HTMLElement>(null);
  useRevealOnEnter(ref);
  return (
    <section ref={ref} id="jogos" className="games-section" aria-labelledby="jogos-titulo">
      <div className="games-panel">
        <h2 id="jogos-titulo" className="games-panel__heading" data-reveal-item>
          Os cinco jogos
        </h2>
        <p className="games-panel__lead" data-reveal-item>
          Magic: The Gathering, Pokémon TCG, Yu-Gi-Oh!, One Piece Card Game e Disney Lorcana entram
          juntos.
        </p>
        <GamesList reveal />
      </div>
    </section>
  );
}
