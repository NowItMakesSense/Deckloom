// T-L02 / REQ-L09: entrada por secao com anime.js (revealOnEnter): as partes sobem com opacity, uma vez, quando a
// secao entra na janela; sem script, sem IntersectionObserver ou com prefers-reduced-motion tudo fica visivel.
import { render } from "@testing-library/react";
import { useRef } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const reveal = vi.hoisted(() => ({ revealOnEnter: vi.fn(), reduced: false }));
vi.mock("@/design-system/motion", () => ({
  revealOnEnter: reveal.revealOnEnter,
  prefersReducedMotion: () => reveal.reduced,
}));

import { useRevealOnEnter } from "./useRevealOnEnter";

class FakeObserver {
  static instances: FakeObserver[] = [];
  observed: Element[] = [];
  disconnect = vi.fn();
  constructor(public callback: IntersectionObserverCallback) {
    FakeObserver.instances.push(this);
  }
  observe(element: Element) {
    this.observed.push(element);
  }
  trigger(isIntersecting: boolean) {
    this.callback(
      [{ isIntersecting, target: this.observed[0] } as IntersectionObserverEntry],
      this as unknown as IntersectionObserver,
    );
  }
}

function Section() {
  const ref = useRef<HTMLElement>(null);
  useRevealOnEnter(ref);
  return (
    <section ref={ref}>
      <h2 data-reveal-item>Titulo</h2>
      <p data-reveal-item>Texto</p>
      <p>Fora da entrada</p>
    </section>
  );
}

beforeEach(() => {
  FakeObserver.instances = [];
  reveal.reduced = false;
  reveal.revealOnEnter.mockClear();
  vi.stubGlobal("IntersectionObserver", FakeObserver);
});
afterEach(() => vi.unstubAllGlobals());

describe("useRevealOnEnter", () => {
  it("esconde as partes marcadas ate a secao entrar na janela e anima uma vez, escalonando", () => {
    const { container } = render(<Section />);
    const [title, text, other] = [...container.querySelectorAll("h2, p")] as HTMLElement[];

    expect(title.style.opacity).toBe("0");
    expect(text.style.opacity).toBe("0");
    expect(other.style.opacity).toBe("");
    expect(reveal.revealOnEnter).not.toHaveBeenCalled();

    const observer = FakeObserver.instances[0];
    observer.trigger(false);
    expect(reveal.revealOnEnter).not.toHaveBeenCalled();

    observer.trigger(true);
    expect(reveal.revealOnEnter).toHaveBeenCalledTimes(1);
    expect(reveal.revealOnEnter).toHaveBeenCalledWith([title, text]);
    expect(observer.disconnect).toHaveBeenCalled();
  });

  it("com prefers-reduced-motion nada e escondido nem observado: o conteudo fica visivel e parado", () => {
    reveal.reduced = true;
    const { container } = render(<Section />);

    expect(FakeObserver.instances).toHaveLength(0);
    for (const el of container.querySelectorAll<HTMLElement>("h2, p"))
      expect(el.style.opacity).toBe("");
  });

  it("sem IntersectionObserver o conteudo tambem fica visivel", () => {
    vi.stubGlobal("IntersectionObserver", undefined);
    const { container } = render(<Section />);

    for (const el of container.querySelectorAll<HTMLElement>("h2, p"))
      expect(el.style.opacity).toBe("");
    expect(reveal.revealOnEnter).not.toHaveBeenCalled();
  });

  it("ao desmontar antes de entrar, para de observar e devolve a visibilidade", () => {
    const { container, unmount } = render(<Section />);
    const title = container.querySelector("h2") as HTMLElement;
    const observer = FakeObserver.instances[0];

    unmount();

    expect(observer.disconnect).toHaveBeenCalled();
    expect(title.style.opacity).toBe("");
  });
});
