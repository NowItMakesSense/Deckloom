// T-L01B / AC-L01Bc: o helper de animacao usa anime.js e respeita prefers-reduced-motion.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const animateSpy = vi.hoisted(() => vi.fn());
vi.mock("animejs", async (importOriginal) => {
  const actual = await importOriginal<typeof import("animejs")>();
  return { ...actual, animate: animateSpy };
});

import { prefersReducedMotion, revealOnEnter } from "./index";

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

function elements(n: number): HTMLElement[] {
  return Array.from({ length: n }, () => document.body.appendChild(document.createElement("p")));
}

beforeEach(() => animateSpy.mockClear());
afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = "";
});

describe("prefersReducedMotion", () => {
  it("le a media query do usuario", () => {
    setReduced(true);
    expect(prefersReducedMotion()).toBe(true);
    setReduced(false);
    expect(prefersReducedMotion()).toBe(false);
  });

  it("sem matchMedia, assume movimento reduzido (conteudo parado e visivel)", () => {
    vi.stubGlobal("matchMedia", undefined);
    expect(prefersReducedMotion()).toBe(true);
  });
});

describe("revealOnEnter (anime.js)", () => {
  it("sem reducao, chama animate do anime.js subindo ate 24px com opacity e escalonando 80ms", () => {
    setReduced(false);
    const els = elements(2);
    revealOnEnter(els);
    expect(animateSpy).toHaveBeenCalledTimes(1);
    const [targets, params] = animateSpy.mock.calls[0];
    expect(targets).toBe(els);
    expect(params.translateY).toEqual([24, 0]);
    expect(params.opacity).toEqual([0, 1]);
    expect(params.delay).toBeDefined();
  });

  it("com reducao, nao chama animate e deixa o conteudo visivel, sem deslocar nem escalar", () => {
    setReduced(true);
    const els = elements(3);
    els.forEach((el) => {
      el.style.opacity = "0";
      el.style.transform = "translateY(24px) scale(0.9)";
    });
    revealOnEnter(els);
    expect(animateSpy).not.toHaveBeenCalled();
    els.forEach((el) => {
      expect(el.style.opacity).toBe("1");
      expect(el.style.transform).toBe("none");
    });
  });
});
