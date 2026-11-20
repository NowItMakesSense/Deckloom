import { useLayoutEffect, type RefObject } from "react";

import { prefersReducedMotion, revealOnEnter } from "@/design-system/motion";

/** Marca as partes de uma secao que sobem com opacity quando ela entra na janela. */
export const REVEAL_ITEM_ATTRIBUTE = "data-reveal-item";

const ENTER_THRESHOLD = 0.12;

/**
 * Entrada por secao (docs/design-system.md 1.7, REQ-L09): as partes marcadas com `data-reveal-item` ficam
 * escondidas (so por script) ate a secao entrar na janela e entao sobem com opacity, escalonadas, uma vez, com
 * anime.js. Sem IntersectionObserver ou com prefers-reduced-motion nada e escondido: o conteudo ja esta visivel e parado.
 */
export function useRevealOnEnter(sectionRef: RefObject<HTMLElement | null>): void {
  useLayoutEffect(() => {
    const section = sectionRef.current;
    if (!section || prefersReducedMotion() || typeof IntersectionObserver === "undefined") return;
    const targets = [...section.querySelectorAll<HTMLElement>(`[${REVEAL_ITEM_ATTRIBUTE}]`)];
    if (targets.length === 0) return;
    for (const target of targets) target.style.opacity = "0";

    const observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((entry) => entry.isIntersecting)) return;
        observer.disconnect();
        revealOnEnter(targets);
      },
      { threshold: ENTER_THRESHOLD },
    );
    observer.observe(section);
    return () => {
      observer.disconnect();
      for (const target of targets) target.style.opacity = "";
    };
  }, [sectionRef]);
}
