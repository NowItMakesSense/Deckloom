// O fundo em 3D (T-L05, decisao do usuario de 2026-11-14): terreno com relevo e o plano das arvores, torres,
// montanhas e ruinas, das mesmas sequencias aleatorias das silhuetas (a composicao que a camera enquadra nao muda).
import { describe, expect, it } from "vitest";

import { boardLayout } from "./board";
import { camPose } from "./camera";
import { PORTAL_TORCHES, STAND_TORCHES } from "./fire";
import { END } from "./phases";
import { PROPS } from "./props";
import { SCENERY_LAYERS, sceneryPlan, terrainHeight } from "./scenery";
import { ARCH_Z, RUIN_WALL_Z, TRUNKS_Z, trunkPlacements } from "./silhouettes";

const plan = sceneryPlan();

describe("terreno com relevo", () => {
  it("e plano onde a cena apoia coisas: clareira, caminho, portal, mural, muro de ruinas, pedras e tochas", () => {
    const flat: Array<[number, number]> = [
      [0, 0],
      [6, -4],
      [7.5, 0],
      [0, 30],
      [3, 60],
      [-40, ARCH_Z],
      [55, ARCH_Z],
      [boardLayout(false).center[0], boardLayout(false).center[2]],
      [-30, RUIN_WALL_Z],
      ...PROPS.map((p) => [p.x, p.z] as [number, number]),
      ...STAND_TORCHES.map((t) => [t.x, t.z] as [number, number]),
      ...PORTAL_TORCHES.map((t) => [t.at[0], t.at[2]] as [number, number]),
    ];
    for (const [x, z] of flat) expect(terrainHeight(x, z), `${x}, ${z}`).toBe(0);
  });

  it("nao e um plano: barrancos sobem dos lados do caminho e em volta da clareira", () => {
    expect(terrainHeight(14, 30)).toBeGreaterThan(0.5);
    expect(terrainHeight(15, 52)).toBeGreaterThan(0.5);
    expect(terrainHeight(0, -24 - 8)).toBeGreaterThanOrEqual(0);
    expect(terrainHeight(20, 4)).toBeGreaterThan(0.4);
    // o proprio caminho continua plano (as placas de pedra e as samambaias apoiam nele)
    for (let z = 0; z <= 70; z += 5) expect(terrainHeight(1.5, z)).toBe(0);
  });

  it("tem colinas longe do caminho, suaves e baixas (no maximo 8 m)", () => {
    let max = 0;
    for (let x = -300; x <= 300; x += 7)
      for (let z = -300; z <= 200; z += 7) {
        const hgt = terrainHeight(x, z);
        expect(hgt).toBeGreaterThanOrEqual(0);
        expect(hgt).toBeLessThanOrEqual(8);
        max = Math.max(max, hgt);
      }
    expect(max).toBeGreaterThan(2);
    // suave: vizinhos a 1 m nao saltam mais de 0,6 m
    for (let x = -200; x <= 200; x += 13)
      expect(Math.abs(terrainHeight(x, -80) - terrainHeight(x + 1, -80))).toBeLessThan(0.6);
    // tambem perto do caminho: sem degrau entre o plano e os barrancos
    for (let x = -40; x <= 40; x += 0.5)
      for (const z of [10, 30, 55])
        expect(Math.abs(terrainHeight(x, z) - terrainHeight(x + 0.5, z))).toBeLessThan(0.35);
  });
});

