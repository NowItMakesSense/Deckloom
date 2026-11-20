// T-L01C: riseOnEnter - entrada do painel da tela (so transform, sem opacity para nao reprovar contraste em axe).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const animateSpy = vi.hoisted(() => vi.fn());
vi.mock("animejs", async (importOriginal) => {
  const actual = await importOriginal<typeof import("animejs")>();
  return { ...actual, animate: animateSpy };
});

import { riseOnEnter } from "./index";

function setReduced(reduced: boolean) {
  vi.stubGlobal(
    "matchMedia",
    vi.fn((query: string) => ({
      matches: reduced && query.includes("prefers-reduced-motion: reduce"),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  );
}

beforeEach(() => animateSpy.mockClear());
afterEach(() => vi.unstubAllGlobals());

describe("riseOnEnter", () => {
  it("sem reducao, sobe 16px so com transform (sem opacity)", () => {
    setReduced(false);
    const el = document.createElement("main");
    riseOnEnter(el);
    const [target, params] = animateSpy.mock.calls[0];
    expect(target).toBe(el);
    expect(params.translateY).toEqual([16, 0]);
    expect(params.opacity).toBeUndefined();
    expect(params.duration).toBeLessThanOrEqual(600);
  });

  it("ao terminar, tira o transform inline: um transform deixado viraria o bloco de contencao do Dialog fixo", () => {
    setReduced(false);
    const el = document.createElement("main");
    riseOnEnter(el);
    el.style.transform = "translateY(0px)";

    animateSpy.mock.calls[0][1].onComplete();

    expect(el.style.transform).toBe("");
  });

  it("com reducao, nao anima", () => {
    setReduced(true);
    riseOnEnter(document.createElement("main"));
    expect(animateSpy).not.toHaveBeenCalled();
  });
});
