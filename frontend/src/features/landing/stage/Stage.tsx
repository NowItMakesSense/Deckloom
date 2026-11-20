import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";

import { UserPlusIcon } from "@/design-system/icons";

import { GamesList } from "../GamesSection";
import { HookCard } from "../HookCard";
import { HOOKS, type HookId } from "../hooks";
import { isNarrowViewport } from "./capabilities";
import { loadStageAssets } from "./loadAssets";
import { applyOverlayFrame, overlayFrame, type OverlayElements } from "./overlays";
import { StaticClearing } from "./StaticClearing";

import "./Stage.css";

export interface StageProps {
  /** Exemplos de cada gancho (HTML real, o mesmo do fallback). */
  examples: Record<HookId, ReactNode>;
  /** O palco nao pode continuar (WebGL falhou ao montar): a pagina troca para o fallback estatico. */
  onFailure: () => void;
}

function collectElements(root: HTMLElement): OverlayElements {
  const one = (name: string) =>
    root.querySelector<HTMLElement>(`[data-overlay="${name}"]`) as HTMLElement;
  return {
    hint: one("hint"),
    intro: one("intro"),
    progress: one("progress"),
    gamesTitle: one("games-title"),
    gamesCap: one("games-cap"),
    boardCta: one("board-cta"),
    hooks: HOOKS.map((_, k) => {
      const panel = root.querySelector<HTMLElement>(`[data-hook-panel="${k}"]`) as HTMLElement;
      return {
        panel,
        items: [...panel.querySelectorAll<HTMLElement>("[data-reveal-item]")],
        bar: root.querySelector<HTMLElement>(`[data-hook-bar="${k}"]`) as HTMLElement,
      };
    }),
  };
}

/**
 * O palco WebGL unico (REQ-L10): uma secao alta cujo scroll guia a camera, com um quadro preso (sticky) onde o
 * canvas desenha a floresta a noite, a deck box, o deck, os ganchos, a mesa e, no fim, o mural de avisos com os planos
 * (REQ-L13): os botoes dos planos e a lista para leitor de tela sao HTML. Por cima, o texto todo em HTML. O three so e
 * carregado aqui, por import dinamico (REQ-L12), depois que a pagina decidiu que o palco vale a pena.
 */
export function Stage({ examples, onFailure }: StageProps) {
  const scrollerRef = useRef<HTMLElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [ready, setReady] = useState(false);
  const failure = useRef(onFailure);
  useEffect(() => {
    failure.current = onFailure;
  });

  useEffect(() => {
    const container = containerRef.current;
    const scroller = scrollerRef.current;
    if (!container || !scroller) return;
    const elements = collectElements(container);
    applyOverlayFrame(elements, overlayFrame(0));
    let cancelled = false;
    let dispose: (() => void) | undefined;

    Promise.all([import("./engine/createStage"), loadStageAssets()])
      .then(([{ createStage }, assets]) => {
        if (cancelled) return;
        const handle = createStage({
          container,
          scroller,
          mobile: isNarrowViewport(globalThis.innerWidth, globalThis.innerHeight),
          logos: assets.logos,
          models: assets.models,
          hooks: HOOKS.map((hook) => ({
            title: hook.title,
            type: `Recurso — ${hook.features.join(", ")}`,
            text: hook.text,
          })),
          onFrame: (raw) => applyOverlayFrame(elements, overlayFrame(raw)),
        });
        dispose = () => handle.dispose();
        setReady(true);
      })
      .catch(() => {
        if (!cancelled) failure.current();
      });

    return () => {
      cancelled = true;
      dispose?.();
    };
  }, []);

  return (
    <section
      ref={scrollerRef}
      id="como-funciona"
      className="stage"
      aria-label="A deck box, o baralho do Deckloom e os cinco jogos"
    >
      <div
        ref={containerRef}
        className="stage__frame"
        data-testid="stage"
        data-ready={ready ? "true" : "false"}
      >
        <StaticClearing className="stage__poster" />
        <div data-overlay="hint" className="stage__hint" aria-hidden="true">
          Role para entrar na floresta
        </div>
        <div data-overlay="intro" className="stage__panel stage__intro">
          <span className="stage__lead-title">O baralho do Deckloom</span>
          <p>Quatro cartas, quatro coisas que o Deckloom faz pela sua coleção.</p>
        </div>
        {HOOKS.map((hook, k) => (
          <div key={hook.id} className="stage__cap" data-hook-panel={k}>
            <HookCard hook={hook}>{examples[hook.id]}</HookCard>
          </div>
        ))}
        <div data-overlay="progress" className="stage__progress" aria-hidden="true">
          {HOOKS.map((hook, k) => (
            <i key={hook.id}>
              <b data-hook-bar={k} />
            </i>
          ))}
        </div>
        <div data-overlay="games-title" className="stage__games-title">
          <h2>Os cinco jogos</h2>
        </div>
        <div data-overlay="games-cap" className="stage__panel stage__games">
          <h3>Uma conta para todos os seus jogos</h3>
          <p>Cada coleção fica separada, e nenhum jogo vem antes do outro.</p>
          <GamesList />
        </div>
        <div
          data-overlay="board-cta"
          className="stage__board-cta"
          role="group"
          aria-labelledby="planos-palco-titulo"
          inert
        >
          <div className="stage__board-actions">
            <Link className="landing-cta" to="/cadastro">
              <UserPlusIcon size={18} />
              Criar conta grátis
            </Link>
            <Link className="stage__board-alt" to="/cadastro">
              Quero o Arcano
            </Link>
          </div>
          <p>Os dois planos começam pelo cadastro grátis. A assinatura ainda não é cobrada.</p>
        </div>
      </div>
      {/* Fim do palco: alvo do link "Planos" (o mural) e a lista dos planos em HTML para leitor de tela. */}
      <div id="planos" className="stage__plans">
        <div className="landing-sr-only">
          <h2 id="planos-palco-titulo">Planos, no mural de avisos da clareira</h2>
          <ul>
            <li>Plano Grátis, R$ 0: até 5 decks salvos, montados com a sua coleção.</li>
            <li>
              Plano Arcano, R$ 19,90 por mês: sugestões com IA, decks ilimitados, alertas de preço e
              sem anúncios.
            </li>
          </ul>
        </div>
      </div>
    </section>
  );
}
