/**
 * Abertura da landing. No palco animado o titulo principal existe em HTML mas fica oculto visualmente (nao ha
 * painel de titulo sobre a floresta); no fallback estatico (sem WebGL ou com prefers-reduced-motion) ele aparece
 * num painel de vidro sobre a imagem da clareira. Sem cartao de criar conta: Criar conta e Entrar ficam na navegacao.
 */
import { LANDING_HEADING } from "./copy";
import { StaticClearing } from "./stage/StaticClearing";

import "./Hero.css";

const LEAD =
  "Registre suas cartas e veja, deck a deck, quanto você já tem e o que ainda falta. Para Magic: The Gathering, Pokémon TCG, Yu-Gi-Oh!, One Piece e Lorcana, todos no mesmo lugar.";

export function Hero({ animated }: { animated: boolean }) {
  if (animated) {
    return (
      <div id="hero" className="landing-hero--hidden">
        <h1 id="hero-titulo" className="landing-sr-only">
          {LANDING_HEADING}
        </h1>
        <p className="landing-sr-only">{LEAD}</p>
      </div>
    );
  }
  return (
    <section id="hero" className="landing-hero" aria-labelledby="hero-titulo">
      <StaticClearing className="landing-hero__art" />
      <div className="landing-hero__panel">
        <h1 id="hero-titulo" className="landing-hero__title">
          {LANDING_HEADING}
        </h1>
        <p className="landing-hero__lead">{LEAD}</p>
      </div>
    </section>
  );
}
