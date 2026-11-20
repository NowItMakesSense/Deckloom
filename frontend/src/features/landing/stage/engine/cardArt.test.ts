// Arte propria das cartas (T-L05, AC-L05c): uma ilustracao por gancho e o verso novo, desenhados em canvas.
// O teste grava as chamadas de desenho num contexto falso: cada gancho tem arte diferente e tudo fica dentro da janela.
import { describe, expect, it } from "vitest";

import { ART_WINDOW, HOOK_ARTS, drawCardBack, drawHookArt } from "./cardArt";

interface Call {
  readonly name: string;
  readonly args: readonly unknown[];
}

function recorder(): { g: CanvasRenderingContext2D; calls: Call[] } {
  const calls: Call[] = [];
  const gradient = { addColorStop: () => undefined };
  const target: Record<string, unknown> = {};
  const g = new Proxy(target, {
    get: (_t, key) => {
      if (typeof key === "symbol") return undefined;
      if (key in target) return target[key];
      if (key.startsWith("create")) return () => gradient;
      if (key === "measureText") return () => ({ width: 10 });
      return (...args: unknown[]) => {
        calls.push({ name: key, args });
      };
    },
    set: (_t, key, value) => {
      target[key as string] = value;
      return true;
    },
  }) as unknown as CanvasRenderingContext2D;
  return { g, calls };
}

/** Pontos (x, y) de cada chamada de caminho; arcos contam o centro +- o raio. */
function points(calls: Call[]): Array<[number, number]> {
  const out: Array<[number, number]> = [];
  for (const c of calls) {
    const a = c.args as number[];
    if (c.name === "moveTo" || c.name === "lineTo") out.push([a[0], a[1]]);
    if (c.name === "quadraticCurveTo") out.push([a[0], a[1]], [a[2], a[3]]);
    if (c.name === "bezierCurveTo") out.push([a[4], a[5]]);
    if (c.name === "arc" || c.name === "ellipse") {
      const rx = a[2];
      const ry = c.name === "ellipse" ? a[3] : a[2];
      out.push([a[0] - rx, a[1] - ry], [a[0] + rx, a[1] + ry]);
    }
  }
  return out;
}

describe("arte das cartas dos ganchos", () => {
  it("quatro ilustracoes, uma por gancho, todas diferentes", () => {
    expect(HOOK_ARTS).toHaveLength(4);
    expect(new Set(HOOK_ARTS).size).toBe(4);
    const logs = HOOK_ARTS.map((_, i) => {
      const r = recorder();
      drawHookArt(r.g, i);
      return JSON.stringify(r.calls);
    });
    expect(new Set(logs).size).toBe(4);
    for (const l of logs) expect(l.length).toBeGreaterThan(200);
  });

  it.each(HOOK_ARTS.map((id, i) => [id, i] as const))(
    "a arte %s fica dentro da janela de arte",
    (_id, i) => {
      const r = recorder();
      drawHookArt(r.g, i);
      const pts = points(r.calls);
      expect(pts.length).toBeGreaterThan(10);
      for (const [x, y] of pts) {
        expect(x).toBeGreaterThanOrEqual(ART_WINDOW.x - 0.5);
        expect(x).toBeLessThanOrEqual(ART_WINDOW.x + ART_WINDOW.w + 0.5);
        expect(y).toBeGreaterThanOrEqual(ART_WINDOW.y - 0.5);
        expect(y).toBeLessThanOrEqual(ART_WINDOW.y + ART_WINDOW.h + 0.5);
      }
    },
  );

  it("o verso novo desenha o selo de runas e o simbolo dentro da carta (512 x 716)", () => {
    const r = recorder();
    drawCardBack(r.g);
    const pts = points(r.calls);
    expect(pts.length).toBeGreaterThan(50);
    for (const [x, y] of pts) {
      expect(x).toBeGreaterThanOrEqual(-0.5);
      expect(x).toBeLessThanOrEqual(512.5);
      expect(y).toBeGreaterThanOrEqual(-0.5);
      expect(y).toBeLessThanOrEqual(716.5);
    }
    // o anel de selos: ao menos doze circulos pequenos em volta do centro
    const small = r.calls.filter((c) => c.name === "arc" && (c.args[2] as number) < 30);
    expect(small.length).toBeGreaterThanOrEqual(12);
  });
});
