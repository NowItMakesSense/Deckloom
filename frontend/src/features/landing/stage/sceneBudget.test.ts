// Orcamento da cena real (T-L05; o usuario subiu o teto para 250 mil triangulos em 2026-11-14): os triangulos de cada
// no dos glTF de verdade, multiplicados por quantas copias a cena usa.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

import { CARD_COUNT } from "./deck";
import { PORTAL_TORCHES, STAND_TORCHES } from "./fire";
import { PROPS } from "./props";
import { sceneryPlan } from "./scenery";
import { trunkPlacements } from "./silhouettes";
import { TERRAIN_SEGMENTS } from "./engine/scenery3d";

interface Gltf {
  nodes: { name?: string; mesh?: number; children?: number[] }[];
  meshes: { primitives: { indices?: number }[] }[];
  accessors: { count: number }[];
}

function load(file: string): Gltf {
  const buf = readFileSync(resolve(__dirname, "../../../../public/models", file));
  const len = buf.readUInt32LE(12);
  return JSON.parse(buf.toString("utf8", 20, 20 + len)) as Gltf;
}

/** Triangulos do no `name` e de todos os descendentes. */
function tris(g: Gltf, name: string): number {
  const start = g.nodes.findIndex((n) => n.name === name);
  expect(start, name).toBeGreaterThanOrEqual(0);
  let total = 0;
  const stack = [start];
  while (stack.length) {
    const n = g.nodes[stack.pop() as number];
    if (n.mesh !== undefined)
      for (const p of g.meshes[n.mesh].primitives)
        total += p.indices === undefined ? 0 : g.accessors[p.indices].count / 3;
    stack.push(...(n.children ?? []));
  }
  return total;
}

describe("orcamento de triangulos da cena inteira", () => {
  it("a cena com todas as copias fica em ate 250 mil triangulos", () => {
    const plan = sceneryPlan();
    const nature = load("nature.glb");
    const bld = load("buildings.glb");
    const pieces = load("pieces.glb");
    const torches = load("torches.glb");
    const rocks = load("rocks.glb");
    const body = (h: number) => tris(bld, `TowerBody${h}`);
    const nearest = (h: number) =>
      [20, 40, 70].reduce((a, b) => (Math.abs(b - h) < Math.abs(a - h) ? b : a));
    const parts: Record<string, number> = {
      pinheiros: plan.pines.length * Math.max(...[0, 1, 2].map((v) => tris(nature, `Pine${v}`))),
      gigantes:
        plan.giants.length * Math.max(...[0, 1, 2].map((v) => tris(nature, `Broadleaf${v}`))),
      copas:
        plan.crowns.filter((c) => c.layer === "portal-de-troncos").length *
        tris(nature, "LeafCrown"),
      arbustos: (plan.bushes.length + plan.hedge.length) * tris(nature, "Bush"),
      torres: plan.towers.reduce((n, t) => n + body(nearest(t.h)) + tris(bld, "TowerSpire"), 0),
      ruinas: tris(bld, "RuinWall") + tris(bld, "RuinPath"),
      troncos: trunkPlacements().length * tris(load("trunk.glb"), "Trunk"),
      velas: 8 * tris(load("candlestick.glb"), "Candlestick"),
      pedrasETocos:
        PROPS.filter((p) => p.kind === "rock").reduce(
          (n, p) => n + tris(rocks, `Rock${p.variant}`),
          0,
        ) +
        PROPS.filter((p) => p.kind === "stump").length * tris(load("stump.glb"), "Stump"),
      tochas:
        STAND_TORCHES.length * tris(torches, "StandTorch") +
        (PORTAL_TORCHES.length + 2) * tris(torches, "WallTorch"),
      pecas: CARD_COUNT * tris(pieces, "Card") + 25 * tris(pieces, "Medallion"),
      unicos:
        tris(load("deckbox.glb"), "DeckBox") +
        tris(load("altar.glb"), "Altar") +
        tris(load("portal.glb"), "Portal") +
        tris(load("noticeboard.glb"), "NoticeBoardWide"),
      // terreno e cristas
      terreno:
        TERRAIN_SEGMENTS * TERRAIN_SEGMENTS * 2 +
        plan.ridges.reduce((n, r) => n + (r.pts.length - 1) * 4, 0),
    };
    const total = Object.values(parts).reduce((a, b) => a + b, 0);
    expect(total, JSON.stringify(parts)).toBeLessThanOrEqual(250_000);
  });
});
