// O fundo em 3D (T-L05; decisao do usuario de 2026-11-14: arvores, terreno e construcoes em 3D). So numeros, sem
// three: o relevo do terreno e o plano de pinheiros, arvores gigantes, copas, torres e cristas, tirados das mesmas
// sequencias aleatorias das silhuetas (silhouettes.ts), para a composicao que a camera enquadra nao mudar.
import { boardLayout } from "./board";
import { PORTAL_TORCHES, STAND_TORCHES } from "./fire";
import { PROPS } from "./props";
import { type Pt, TRUNKS_Z, recordForest, trunkPlacements } from "./silhouettes";

/** Camadas do cenario: profundidade da silhueta, folga de profundidade para o 3D e o quanto a nevoa clareia. */
export interface SceneryLayer {
  readonly id: string;
  readonly z: number;
  /** Os objetos se espalham em z +- depth em volta da camada (volume em vez de um plano). */
  readonly depth: number;
  /** 0 = cor do material; 1 = cor da silhueta (a distancia apaga os detalhes, como a nevoa). */
  readonly haze: number;
}

export const SCENERY_LAYERS: readonly SceneryLayer[] = [
  { id: "montanhas", z: -260, depth: 40, haze: 0.82 },
  { id: "cidade", z: -170, depth: 25, haze: 0.7 },
  { id: "gigantes", z: -105, depth: 15, haze: 0.55 },
  { id: "anel", z: -55, depth: 10, haze: 0.4 },
  { id: "muro-de-ruinas", z: -24, depth: 6, haze: 0.25 },
  { id: "portal-de-troncos", z: TRUNKS_Z, depth: 0, haze: 0.15 },
  { id: "ruinas-do-caminho", z: 62, depth: 3, haze: 0.2 },
  { id: "corredor", z: 80, depth: 8, haze: 0.25 },
  { id: "dossel-fundo", z: 100, depth: 8, haze: 0.3 },
  { id: "dossel-frente", z: 128, depth: 10, haze: 0.35 },
];

export interface PinePlace {
  readonly layer: string;
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly h: number;
  readonly w: number;
}
export interface GiantPlace {
  readonly layer: string;
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly h: number;
  readonly tw: number;
}
export interface CrownPlace {
  readonly layer: string;
  readonly x: number;
  /** Centro da copa (relativo ao chao base, GROUND_Y). */
  readonly y: number;
  readonly z: number;
  readonly r: number;
}
export interface TowerPlace {
  readonly layer: string;
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly h: number;
  readonly w: number;
}
export interface RidgePlace {
  readonly layer: string;
  readonly z: number;
  /** Crista: (x, altura acima do chao base). */
  readonly pts: readonly Pt[];
}

export interface BushPlace {
  readonly x: number;
  readonly y: number;
  readonly z: number;
  /** Escala (raio do arbusto em m). */
  readonly s: number;
  readonly yaw: number;
}

export interface SceneryPlan {
  readonly bushes: BushPlace[];
  /** Cerca viva dos dois lados do caminho de terra (pedido do usuario, 2026-11-15). */
  readonly hedge: BushPlace[];
  readonly pines: PinePlace[];
  readonly giants: GiantPlace[];
  readonly crowns: CrownPlace[];
  readonly towers: TowerPlace[];
  readonly ridges: RidgePlace[];
}

function smoothstep(a: number, b: number, x: number): number {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
}

function hash(x: number, y: number): number {
  const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453;
  return s - Math.floor(s);
}

function valueNoise(x: number, y: number): number {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;
  const ux = fx * fx * (3 - 2 * fx);
  const uy = fy * fy * (3 - 2 * fy);
  const a = hash(ix, iy);
  const b = hash(ix + 1, iy);
  const c = hash(ix, iy + 1);
  const d = hash(ix + 1, iy + 1);
  return (a * (1 - ux) + b * ux) * (1 - uy) + (c * (1 - ux) + d * ux) * uy;
}

/** Faixa larga sem colinas em volta do trilho da camera (as colinas grandes ficam alem dela). */
const FLAT_HALF_WIDTH = 30;
const HILL_MAX = 7;

/** Pontos de apoio (x, z, raio): o chao fica plano sob tudo o que pousa nele (pedras, tochas, mural, troncos). */
const PADS: ReadonlyArray<readonly [number, number, number]> = [
  ...PROPS.map((p) => [p.x, p.z, 2.2 * p.scale + 0.6] as const),
  ...STAND_TORCHES.map((t) => [t.x, t.z, 1.6] as const),
  ...PORTAL_TORCHES.map((t) => [t.at[0], t.at[2], 1.4] as const),
  [boardLayout(false).center[0], boardLayout(false).center[2], 5.5],
  ...trunkPlacements().map((t) => [t.x, t.z, 3.2] as const),
];

/** 0 sobre os apoios e faixas planas (altar, caminho, pe dos muros), subindo suave ate 1 em volta. */
function padMask(x: number, z: number): number {
  const ax = Math.abs(x);
  let m = 1;
  // altar e o redor imediato (velas, tochas de tripe)
  m = Math.min(m, smoothstep(0, 3, Math.max(ax - 8, Math.abs(z) - 6)));
  // o caminho de pedra
  if (z > -8 && z < 78) m = Math.min(m, smoothstep(3.2, 6.5, ax));
  // pe do muro do portal e do muro de ruinas
  if (ax < 64) m = Math.min(m, smoothstep(5, 9, Math.abs(z - 42)));
  if (ax < 50) m = Math.min(m, smoothstep(2.5, 6, Math.abs(z + 24)));
  for (const [px, pz, r] of PADS) m = Math.min(m, smoothstep(r, r + 3, Math.hypot(x - px, z - pz)));
  return m;
}

