// T-L02 / REQ-L10 (decisao do usuario de 2026-11-13: "religue as nevoas, mas quero manter a floresta escura. Quero de
// noite basicamente"): as faixas de nevoa sao bruma noturna entre as camadas de silhueta (profundidade a la Firewatch).
// Precisam ser vistas diante das silhuetas escuras sem clarear a cena: cor fria e dessaturada da paleta, um pouco mais
// clara que as silhuetas e mais escura que o horizonte, opacidade moderada, deriva lenta e afinam na explosao.
import { describe, expect, it } from "vitest";

import {
  MIST_MAX_OPACITY,
  MIST_SPRITE_PEAK,
  lumaOf,
  mistBands,
  mistDrift,
  mistNearFade,
  mistOpacity,
} from "./mist";
import { fogBankOpacity, fogBanks } from "./mist";
import { FOREST, MIST, NIGHT, SKY } from "./palette";

/** Luminancia de uma cor da paleta depois do escurecimento da noite (como a cena desenha as silhuetas). */
const nightLuma = (hex: string) => lumaOf(hex) * NIGHT;
/** As silhuetas escuras diante das quais a bruma tem de aparecer (anel de arvores ate o dossel). */
const DARK_SILHOUETTES = [
  FOREST.ring,
  FOREST.ruin,
  FOREST.portal,
  FOREST.ruins2,
  FOREST.corridor,
  FOREST.canopyB,
  FOREST.canopyA,
  FOREST.trunk,
];

describe("faixas de nevoa", () => {
  it("doze faixas deterministicas; nenhuma atravessa o mural de avisos (z perto de 46)", () => {
    const bands = mistBands();
    expect(bands).toHaveLength(12);
    expect(mistBands()).toEqual(bands);
    expect(bands.filter((b) => Math.abs(b.z - 46) < 6)).toEqual([]);
  });

  it("a cor vem da paleta: azul frio e dessaturado, na faixa de #2a2f4a a #3a3d5c", () => {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(MIST.color.slice(i, i + 2), 16));
    expect(b).toBeGreaterThan(r);
    expect(b).toBeGreaterThan(g);
    expect(Math.max(r, g, b) - Math.min(r, g, b)).toBeLessThanOrEqual(45); // dessaturada
    expect(lumaOf(MIST.color)).toBeGreaterThanOrEqual(lumaOf("#2a2f4a"));
    expect(lumaOf(MIST.color)).toBeLessThanOrEqual(lumaOf("#3a3d5c"));
  });

  it("um pouco mais clara que as silhuetas escuras da noite e mais escura que o ceu do horizonte", () => {
    for (const hex of DARK_SILHOUETTES)
      expect(lumaOf(MIST.color), hex).toBeGreaterThan(nightLuma(hex));
    expect(lumaOf(MIST.color)).toBeLessThan(lumaOf(SKY.horizon));
  });

  it("visivel: no miolo de uma faixa sobre silhueta escura a diferenca passa de 6 niveis (0 a 255)", () => {
    const peak = MIST_MAX_OPACITY * MIST_SPRITE_PEAK;
    for (const hex of DARK_SILHOUETTES) {
      expect(peak * (lumaOf(MIST.color) - nightLuma(hex)), hex).toBeGreaterThanOrEqual(6);
    }
  });

  it("opacidade moderada vinda da paleta, com mistura normal (nunca opaca)", () => {
    expect(MIST_MAX_OPACITY).toBeGreaterThanOrEqual(0.2);
    expect(MIST_MAX_OPACITY).toBeLessThanOrEqual(0.45);
    for (const band of mistBands()) {
      expect(mistOpacity(band, 0)).toBeCloseTo(band.opacity * MIST.opacity);
      expect(mistOpacity(band, 0)).toBeLessThanOrEqual(MIST_MAX_OPACITY);
    }
  });

  it("deriva devagar (no maximo 0,3 unidade por segundo) e volta ao lugar", () => {
    const band = mistBands()[3];
    let max = 0;
    for (let i = 0; i < 2000; i++) {
      const t = i / 10;
      max = Math.max(max, Math.abs(mistDrift(band, t + 0.1) - mistDrift(band, t)) / 0.1);
      expect(Math.abs(mistDrift(band, t) - band.x)).toBeLessThanOrEqual(6);
    }
    expect(max).toBeLessThanOrEqual(0.3);
  });

  it("some quando a camera chega perto da faixa (dentro da bruma nao se ve o veu, e a cena nao lava)", () => {
    expect(mistNearFade(0)).toBe(0);
    expect(mistNearFade(4)).toBe(0);
    expect(mistNearFade(14)).toBe(1);
    expect(mistNearFade(60)).toBe(1);
    const mid = mistNearFade(9);
    expect(mid).toBeGreaterThan(0);
    expect(mid).toBeLessThan(1);
    const band = mistBands()[2];
    expect(mistOpacity(band, 0, 2)).toBe(0);
    expect(mistOpacity(band, 0, 40)).toBeCloseTo(mistOpacity(band, 0));
  });

  it("reage a explosao: a nevoa afina pela metade no pico (opacidade x (1 - 0,5 x burst))", () => {
    const band = mistBands()[0];
    expect(mistOpacity(band, 1)).toBeCloseTo(mistOpacity(band, 0) * 0.5);
    expect(mistOpacity(band, 0.5)).toBeCloseTo(mistOpacity(band, 0) * 0.75);
  });
});

describe("bancos de nevoa volumetrica (2026-11-15)", () => {
  const banks = fogBanks();

  it("muitos bancos baixos ao longo do caminho e entre as arvores, densos", () => {
    expect(banks.length).toBeGreaterThanOrEqual(70);
    for (const b of banks) {
      expect(b.y).toBeGreaterThanOrEqual(0);
      expect(b.y).toBeLessThanOrEqual(3.5);
      expect(b.opacity).toBeGreaterThanOrEqual(0.25);
      expect(b.opacity).toBeLessThanOrEqual(0.6);
    }
    // o caminho, da entrada ao altar, tem nevoa por toda a extensao
    for (let z = 6; z <= 69; z += 6)
      expect(
        banks.some((b) => Math.abs(b.z - z) < 5 && Math.abs(b.x) < 8),
        `sem nevoa em z = ${z}`,
      ).toBe(true);
    // e o altar fica parcialmente envolto
    expect(banks.some((b) => Math.hypot(b.x, b.z) < 8)).toBe(true);
  });

  it("um banco some ao chegar perto da camera (sem parede de nevoa na cara)", () => {
    const b = banks[0];
    expect(fogBankOpacity(b, 0.5)).toBeLessThan(0.05);
    expect(fogBankOpacity(b, 20)).toBeCloseTo(b.opacity, 6);
  });
});
