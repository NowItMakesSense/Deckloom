// A camada HTML do palco (REQ-L10, REQ-L11): dica de rolar, abertura do baralho, legendas dos quatro ganchos,
// barras de progresso, os jogos e os botoes do mural de avisos. Tudo funcao pura do progresso `raw` (0 a END);
// `applyOverlayFrame` so escreve no DOM.
import { clamp, smooth } from "./math";
import { PH, SEGL } from "./phases";

/** Partes de cada legenda que entram escalonadas (faixa de titulo, arte, tipo, texto). */
export const HOOK_PARTS = 4;
const HOOK_COUNT = 4;

export interface Fade {
  readonly opacity: number;
  /** Deslocamento vertical em px. */
  readonly dy: number;
}

export interface HookFrame {
  readonly opacity: number;
  /** Preenchimento da barra de progresso do gancho (0 a 1). */
  readonly bar: number;
  readonly items: readonly Fade[];
}

export interface OverlayFrame {
  readonly hint: Fade;
  readonly intro: Fade;
  readonly progress: Fade;
  readonly hooks: readonly HookFrame[];
  readonly gamesTitle: Fade;
  readonly gamesCap: Fade;
  /** Botoes dos planos sob o mural de avisos, no fim do pan. */
  readonly boardCta: Fade;
}

export function overlayFrame(raw: number): OverlayFrame {
  const introIn = smooth((raw - (PH.rise + 0.008)) / 0.015);
  const hooks: HookFrame[] = Array.from({ length: HOOK_COUNT }, (_, k) => {
    const u = (raw - PH.story0 - k * SEGL) / SEGL;
    const out = smooth((u - 0.86) / 0.07);
    return {
      opacity: smooth((u - 0.55) / 0.05) * (1 - out),
      bar: clamp(u, 0, 1),
      items: Array.from({ length: HOOK_PARTS }, (_, j) => {
        const inn = smooth((u - 0.57 - j * 0.03) / 0.08);
        return { opacity: inn * (1 - out), dy: 18 * (1 - inn) - 18 * out };
      }),
    };
  });
  const cap = smooth((raw - 0.955) / 0.03);
  // o titulo e a legenda dos jogos saem quando a camera deixa o portal rumo ao mural
  const leave = 1 - smooth((raw - 1.0) / 0.012);
  const board = smooth((raw - 1.05) / 0.02);
  return {
    hint: { opacity: 1 - smooth(raw / 0.012), dy: 0 },
    intro: {
      opacity: introIn * (1 - smooth((raw - (PH.intro - 0.012)) / 0.01)),
      dy: 10 * (1 - introIn),
    },
    progress: {
      opacity: smooth((raw - PH.story0) / 0.01) * (1 - smooth((raw - PH.story1) / 0.01)),
      dy: 0,
    },
    hooks,
    gamesTitle: { opacity: smooth((raw - (PH.regroup + 0.03)) / 0.02) * leave, dy: 0 },
    gamesCap: { opacity: cap * leave, dy: 16 * (1 - cap) },
    boardCta: { opacity: board, dy: 14 * (1 - board) },
  };
}

export interface HookElements {
  readonly panel: HTMLElement;
  readonly items: readonly HTMLElement[];
  readonly bar: HTMLElement;
}

export interface OverlayElements {
  readonly hint: HTMLElement;
  readonly intro: HTMLElement;
  readonly progress: HTMLElement;
  readonly hooks: readonly HookElements[];
  readonly gamesTitle: HTMLElement;
  readonly gamesCap: HTMLElement;
  readonly boardCta: HTMLElement;
}

/** Abaixo disto o painel e tratado como invisivel: sai da ordem de foco e da leitura (inert). */
const VISIBLE_OPACITY = 0.05;

function write(el: HTMLElement, fade: Fade): void {
  el.style.opacity = fade.opacity.toFixed(3);
  el.style.translate = `0 ${fade.dy.toFixed(1)}px`;
}

export function applyOverlayFrame(els: OverlayElements, frame: OverlayFrame): void {
  write(els.hint, frame.hint);
  write(els.intro, frame.intro);
  els.progress.style.opacity = frame.progress.opacity.toFixed(3);
  frame.hooks.forEach((hook, k) => {
    const target = els.hooks[k];
    if (!target) return;
    target.panel.style.opacity = hook.opacity.toFixed(3);
    target.panel.toggleAttribute("inert", hook.opacity < VISIBLE_OPACITY);
    target.bar.style.transform = `scaleX(${hook.bar.toFixed(3)})`;
    hook.items.forEach((fade, j) => {
      const item = target.items[j];
      if (item) write(item, fade);
    });
  });
  write(els.gamesTitle, frame.gamesTitle);
  write(els.gamesCap, frame.gamesCap);
  els.gamesCap.toggleAttribute("inert", frame.gamesCap.opacity < VISIBLE_OPACITY);
  write(els.boardCta, frame.boardCta);
  // escondido de verdade ate o pan terminar: fora da ordem de foco e da leitura
  els.boardCta.style.visibility = frame.boardCta.opacity > 0.02 ? "visible" : "hidden";
  els.boardCta.toggleAttribute("inert", frame.boardCta.opacity < VISIBLE_OPACITY);
}