/**
 * Altura do terreno acima do chao base (GROUND_Y): barrancos suaves dos lados do caminho, a clareira numa bacia rasa,
 * microrrelevo por toda parte e colinas grandes longe do trilho da camera. Plano (zero) sob o que pousa no chao.
 */
export function terrainHeight(x: number, z: number): number {
  const ax = Math.abs(x);
  const pad = padMask(x, z);
  if (pad <= 0) return 0;
  const banks = 1.6 * smoothstep(5, 24, ax) * smoothstep(14, 20, z) * (1 - smoothstep(70, 82, z));
  const bowl = 1.3 * smoothstep(15, 26, Math.hypot(x, z));
  const micro = 0.25 * valueNoise(x / 6 + 11.3, z / 6 - 4.1);
  let hills = 0;
  if (ax > FLAT_HALF_WIDTH) {
    let w = smoothstep(FLAT_HALF_WIDTH, 75, ax);
    if (ax < 52) w *= smoothstep(4, 22, Math.abs(z + 24));
    if (ax < 66) w *= smoothstep(7, 25, Math.abs(z - 42));
    hills =
      HILL_MAX *
      w *
      (valueNoise(x / 46, z / 46) * 0.7 + valueNoise(x / 17 + 3.1, z / 17 - 7.7) * 0.3);
  }
  return Math.min(8, (Math.max(banks, bowl) + micro + hills) * pad);
}

/** Deslocamento deterministico em [-1, 1] para espalhar a camada em profundidade. */
function jitter(i: number, salt: number): number {
  return hash(i * 0.731 + salt, salt * 1.37) * 2 - 1;
}

/**
 * Arbustos (pedido do usuario, 2026-11-15): grade com sorteio nos barrancos e nas bordas da clareira, fora do caminho,
 * da clareira e dos pontos de apoio; mais densos perto do caminho (onde a camera passa) e mais ralos longe.
 */
function bushPlan(): BushPlace[] {
  const out: BushPlace[] = [];
  // ate 44 m do caminho: alem disso a nevoa densa ja cobre tudo
  const step = 4.6;
  for (let gx = -44; gx <= 44; gx += step)
    for (let gz = -40; gz <= 110; gz += step) {
      const i = out.length + gx * 31 + gz * 17;
      const x = gx + jitter(i, 1.1) * step * 0.45;
      const z = gz + jitter(i, 2.3) * step * 0.45;
      const ax = Math.abs(x);
      if (z > -8 && z < 78 && ax <= 4.5) continue;
      if (Math.hypot(x, z) <= 10) continue;
      if (padMask(x, z) < 0.7) continue;
      const keep = 0.5 - 0.3 * smoothstep(8, 44, ax);
      if ((jitter(i, 5.7) + 1) / 2 > keep) continue;
      out.push({
        x,
        y: terrainHeight(x, z),
        z,
        s: 0.7 + ((jitter(i, 8.9) + 1) / 2) * 1.2,
        yaw: jitter(i, 4.4) * 3.1,
      });
    }
  return out;
}

/**
 * Cerca viva continua dos dois lados do caminho de terra, da entrada (z = 69) ate o altar (z = 6): duas fileiras
 * desencontradas de arbustos de tamanhos e afastamentos variados (organica, nada de linha reta), com o vao do arco do
 * portal livre.
 */
function hedgePlan(): BushPlace[] {
  const out: BushPlace[] = [];
  let i = 0;
  for (const side of [-1, 1])
    for (const row of [0, 1])
      for (let z = 6 + row * 0.55; z <= 69.5; z += 1.1) {
        i++;
        if (z > 39.8 && z < 44.2) continue;
        const s = 0.8 + ((jitter(i, 3.3) + 1) / 2) * 0.55 + row * 0.25;
        const x = side * (2.7 + row * 1.25 + s * 0.35 + jitter(i, 6.1) * 0.25);
        const zz = z + jitter(i, 7.7) * 0.3;
        out.push({ x, y: terrainHeight(x, zz), z: zz, s, yaw: jitter(i, 9.1) * 3.1 });
      }
  return out;
}

/** O plano do cenario 3D, das mesmas sequencias aleatorias das silhuetas. */
export function sceneryPlan(): SceneryPlan {
  const plan: SceneryPlan = {
    pines: [],
    giants: [],
    crowns: [],
    towers: [],
    ridges: [],
    bushes: bushPlan(),
    hedge: hedgePlan(),
  };
  const layers = new Map(SCENERY_LAYERS.map((l) => [l.id, l]));
  recordForest().forEach((e, i) => {
    const l = e.layer ? layers.get(e.layer) : undefined;
    if (!l) return;
    if (e.kind === "band") {
      plan.ridges.push({ layer: l.id, z: l.z - l.depth, pts: e.pts });
      return;
    }
    const z = l.z + jitter(i, l.z) * l.depth;
    const y = terrainHeight(e.x, z);
    if (e.kind === "pine") plan.pines.push({ layer: l.id, x: e.x, y, z, h: e.h, w: e.w });
    if (e.kind === "tower") plan.towers.push({ layer: l.id, x: e.x, y, z, h: e.h, w: e.w });
    if (e.kind === "giant") {
      // os troncos do portal de troncos ja sao o trunk.glb (trunkPlacements): aqui so as copas deles
      const gz = l.id === "portal-de-troncos" ? TRUNKS_Z : z;
      const gy = terrainHeight(e.x, gz);
      if (l.id !== "portal-de-troncos")
        plan.giants.push({ layer: l.id, x: e.x, y: gy, z: gz, h: e.h, tw: e.tw });
      plan.crowns.push({ layer: l.id, x: e.x, y: gy + e.h * 0.78, z: gz, r: e.cr });
    }
  });
  return plan;
}