describe("plano do cenario 3D", () => {
  it("tem pinheiros, arvores gigantes, torres, cristas e as ruinas, cada um numa camada conhecida", () => {
    expect(plan.pines.length).toBeGreaterThan(150);
    expect(plan.giants.length).toBeGreaterThan(40);
    expect(plan.towers.length).toBeGreaterThan(25);
    expect(plan.ridges.length).toBeGreaterThanOrEqual(5);
    const ids = new Set(SCENERY_LAYERS.map((l) => l.id));
    for (const o of [...plan.pines, ...plan.giants, ...plan.towers, ...plan.ridges])
      expect(ids).toContain(o.layer);
  });

  it("tem arbustos espalhados nos barrancos e nas bordas, fora do caminho, da clareira e dos apoios", () => {
    expect(plan.bushes.length).toBeGreaterThanOrEqual(150);
    for (const b of plan.bushes) {
      expect(b.s).toBeGreaterThan(0.5);
      expect(b.s).toBeLessThan(2.2);
      if (b.z > -8 && b.z < 78)
        expect(Math.abs(b.x), `arbusto no caminho ${b.x}, ${b.z}`).toBeGreaterThan(4);
      expect(Math.hypot(b.x, b.z), "arbusto na clareira").toBeGreaterThan(9);
      expect(b.y).toBeCloseTo(terrainHeight(b.x, b.z), 6);
    }
  });

  it("cerca viva densa dos dois lados do caminho, continua da entrada ao altar (so o arco do portal fica livre)", () => {
    expect(plan.hedge.length).toBeGreaterThan(150);
    for (const side of [-1, 1]) {
      for (let z = 6; z <= 69; z += 1) {
        if (z > 39.5 && z < 44.5) continue; // o vao do arco do portal
        const near = plan.hedge.filter(
          (b) =>
            Math.sign(b.x) === side &&
            Math.abs(b.z - z) <= 1.0 &&
            Math.abs(b.x) >= 2.4 &&
            Math.abs(b.x) <= 5.5,
        );
        expect(
          near.length,
          `buraco na cerca ${side > 0 ? "direita" : "esquerda"} em z = ${z}`,
        ).toBeGreaterThan(0);
      }
    }
    for (const b of plan.hedge) {
      // fora do caminho de terra (3,4 m de largura) e apoiada no chao
      expect(Math.abs(b.x) - b.s * 0.9).toBeGreaterThan(1.7 - 0.6);
      expect(b.y).toBeCloseTo(terrainHeight(b.x, b.z), 6);
    }
    // organica: nem todas no mesmo alinhamento nem do mesmo tamanho
    expect(new Set(plan.hedge.map((b) => Math.abs(b.x).toFixed(1))).size).toBeGreaterThan(10);
    expect(new Set(plan.hedge.map((b) => b.s.toFixed(1))).size).toBeGreaterThan(5);
  });

  it("e deterministico", () => {
    expect(sceneryPlan()).toEqual(plan);
  });

  it("os objetos ficam na faixa de profundidade da propria camada e apoiados no terreno", () => {
    for (const o of [...plan.pines, ...plan.giants, ...plan.towers]) {
      const l = SCENERY_LAYERS.find((x) => x.id === o.layer)!;
      expect(Math.abs(o.z - l.z), `${o.layer} ${o.x}`).toBeLessThanOrEqual(l.depth + 1e-9);
      expect(o.y).toBeCloseTo(terrainHeight(o.x, o.z), 6);
    }
  });

  it("as copas do portal de troncos ficam no topo dos troncos 3D", () => {
    const trunks = trunkPlacements();
    expect(plan.crowns.length).toBeGreaterThanOrEqual(trunks.length);
    for (const t of trunks) {
      const c = plan.crowns.find((k) => Math.abs(k.x - t.x) < 1e-9 && k.z === TRUNKS_Z);
      expect(c, `copa do tronco ${t.x}`).toBeDefined();
      expect(c!.y).toBeGreaterThan(t.h * 0.7);
    }
  });

  it("a camera nunca entra numa arvore, torre ou copa em todo o trilho da landing", () => {
    const solids = [
      ...plan.pines.map((p) => ({ x: p.x, z: p.z, r: p.w * 0.5, bottom: -9, top: p.y + p.h })),
      ...plan.giants.map((g) => ({
        x: g.x,
        z: g.z,
        r: g.tw * 1.1,
        bottom: -9,
        top: g.y + g.h * 0.75,
      })),
      // a copa fica no alto: a camera pode passar por baixo dela
      ...plan.crowns.map((c) => ({
        x: c.x,
        z: c.z,
        r: c.r * 1.2,
        bottom: c.y - c.r * 0.8,
        top: c.y + c.r * 0.9,
      })),
      ...plan.towers.map((t) => ({
        x: t.x,
        z: t.z,
        r: t.w * 0.75,
        bottom: -9,
        top: t.y + t.h + t.w * 2.4,
      })),
    ];
    for (let raw = 0; raw <= END; raw += 0.002) {
      const p = camPose(raw, 0, false).pos;
      for (const s of solids) {
        const y = p[1] + 0.4; // altura da camera acima do chao base (GROUND_Y = -0,4)
        if (y > s.top + 0.3 || y < s.bottom - 0.3) continue;
        const d = Math.hypot(p[0] - s.x, p[2] - s.z);
        expect(d, `camera em raw ${raw.toFixed(3)} dentro de ${s.x}, ${s.z}`).toBeGreaterThan(
          s.r + 0.3,
        );
      }
      expect(p[1], `camera abaixo do terreno em raw ${raw}`).toBeGreaterThan(
        terrainHeight(p[0], p[2]) - 0.4 + 0.5,
      );
    }
  });
});
