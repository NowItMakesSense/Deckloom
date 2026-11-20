import { useRef, type ReactNode } from "react";

import { HookCard } from "./HookCard";
import type { HookData } from "./hooks";
import { useRevealOnEnter } from "./useRevealOnEnter";

/** Um gancho como secao da pagina, no fallback estatico: o card entra com anime.js ao aparecer na janela. */
export function HookSection({ hook, children }: { hook: HookData; children?: ReactNode }) {
  const ref = useRef<HTMLElement>(null);
  useRevealOnEnter(ref);
  return (
    <section ref={ref} id={hook.id} className="hook-section">
      <HookCard hook={hook}>{children}</HookCard>
    </section>
  );
}
