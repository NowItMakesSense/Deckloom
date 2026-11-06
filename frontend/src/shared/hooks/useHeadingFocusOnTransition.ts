import { useEffect, type RefObject } from "react";
import { useLocation } from "react-router-dom";

/** Chave da localizacao inicial do react-router: carga de pagina, sem navegacao anterior. */
const INITIAL_LOCATION_KEY = "default";

/** true quando a tela foi alcancada por navegacao dentro da SPA; false na carga de pagina. */
export function useIsRouteTransition(): boolean {
  return useLocation().key !== INITIAL_LOCATION_KEY;
}

/**
 * Foca o <h1> so em transicao de rota da SPA (A11Y-34). Na carga de pagina o foco fica no inicio do
 * documento para que o primeiro Tab chegue ao SkipLink (A11Y-23, QA-03).
 */
export function useHeadingFocusOnTransition(
  headingRef: RefObject<HTMLElement | null>,
  enabled = true,
): void {
  const isTransition = useIsRouteTransition();
  useEffect(() => {
    if (enabled && isTransition) headingRef.current?.focus();
  }, [enabled, isTransition, headingRef]);
}
