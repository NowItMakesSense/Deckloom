// T-L02 / AC-L02k (REQ-L10): a explosao do deck, como funcao pura.
import { describe, expect, it } from "vitest";

import { explodedOffset, planExplosion } from "./explosion";

describe("planExplosion", () => {
  const plan = planExplosion(44);

  it("e deterministico", () => {
    expect(planExplosion(44)).toEqual(plan);
  });

  it("cada carta tem trajetoria, rotacao e velocidade proprias", () => {
    expect(new Set(plan.map((c) => c.dir.join(","))).size).toBe(44);
    expect(new Set(plan.map((c) => c.rot.join(","))).size).toBe(44);
    expect(new Set(plan.map((c) => c.pow.toFixed(6))).size).toBeGreaterThan(30);
    for (const c of plan) expect(Math.hypot(...c.dir)).toBeCloseTo(1);
  });

  it("tres categorias: para a camera, para o fundo e para os lados", () => {
    expect(plan[0].dir[2]).toBeGreaterThan(0);
    expect(plan[1].dir[2]).toBeLessThan(0);
    expect(Math.abs(plan[2].dir[0])).toBeGreaterThan(0.5);
  });
});

describe("explodedOffset", () => {
  const plan = planExplosion(44);

  it("antes da explosao nao desloca e a carta aparece; depois some", () => {
    const before = explodedOffset(0, plan[0]);
    expect(before.offset).toEqual([0, 0, 0]);
    expect(before.visible).toBe(true);
    expect(explodedOffset(1, plan[0]).visible).toBe(false);
  });

  it("as cartas que vao ao fundo saem por cima do quadro", () => {
    const back = explodedOffset(0.4, plan[1]);
    expect(back.offset[1]).toBeGreaterThan(8);
    expect(back.offset[2]).toBeLessThan(0);
  });

  it("o deslocamento cresce com o progresso ate a carta sumir", () => {
    let previous = 0;
    for (let i = 1; i <= 100; i++) {
      const o = explodedOffset(0.06 + (i / 100) * 0.5, plan[0]);
      const dist = Math.hypot(...o.offset);
      expect(dist).toBeGreaterThanOrEqual(previous - 1e-9);
      previous = dist;
    }
  });

  it("as rotacoes proprias crescem junto", () => {
    expect(Math.hypot(...explodedOffset(0.3, plan[4]).rotation)).toBeGreaterThan(0.01);
  });
});
