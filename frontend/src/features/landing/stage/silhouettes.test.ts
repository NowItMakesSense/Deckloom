// T-L02 / AC-L02d, AC-L02h (REQ-L10): os geradores de silhueta da floresta e a imagem estatica da clareira.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

import { clearingGround, projectLayer, STATIC_VIEW, toSvgPath } from "./clearing";
import { FOREST } from "./palette";
import {
  ARCH_Z,
  TRUNKS_Z,
  RUIN_WALL_Z,
  archRunePlacements,
  trunkPlacements,
  fernPlacements,
  forestLayers,
  heroLayers,
  pathRunePlacements,
  pine,
  shapes,
  STATIC_CAMERA,
} from "./silhouettes";
import type { Shapes } from "./silhouettes";

const finite = (s: Shapes) =>
  s.polys.every(
    (p) => p.length >= 3 && p.every(([x, y]) => Number.isFinite(x) && Number.isFinite(y)),
  ) && s.circles.every((c) => c.every(Number.isFinite) && c[2] > 0);

describe("geradores", () => {
  it("pine devolve uma conifera de 4 andares fechada, acima da base", () => {
    const o = shapes();
    pine(o, 0, 0, 10, 4);
    expect(o.polys).toHaveLength(1);
    const ys = o.polys[0].map((p) => p[1]);
    expect(Math.min(...ys)).toBe(0);
    expect(Math.max(...ys)).toBe(10);
  });
});

describe("forestLayers (palco WebGL)", () => {
  const layers = forestLayers();

  it("e deterministica e todas as formas sao validas", () => {
    expect(forestLayers()).toEqual(layers);
    for (const l of layers) expect(finite(l.body), l.id).toBe(true);
    for (const l of layers) if (l.accents) expect(finite(l.accents), l.id).toBe(true);
  });

  it("vai da camada mais distante (montanhas) ao dossel, do claro azulado ao quase preto", () => {
    const zs = layers.map((l) => l.z);
    expect([...zs].sort((a, b) => a - b)).toEqual(zs);
    expect(layers[0].color).toBe(FOREST.mount);
    expect(layers.at(-1)?.color).toBe(FOREST.canopyA);
    expect(zs).toContain(RUIN_WALL_Z);
    expect(new Set(layers.map((l) => l.id)).size).toBe(layers.length);
  });

  it("os 16 troncos do portal de troncos sao modelos (trunkPlacements); a camada so guarda as copas", () => {
    const trunks = trunkPlacements();
    expect(trunks).toHaveLength(16);
    // os dois gigantes que emolduram o caminho
    expect(trunks.slice(0, 2).map((t) => t.x)).toEqual([-6.8, 6.8]);
    for (const t of trunks) {
      expect(t.z).toBe(TRUNKS_Z);
      expect(t.h).toBeGreaterThan(20);
      expect(t.tw).toBeGreaterThan(1);
    }
    const layer = layers.find((l) => l.id === "portal-de-troncos");
    expect(layer?.body.polys).toEqual([]);
    expect(layer?.body.circles.length).toBeGreaterThan(16 * 8);
  });

  it("o portal de pedra saiu das silhuetas: virou modelo glTF (T-L05, AC-L05b)", () => {
    expect(layers.some((l) => l.z === ARCH_Z)).toBe(false);
    expect(layers.some((l) => l.id === "portal-de-pedra")).toBe(false);
  });
});

describe("runas e samambaias", () => {
  it("nove runas no arco, da esquerda para a direita, diante do portal", () => {
    const runes = archRunePlacements();
    expect(runes).toHaveLength(9);
    expect(runes.map((r) => r.x)).toEqual([...runes.map((r) => r.x)].sort((a, b) => a - b));
    // diante das aduelas do modelo (que saltam 0,24 m da face) e da chave (0,36 m)
    expect(runes.every((r) => r.z >= ARCH_Z + 0.4)).toBe(true);
    // na aduela: entre o vao (3,4 m de meio vao, flecha 3,9 m) e a borda de fora (+1,5 m)
    for (const r of runes) {
      const ex = Math.abs(r.x);
      const ey = r.y - 5.4;
      expect(Math.hypot(ex / 3.4, ey / 3.9)).toBeGreaterThan(1);
      expect(Math.hypot(ex / 4.9, ey / 5.4)).toBeLessThan(1);
    }
  });

  it("dezoito runas no caminho, alternando os lados, do portal a clareira", () => {
    const runes = pathRunePlacements();
    expect(runes).toHaveLength(18);
    expect(runes[0].x).toBeLessThan(0);
    expect(runes[1].x).toBeGreaterThan(0);
    expect(runes.map((r) => r.z)).toEqual([...runes.map((r) => r.z)].sort((a, b) => a - b));
  });

  it("samambaias deterministicas ao longo do caminho, fora dele", () => {
    const ferns = fernPlacements();
    expect(ferns).toHaveLength(34);
    expect(fernPlacements()).toEqual(ferns);
    expect(ferns.every((f) => Math.abs(f.x) >= 2)).toBe(true);
  });

  it("nenhuma samambaia no corredor do pan final (a esquerda do caminho, perto do portal)", () => {
    expect(fernPlacements().filter((f) => f.z > 45 && f.x < 0)).toEqual([]);
  });
});

describe("imagem estatica da clareira (fallback)", () => {
  const layers = heroLayers();

  it("tem as camadas distantes e a clareira, todas validas", () => {
    expect(layers.map((l) => l.z)).toEqual([2000, 900, 480, 320, 262]);
    for (const l of layers) expect(finite(l.body), l.id).toBe(true);
  });

  it("projeta so o que esta diante da camera final; mais perto, maior", () => {
    const view = STATIC_VIEW;
    const scales = layers.map((l) => projectLayer(l, STATIC_CAMERA, view)?.scale ?? null);
    expect(scales.every((s) => s !== null)).toBe(true);
    const nonNull = scales as number[];
    expect([...nonNull].sort((a, b) => a - b)).toEqual(nonNull);
    expect(projectLayer({ ...layers[0], z: 100 }, STATIC_CAMERA, view)).toBeNull();
  });

  it("o chao tem o horizonte no meio, o caminho em perspectiva e a clareira", () => {
    const g = clearingGround(STATIC_VIEW);
    expect(g.horizonY).toBe(STATIC_VIEW.h / 2);
    expect(g.path).toHaveLength(4);
    expect(g.path[0][1]).toBeGreaterThan(g.horizonY);
    expect(g.clearing.length).toBeGreaterThan(10);
    expect(g.stoneLines.length).toBeGreaterThan(0);
  });
});

describe("sem aleatoriedade fora da semente", () => {
  it("nenhum modulo do palco usa Math.random", () => {
    for (const file of [
      "silhouettes.ts",
      "tokens.ts",
      "explosion.ts",
      "deck.ts",
      "camera.ts",
      "clearing.ts",
    ]) {
      const text = readFileSync(resolve(__dirname, file), "utf-8");
      expect(text, file).not.toMatch(/Math\.random/);
    }
  });
});

describe("toSvgPath", () => {
  it("troca y para baixo e fecha cada poligono e circulo", () => {
    const o = shapes();
    o.polys.push([
      [0, 0],
      [4, 0],
      [4, 3],
    ]);
    o.circles.push([1, 1, 0.5]);

    const d = toSvgPath(o);

    expect(d).toContain("M0.00 0.00L4.00 0.00L4.00 -3.00Z");
    expect(d).toContain("A0.50 0.50");
    expect(d.match(/Z/g)).toHaveLength(2);
  });
});
