// Helpers de movimento sobre anime.js (REQ-L09). Sempre com fallback para prefers-reduced-motion:
// reduzido, nada desloca nem escala e o conteudo fica visivel e parado.
import { animate, cubicBezier, stagger } from "animejs";

const REDUCED_QUERY = "(prefers-reduced-motion: reduce)";

/** True com reducao de movimento. Sem matchMedia, assume reduzido: o conteudo fica parado e visivel. */
export function prefersReducedMotion(): boolean {
  if (typeof globalThis.matchMedia !== "function") return true;
  return globalThis.matchMedia(REDUCED_QUERY).matches;
}

export interface RevealOptions {
  /** Distancia inicial da subida, em px (design-system 1.7: ate 24). */
  distance?: number;
  /** Intervalo entre elementos, em ms (design-system 1.7: 80). */
  stagger?: number;
  /** Duracao de cada elemento, em ms. */
  duration?: number;
}

/** Entrada por secao: os elementos sobem ate `distance` com opacity, escalonados, uma vez. */
export function revealOnEnter(targets: HTMLElement[], options: RevealOptions = {}): void {
  if (prefersReducedMotion()) {
    for (const el of targets) {
      el.style.opacity = "1";
      el.style.transform = "none";
    }
    return;
  }
  const { distance = 24, stagger: gap = 80, duration = 600 } = options;
  animate(targets, {
    translateY: [distance, 0],
    opacity: [0, 1],
    delay: stagger(gap),
    duration,
    ease: cubicBezier(0.23, 1, 0.32, 1),
  });
}

/** Entrada do painel de uma tela: sobe 16px so com transform (sem opacity, o contraste nunca cai durante a entrada). */
export function riseOnEnter(target: HTMLElement): void {
  if (prefersReducedMotion()) return;
  animate(target, {
    translateY: [16, 0],
    duration: 500,
    ease: cubicBezier(0.23, 1, 0.32, 1),
    // O transform final fica inline e viraria o bloco de contencao do Dialog (position: fixed) dentro do painel.
    onComplete: () => target.style.removeProperty("transform"),
  });
}
