// T-L02 (REQ-L12, REQ-L09): a animacao dos planos usa anime.js e some com prefers-reduced-motion.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const timeline = { add: vi.fn(), revert: vi.fn() };
timeline.add.mockReturnValue(timeline);
const createTimeline = vi.fn(() => timeline);
const onScroll = vi.fn(() => "scroll-autoplay");
const set = vi.fn();
vi.mock("animejs", () => ({
  createTimeline: (...args: unknown[]) => (createTimeline as (...a: unknown[]) => unknown)(...args),
  onScroll: (...args: unknown[]) => (onScroll as (...a: unknown[]) => unknown)(...args),
  stagger: (n: number) => `stagger(${n})`,
  utils: { set: (...args: unknown[]) => set(...args) },
}));

import { animatePlans } from "./plansMotion";

function fixture() {
  document.body.innerHTML = `
    <section id="planos">
      <header><span>Planos</span><h2>Comece</h2></header>
      <div class="plans__shell"><article class="plan"><ul><li>a</li></ul></article></div>
      <div class="plans__shell"><article class="plan plan--paid"><svg><rect class="plan__trace"/></svg><b data-price>R$ 19,90</b><ul><li>b</li></ul></article></div>
    </section>`;
  return document.getElementById("planos") as HTMLElement;
}

function motion(reduce: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query.includes("reduce") ? reduce : false,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

beforeEach(() => {
  vi.clearAllMocks();
  timeline.add.mockReturnValue(timeline);
});
afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = "";
});

describe("animatePlans", () => {
  it("com movimento reduzido nao anima nada: o conteudo fica visivel e parado", () => {
    motion(true);
    const section = fixture();

    const stop = animatePlans(section);

    expect(createTimeline).not.toHaveBeenCalled();
    expect(set).not.toHaveBeenCalled();
    expect(section.querySelector("[data-price]")?.textContent).toBe("R$ 19,90");
    expect(() => stop()).not.toThrow();
  });

  it("sem reducao, monta uma linha do tempo ligada ao scroll e conta o preco ate R$ 19,90", () => {
    motion(false);
    const section = fixture();

    const stop = animatePlans(section);

    expect(onScroll).toHaveBeenCalledTimes(1);
    expect(createTimeline).toHaveBeenCalledWith(
      expect.objectContaining({ autoplay: "scroll-autoplay" }),
    );
    expect(timeline.add.mock.calls.length).toBeGreaterThanOrEqual(4);
    const price = timeline.add.mock.calls.find(
      ([target]) => typeof target === "object" && "v" in target,
    );
    expect(price?.[1].v).toBe(19.9);
    price?.[1].onUpdate();
    expect(section.querySelector("[data-price]")?.textContent).toMatch(/^R\$ \d+,\d{2}$/);
    stop();
    expect(timeline.revert).toHaveBeenCalled();
    expect(section.querySelector("[data-price]")?.textContent).toBe("R$ 19,90");
  });
});
