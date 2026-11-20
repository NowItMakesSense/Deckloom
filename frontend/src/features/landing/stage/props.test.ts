// Pedras com musgo e tocos do Poly Haven (T-L05, REQ-L14): ambientam o caminho e a clareira sem cobrir o altar, o
// caminho de pedra, as tochas nem o corredor do pan final ate o mural de avisos.
import { describe, expect, it } from "vitest";

import { boardLayout } from "./board";
import { PORTAL_TORCHES, STAND_TORCHES } from "./fire";
import { PROPS, ROCK_VARIANTS } from "./props";

/** Raio aproximado de cada modelo no chao (m, escala 1): pedras do rock_moss_set e o toco. */
const RADIUS = { rock: 1.7, stump: 0.8 } as const;

describe("pedras e tocos espalhados (props)", () => {
  it("tem pedras das quatro variantes do rocks.glb e ao menos dois tocos", () => {
    const rocks = PROPS.filter((p) => p.kind === "rock");
    expect(rocks.length).toBeGreaterThanOrEqual(6);
    expect(new Set(rocks.map((p) => p.variant))).toEqual(
      new Set(Array.from({ length: ROCK_VARIANTS }, (_, i) => i)),
    );
    expect(PROPS.filter((p) => p.kind === "stump").length).toBeGreaterThanOrEqual(2);
  });

  it.each(PROPS.map((p, i) => [i, p] as const))(
    "prop %i fica fora do altar, do caminho e das tochas",
    (_i, p) => {
      const r = RADIUS[p.kind] * p.scale;
      // altar: 13 x 9,2 m no centro, com 1 m de folga
      expect(Math.abs(p.x) - r > 7.5 || Math.abs(p.z) - r > 5.6).toBe(true);
      // caminho de pedra: 3,4 m de largura, de z = 5 a z = 69
      if (p.z > 5 - r && p.z < 69 + r) expect(Math.abs(p.x) - r).toBeGreaterThan(1.7);
      // tochas: 2 m livres em volta
      for (const t of STAND_TORCHES)
        expect(Math.hypot(t.x - p.x, t.z - p.z) - r).toBeGreaterThan(2);
      for (const t of PORTAL_TORCHES)
        expect(Math.hypot(t.at[0] - p.x, t.at[2] - p.z) - r).toBeGreaterThan(2);
    },
  );

  it("o corredor do pan final (do portal ao mural de avisos) fica livre", () => {
    const b = boardLayout(false);
    for (const p of PROPS) {
      const r = RADIUS[p.kind] * p.scale;
      const inCorridor = p.x - r < 1.5 && p.x + r > b.center[0] - 2 && p.z + r > 42 && p.z - r < 60;
      expect(inCorridor, `${p.kind} em ${p.x}, ${p.z}`).toBe(false);
    }
  });
});
