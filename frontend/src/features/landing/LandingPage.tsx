import { useEffect, useState, type ReactNode } from "react";

import { AssistantExample, DeckExample, MetaExample, ShopsExample } from "./examples";
import { GamesSection } from "./GamesSection";
import { Hero } from "./Hero";
import { HookSection } from "./HookSection";
import { HOOKS, type HookId } from "./hooks";
import { LandingFooter } from "./LandingFooter";
import { LandingNav } from "./LandingNav";
import { DECK, addOne } from "./mock/deck-mock";
import { PlansSection } from "./PlansSection";
import {
  REDUCED_MOTION_QUERY,
  detectCapabilities,
  shouldRenderStage,
  type StageCapabilities,
} from "./stage/capabilities";
import { Stage } from "./stage/Stage";

import "./LandingPage.css";

export interface LandingPageProps {
  /** So para testes: decide de fora se o palco monta. Sem ele, detecta no navegador. */
  capabilities?: StageCapabilities;
}

/**
 * Landing publica em / (REQ-L10 v6): um palco WebGL unico guiado pelo scroll (floresta a noite, deck box, deck, quatro
 * ganchos, mesa dos cinco jogos, epilogo e o mural de avisos com os planos, REQ-L13). Sem WebGL ou com
 * prefers-reduced-motion: a imagem estatica da clareira, os ganchos em lista, os jogos em grade e os planos numa
 * section. O texto e sempre HTML.
 */
export function LandingPage({ capabilities }: LandingPageProps) {
  const [deck, setDeck] = useState(DECK);
  const [detected, setDetected] = useState<StageCapabilities>(
    () => capabilities ?? detectCapabilities(),
  );
  const [failed, setFailed] = useState(false);

  // prefers-reduced-motion pode mudar com a pagina aberta: ligar desmonta o palco.
  useEffect(() => {
    if (typeof globalThis.matchMedia !== "function") return;
    const query = globalThis.matchMedia(REDUCED_MOTION_QUERY);
    const onChange = () => setDetected((current) => ({ ...current, reducedMotion: query.matches }));
    query.addEventListener("change", onChange);
    return () => query.removeEventListener("change", onChange);
  }, []);

  const live = shouldRenderStage(detected) && !failed;
  const examples: Record<HookId, ReactNode> = {
    "quanto-do-deck": (
      <DeckExample deck={deck} onHave={(name) => setDeck((current) => addOne(current, name))} />
    ),
    meta: <MetaExample />,
    lojas: <ShopsExample deck={deck} />,
    ia: <AssistantExample />,
  };

  return (
    <div className="landing" data-mode={live ? "live" : "static"}>
      <LandingNav />
      <main id="conteudo" tabIndex={-1} className="landing__main">
        <Hero animated={live} />
        {live ? (
          <Stage examples={examples} onFailure={() => setFailed(true)} />
        ) : (
          <>
            <div id="como-funciona" className="hooks-list">
              {HOOKS.map((hook) => (
                <HookSection key={hook.id} hook={hook}>
                  {examples[hook.id]}
                </HookSection>
              ))}
            </div>
            <GamesSection />
            <PlansSection />
          </>
        )}
      </main>
      <LandingFooter />
    </div>
  );
}
