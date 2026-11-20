// Animacao da secao de planos com anime.js (REQ-L12, REQ-L09): a linha do tempo anda com o scroll; os cartoes
// entram em perspectiva, o filete dourado do plano pago se desenha, o preco conta ate R$ 19,90 e a lista aparece.
// Com prefers-reduced-motion nada e escondido nem movido.
import { createTimeline, onScroll, stagger, utils } from "animejs";

import { prefersReducedMotion } from "@/design-system/motion";

const PAID_PRICE = 19.9;

function brl(value: number): string {
  return `R$ ${value.toFixed(2).replace(".", ",")}`;
}

/** Liga a animacao a `section`; devolve a funcao que a desfaz e restaura o estado final. */
export function animatePlans(section: HTMLElement): () => void {
  if (prefersReducedMotion()) return () => {};
  const price = section.querySelector<HTMLElement>("[data-price]");
  const shells = ".plans__shell";
  const header = ".plans__header > *";
  const items = ".plan li";
  const trace = ".plan__trace";
  const priceState = { v: 0 };

  utils.set(shells, { opacity: 0, y: 90, rotateX: 18, z: -140 });
  utils.set(`${header}, ${items}`, { opacity: 0 });
  utils.set(trace, { strokeDashoffset: 1 });
  if (price) price.textContent = brl(0);

  const timeline = createTimeline({
    autoplay: onScroll({
      target: section,
      enter: { target: "top", container: "bottom" },
      leave: { target: "top", container: "top" },
      sync: 0.35,
    }),
    defaults: { ease: "outCubic" },
  });
  timeline
    .add(header, { opacity: [0, 1], y: [24, 0], duration: 300, delay: stagger(60) }, 0)
    .add(shells, { opacity: 1, y: 0, rotateX: 0, z: 0, duration: 520, delay: stagger(130) }, 140)
    .add(trace, { strokeDashoffset: 0, duration: 520, ease: "inOutSine" }, 520)
    .add(
      priceState,
      {
        v: PAID_PRICE,
        duration: 460,
        ease: "outQuad",
        onUpdate: () => {
          if (price) price.textContent = brl(priceState.v);
        },
      },
      520,
    )
    .add(items, { opacity: 1, x: [-12, 0], duration: 220, delay: stagger(50) }, 620);

  return () => {
    timeline.revert();
    if (price) price.textContent = brl(PAID_PRICE);
  };
}

/** Inclina o cartao em 3D seguindo o ponteiro (so com mouse; nunca com movimento reduzido). */
export function tiltOnPointer(section: HTMLElement): () => void {
  const fine = globalThis.matchMedia?.("(hover: hover) and (pointer: fine)").matches ?? false;
  if (!fine || prefersReducedMotion()) return () => {};
  const cleanups: Array<() => void> = [];
  section.querySelectorAll<HTMLElement>(".plans__shell").forEach((shell) => {
    const card = shell.firstElementChild as HTMLElement | null;
    if (!card) return;
    const move = (event: PointerEvent) => {
      const r = shell.getBoundingClientRect();
      const mx = (event.clientX - r.left) / r.width - 0.5;
      const my = (event.clientY - r.top) / r.height - 0.5;
      card.style.transform = `rotateY(${(mx * 8).toFixed(2)}deg) rotateX(${(-my * 6).toFixed(2)}deg) translateZ(6px)`;
    };
    const leave = () => {
      card.style.transform = "";
    };
    shell.addEventListener("pointermove", move);
    shell.addEventListener("pointerleave", leave);
    cleanups.push(() => {
      shell.removeEventListener("pointermove", move);
      shell.removeEventListener("pointerleave", leave);
      leave();
    });
  });
  return () => cleanups.forEach((cleanup) => cleanup());
}
