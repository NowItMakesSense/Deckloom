// Geradores de silhueta da floresta elfica (REQ-L10): funcoes puras que devolvem poligonos e circulos (y para
// cima). A mesma geometria alimenta o palco WebGL (ShapeGeometry) e a imagem estatica da clareira (SVG).
import { FOREST } from "./palette";
import { rng } from "./math";

export type Pt = readonly [number, number];
export type Circle = readonly [number, number, number];

export interface Shapes {
  polys: Pt[][];
  circles: Circle[];
}

export const shapes = (): Shapes => ({ polys: [], circles: [] });

/** Eventos do desenho das silhuetas, para o cenario 3D reaproveitar as mesmas posicoes (scenery.ts). */
export type SceneryEvent =
  | { kind: "pine"; x: number; h: number; w: number; layer?: string }
  | { kind: "giant"; x: number; h: number; tw: number; cr: number; layer?: string }
  | { kind: "tower"; x: number; h: number; w: number; layer?: string }
  | { kind: "band"; pts: Pt[]; layer?: string };

let sink: SceneryEvent[] | null = null;

/** Roda as camadas da floresta gravando cada pinheiro, gigante, torre e faixa, com o id da camada. */
export function recordForest(): SceneryEvent[] {
  sink = [];
  try {
    forestLayers();
    return sink;
  } finally {
    sink = null;
  }
}

type Rand = () => number;

export function rectPoly(x: number, y: number, w: number, h: number): Pt[] {
  return [
    [x, y],
    [x + w, y],
    [x + w, y + h],
    [x, y + h],
  ];
}

/** Conifera de quatro andares. */
export function pine(o: Shapes, x: number, b: number, h: number, w: number): void {
  sink?.push({ kind: "pine", x, h, w });
  const pts: Pt[] = [
    [x - w * 0.07, b],
    [x - w * 0.07, b + h * 0.12],
  ];
  for (let k = 0; k < 4; k++) {
    const y0 = b + h * (0.12 + k * 0.2);
    const ww = (w * (1 - k * 0.2)) / 2;
    pts.push([x - ww, y0], [x - ww * 0.35, y0 + h * 0.2]);
  }
  pts.push([x, b + h]);
  for (let k = 3; k >= 0; k--) {
    const y1 = b + h * (0.12 + k * 0.2);
    const w1 = (w * (1 - k * 0.2)) / 2;
    pts.push([x + w1 * 0.35, y1 + h * 0.2], [x + w1, y1]);
  }
  pts.push([x + w * 0.07, b + h * 0.12], [x + w * 0.07, b]);
  o.polys.push(pts);
}

/** Arvore gigante: tronco com raizes, copa de circulos e (no detalhe rico) galhos. */
export function giant(
  o: Shapes,
  x: number,
  b: number,
  h: number,
  tw: number,
  cr: number,
  r: Rand,
  rich: boolean,
  trunk = true,
): void {
  sink?.push({ kind: "giant", x, h, tw, cr });
  if (trunk)
    o.polys.push([
      [x - tw * 1.7, b],
      [x - tw * 0.62, b + h * 0.07],
      [x - tw * 0.5, b + h * 0.72],
      [x + tw * 0.5, b + h * 0.72],
      [x + tw * 0.62, b + h * 0.07],
      [x + tw * 1.7, b],
    ]);
  if (rich) {
    o.polys.push([
      [x - tw * 0.4, b + h * 0.55],
      [x - cr * 0.95, b + h * 0.7],
      [x - cr * 0.95, b + h * 0.73],
      [x - tw * 0.4, b + h * 0.6],
    ]);
    o.polys.push([
      [x + tw * 0.4, b + h * 0.5],
      [x + cr * 0.9, b + h * 0.66],
      [x + cr * 0.9, b + h * 0.69],
      [x + tw * 0.4, b + h * 0.55],
    ]);
  }
  for (let k = 0; k < 7; k++) {
    const a = (k / 7) * Math.PI * 2 + r() * 0.5;
    o.circles.push([
      x + Math.cos(a) * cr * 0.7,
      b + h * 0.8 + Math.sin(a) * cr * 0.42,
      cr * (0.5 + r() * 0.3),
    ]);
  }
  o.circles.push([x, b + h * 0.84, cr * 0.75]);
}

/** Torre elfica de telhado em agulha; as janelas vao para `win`. */
export function tower(
  o: Shapes,
  win: Shapes,
  x: number,
  b: number,
  h: number,
  w: number,
  r: Rand,
  rich: boolean,
): void {
  sink?.push({ kind: "tower", x, h, w });
  o.polys.push(rectPoly(x - w / 2, b, w, h));
  o.polys.push([
    [x - w * 0.72, b + h],
    [x, b + h + w * 2.4],
    [x + w * 0.72, b + h],
  ]);
  o.polys.push(rectPoly(x - w * 0.06, b + h + w * 2.3, w * 0.12, w * 0.9));
  if (rich) {
    o.polys.push(rectPoly(x - w * 0.78, b + h * 0.62, w * 1.56, w * 0.16));
    o.polys.push([
      [x - w * 0.78, b + h * 0.62],
      [x - w * 0.5, b + h * 0.55],
      [x + w * 0.5, b + h * 0.55],
      [x + w * 0.78, b + h * 0.62],
    ]);
  }
  for (let k = 0; k < 5; k++) {
    if (r() >= 0.7) continue;
    if (rich) {
      const yy = b + h * (0.18 + k * 0.16);
      win.polys.push([
        [x - w * 0.11, yy],
        [x + w * 0.11, yy],
        [x + w * 0.11, yy + w * 0.38],
        [x, yy + w * 0.52],
        [x - w * 0.11, yy + w * 0.38],
      ]);
    } else {
      win.polys.push(rectPoly(x - w * 0.11, b + h * (0.18 + k * 0.16), w * 0.22, w * 0.45));
    }
  }
}

/** Ponte em arco entre duas torres. */
export function bridge(o: Shapes, x1: number, x2: number, y: number, th: number, H: number): void {
  const pts: Pt[] = [
    [x1, y],
    [x2, y],
    [x2, y - th - H],
  ];
  const xc = (x1 + x2) / 2;
  const half = (x2 - x1) / 2;
  for (let k = 1; k < 16; k++) {
    const x = x2 - ((x2 - x1) * k) / 16;
    const d = (x - xc) / half;
    pts.push([x, y - th - H * d * d]);
  }
  pts.push([x1, y - th - H]);
  o.polys.push(pts);
}

/** Domo com agulha. */
export function dome(o: Shapes, x: number, b: number, w: number, h: number): void {
  const pts: Pt[] = [[x - w / 2, b]];
  for (let k = 0; k <= 12; k++) {
    const a = Math.PI - (k / 12) * Math.PI;
    pts.push([x + Math.cos(a) * (w / 2), b + h + Math.sin(a) * (w / 2)]);
  }
  pts.push([x + w / 2, b]);
  o.polys.push(pts);
  o.polys.push(rectPoly(x - w * 0.04, b + h + w / 2, w * 0.08, w * 0.4));
}

/** Faixa serrilhada (cumes de montanha, linha de copas). */
export function band(
  o: Shapes,
  x1: number,
  x2: number,
  b: number,
  hMin: number,
  hMax: number,
  r: Rand,
  step: number,
): void {
  const pts: Pt[] = [[x1, b]];
  for (let x = x1; x <= x2; x += step) pts.push([x, b + hMin + r() * (hMax - hMin)]);
  pts.push([x2, b]);
  o.polys.push(pts);
  sink?.push({ kind: "band", pts: pts.slice(1, -1).map(([x, y]) => [x, y - b] as Pt) });
}

/** Uma camada da floresta: silhuetas a uma profundidade `z`, com acentos (janelas, musgo). */
export interface ForestLayer {
  readonly id: string;
  readonly z: number;
  readonly color: string;
  readonly body: Shapes;
  readonly accentColor?: string;
  readonly accents?: Shapes;
}

/** Base do chao da floresta no palco WebGL. */
export const GROUND_Y = -0.4;

function layer(
  id: string,
  z: number,
  color: string,
  body: Shapes,
  accentColor?: string,
  accents?: Shapes,
): ForestLayer {
  if (sink) for (const e of sink) if (e.layer === undefined) e.layer = id;
  return { id, z, color, body, accentColor, accents };
}

/** Profundidade do portal de troncos: os troncos sao o trunk.glb; a silhueta so guarda as copas. */
export const TRUNKS_Z = 22;

/** Um tronco gigante do portal de troncos: posicao, altura e largura da silhueta giant(). */
export interface TrunkPlacement {
  readonly x: number;
  readonly z: number;
  readonly h: number;
  readonly tw: number;
}

/** O portal de troncos: as copas (silhueta) e os troncos (modelo), da mesma sequencia aleatoria. */
function trunkLayer(): { crowns: Shapes; trunks: TrunkPlacement[] } {
  const r = rng(106);
  const crowns = shapes();
  const trunks: TrunkPlacement[] = [];
  const add = (x: number, h: number, tw: number, cr: number) => {
    giant(crowns, x, GROUND_Y, h, tw, cr, r, false, false);
    trunks.push({ x, z: TRUNKS_Z, h, tw });
  };
  add(-6.8, 30, 1.6, 6);
  add(6.8, 30, 1.6, 6);
  for (let k = 0; k < 14; k++) {
    const x = (k % 2 ? 1 : -1) * (16 + Math.floor(k / 2) * 9 + r() * 4);
    const h = 24 + r() * 12;
    const tw = 1.2 + r() * 0.8;
    add(x, h, tw, 5 + r() * 2);
  }
  for (let k = 0; k < 9; k++) crowns.circles.push([-12 + k * 3, 21 + r() * 3, 3.5 + r() * 1.5]);
  return { crowns, trunks };
}

/** Os 16 troncos do portal de troncos (os dois primeiros emolduram o caminho). */
export function trunkPlacements(): TrunkPlacement[] {
  return trunkLayer().trunks;
}

/** A floresta do palco: da camada mais distante (montanhas) ate o dossel que a camera sobrevoa. */
export function forestLayers(): ForestLayer[] {
  const B = GROUND_Y;
  const layers: ForestLayer[] = [];

  let r = rng(101);
  let sh = shapes();
  let wi = shapes();
  band(sh, -520, 520, B, 18, 62, r, 26);
  for (let k = 0; k < 9; k++)
    tower(sh, wi, -320 + k * 80 + r() * 30, B, 50 + r() * 40, 3 + r() * 2, r, false);
  layers.push(layer("montanhas", -260, FOREST.mount, sh, FOREST.windowMount, wi));

  r = rng(102);
  sh = shapes();
  wi = shapes();
  band(sh, -360, 360, B, 6, 14, r, 14);
  for (let k = 0; k < 22; k++) {
    tower(sh, wi, -150 + k * 14 + (r() - 0.5) * 6, B, 18 + r() * 36, 3 + r() * 3, r, false);
  }
  for (let k = 0; k < 40; k++) pine(sh, -340 + k * 17 + r() * 8, B, 10 + r() * 9, 5 + r() * 3);
  layers.push(layer("cidade", -170, FOREST.city, sh, FOREST.window, wi));

  r = rng(103);
  sh = shapes();
  band(sh, -300, 300, B, 3, 8, r, 10);
  for (let k = 0; k < 7; k++) {
    let gx = -180 + k * 60 + (r() - 0.5) * 16;
    if (Math.abs(gx) < 30) gx += gx < 0 ? -30 : 30;
    giant(sh, gx, B, 34 + r() * 18, 2 + r() * 1.2, 8 + r() * 3, r, false);
  }
  layers.push(layer("gigantes", -105, FOREST.giants, sh));

  r = rng(104);
  sh = shapes();
  band(sh, -200, 200, B, 2, 5, r, 8);
  for (let k = 0; k < 9; k++) {
    let gx = -128 + k * 32 + (r() - 0.5) * 8;
    if (Math.abs(gx) < 22) gx += gx < 0 ? -22 : 22;
    giant(sh, gx, B, 26 + r() * 14, 1.8 + r() * 1.2, 6.5 + r() * 3, r, false);
  }
  layers.push(layer("anel", -55, FOREST.ring, sh));

  // muro de ruinas com arcos e musgo, logo atras da clareira
  r = rng(105);
  sh = shapes();
  wi = shapes();
  const wall: Pt[] = [[-46, B]];
  for (let wx = -46; wx <= 46; wx += 2) {
    wall.push([wx, B + 5 + r() * 3 - (Math.abs(wx) < 10 ? 2 : 0)]);
  }
  wall.push([46, B]);
  sh.polys.push(wall);
  for (let k = -4; k <= 4; k++) {
    sh.polys.push(rectPoly(k * 9 - 0.7, B, 1.4, 9 + r() * 4));
    sh.polys.push([
      [k * 9 - 1.1, B + 9],
      [k * 9 + 1.1, B + 9],
      [k * 9, B + 10.8],
    ]);
  }
  for (let k = 0; k < 10; k++) {
    giant(
      sh,
      (k < 5 ? -1 : 1) * (16 + (k % 5) * 9 + r() * 4),
      B,
      24 + r() * 14,
      1.2 + r(),
      6 + r() * 3,
      r,
      false,
    );
  }
  for (let k = 0; k < 26; k++)
    wi.circles.push([-44 + k * 3.5 + r(), B + 5.5 + r() * 2, 0.8 + r() * 0.9]);
  layers.push(layer("muro-de-ruinas", RUIN_WALL_Z, FOREST.ruin, sh, FOREST.moss, wi));

  // o caminho ate a clareira: portal de troncos, ruinas, corredor e dossel
  layers.push(layer("portal-de-troncos", TRUNKS_Z, FOREST.portal, trunkLayer().crowns));

  // o portal de pedra (ARCH_Z) e o modelo design/3d/portal.py, nao uma silhueta (T-L05, AC-L05b)

  r = rng(108);
  sh = shapes();
  for (let k = 0; k < 12; k++) {
    const x = (k % 2 ? 1 : -1) * (7 + Math.floor(k / 2) * 8 + r() * 3);
    if (r() < 0.5) pine(sh, x, B, 14 + r() * 10, 6 + r() * 3);
    else
      giant(sh, x + (x > 0 ? 3 : -3), B, 20 + r() * 10, 1 + r() * 0.6, 3.5 + r() * 1.5, r, false);
  }
  sh.polys.push(
    rectPoly(-6.2, B, 1.2, 4.5),
    rectPoly(5.2, B, 1.2, 3.2),
    rectPoly(-6.6, B + 4.5, 2, 0.5),
  );
  layers.push(layer("ruinas-do-caminho", 58, FOREST.ruins2, sh));

  r = rng(109);
  sh = shapes();
  for (let k = 0; k < 16; k++) {
    giant(
      sh,
      (k % 2 ? 1 : -1) * (12 + Math.floor(k / 2) * 9 + r() * 3),
      B,
      16 + r() * 8,
      1 + r() * 0.6,
      3.6,
      r,
      false,
    );
  }
  for (let k = 0; k < 10; k++)
    pine(sh, (k % 2 ? 1 : -1) * (9 + r() * 40), B, 9 + r() * 5, 4 + r() * 2);
  layers.push(layer("corredor", 80, FOREST.corridor, sh));

  r = rng(110);
  sh = shapes();
  band(sh, -260, 260, B, 3, 5, r, 6);
  for (let k = 0; k < 70; k++) pine(sh, -250 + k * 7.3 + r() * 3, B, 9 + r() * 4.5, 5 + r() * 2);
  layers.push(layer("dossel-fundo", 100, FOREST.canopyB, sh));

  r = rng(111);
  sh = shapes();
  band(sh, -260, 260, B, 4, 6, r, 5);
  for (let k = 0; k < 80; k++) pine(sh, -250 + k * 6.4 + r() * 3, B, 10 + r() * 4, 5 + r() * 2);
  layers.push(layer("dossel-frente", 128, FOREST.canopyA, sh));

  return layers;
}

/** Profundidade do muro de ruinas que esconde a origem dos tokens vindos de tras. */
export const RUIN_WALL_Z = -24;
/** Profundidade do portal de pedra (portal.glb): as runas do arco ficam na frente dele. */
export const ARCH_Z = 42;

/** Aduela do portal.glb (design/3d/portal.py): nascenca, meio vao e flecha do vao, largura e ponta ogival. */
const ARCH = { spring: 5.4, half: 3.4, rise: 3.3, band: 1.5, point: 0.6 } as const;

export interface RunePlacement {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

/** As nove runas do arco do portal, da esquerda para a direita. */
export function archRunePlacements(): RunePlacement[] {
  const rx = ARCH.half + ARCH.band / 2;
  const ry = ARCH.rise + ARCH.band / 2;
  return Array.from({ length: 9 }, (_, k) => {
    const a = Math.PI * (0.12 + (k * 0.76) / 8);
    const t = Math.min(a, Math.PI - a) / (Math.PI / 2); // 0 na nascenca, 1 no fecho (a curva do modelo)
    return {
      x: -Math.cos(a) * rx,
      y: ARCH.spring + Math.sin(a) * ry + ARCH.point * t ** 4,
      z: ARCH_Z + 0.42,
    };
  });
}

/** As dezoito runas do caminho, alternando os lados. */
export function pathRunePlacements(): RunePlacement[] {
  return Array.from({ length: 18 }, (_, k) => ({
    x: k % 2 ? 0.8 : -0.8,
    y: -0.38,
    z: 6 + k * 3.4,
  }));
}

export interface FernPlacement {
  readonly x: number;
  readonly z: number;
  readonly scale: number;
  readonly phase: number;
  readonly leaves: Pt[][];
}

/** Samambaias ao longo do caminho (as folhas balancam no palco). */
export function fernPlacements(): FernPlacement[] {
  const r = rng(120);
  const out: FernPlacement[] = [];
  for (let k = 0; k < 34; k++) {
    const leaves: Pt[][] = [];
    for (let lf = 0; lf < 5; lf++) {
      const la = -0.9 + lf * 0.45;
      leaves.push([
        [0, 0],
        [Math.sin(la) * 0.25 - 0.05, Math.cos(la) * 0.9],
        [Math.sin(la) * 0.9, Math.cos(la) * 1.4],
        [Math.sin(la) * 0.25 + 0.05, Math.cos(la) * 0.9],
      ]);
    }
    let x = (k % 2 ? 1 : -1) * (2.0 + r() * 2.5);
    const z = 6 + r() * 58;
    const scale = 0.7 + r() * 0.9;
    // nenhuma no corredor do pan final (a esquerda do caminho, perto do portal, diante do mural de avisos)
    if (z > 45 && x < 0) x = -x;
    out.push({ x, z, scale, phase: r() * 6.28, leaves });
  }
  return out;
}

// ---------------------------------------------------------------------------------------------------------------
// Imagem estatica da clareira (fallback): as camadas do hero 2,5D, vistas da ultima pose da camera.
// ---------------------------------------------------------------------------------------------------------------

export interface HeroLayer {
  readonly id: string;
  /** Distancia da camera de partida. */
  readonly z: number;
  /** Meia-largura e altura da camada, em unidades do mundo. */
  readonly W: number;
  readonly H: number;
  readonly color: string;
  readonly body: Shapes;
  readonly accentColor?: string;
  readonly accents?: Shapes;
  /** Circulos de musgo, na cor de musgo. */
  readonly moss?: Circle[];
  readonly mossColor?: string;
}

/** As camadas do hero; so as que ficam diante da camera final aparecem na imagem. */
export function heroLayers(): HeroLayer[] {
  const B = 0;
  const out: HeroLayer[] = [];
  const push = (
    id: string,
    z: number,
    W: number,
    H: number,
    color: string,
    body: Shapes,
    accents?: Shapes,
    accentColor?: string,
    moss?: Circle[],
  ) => out.push({ id, z, W, H, color, body, accents, accentColor, moss, mossColor: FOREST.moss });

  let r = rng(101);
  let o = shapes();
  let win = shapes();
  band(o, -2600, 2600, B, 120, 360, r, 140);
  for (let i = 0; i < 14; i++)
    tower(o, win, -1500 + i * 230 + r() * 80, B, 240 + r() * 260, 16 + r() * 10, r, true);
  push("montanhas", 2000, 2600, 1300, FOREST.mount, o, win, FOREST.windowMount);

  r = rng(102);
  o = shapes();
  win = shapes();
  band(o, -1100, 1100, B, 30, 60, r, 40);
  const tx: Array<[number, number]> = [];
  for (let i = 0; i < 24; i++) {
    const cx = -560 + i * 48 + (r() - 0.5) * 18;
    const chh = 70 + r() * 130;
    tx.push([cx, chh]);
    tower(o, win, cx, B, chh, 10 + r() * 9, r, true);
  }
  for (let i = 0; i < 22; i += 3)
    bridge(o, tx[i][0], tx[i + 1][0], Math.min(tx[i][1], tx[i + 1][1]) * 0.62, 3, 9);
  for (let i = 0; i < 6; i++) dome(o, -460 + i * 170 + r() * 40, B, 30 + r() * 18, 22 + r() * 18);
  for (let i = 0; i < 60; i++) pine(o, -1080 + i * 36 + r() * 16, B, 34 + r() * 28, 18 + r() * 8);
  push("cidade", 900, 1100, 600, FOREST.city, o, win, FOREST.window);

  r = rng(103);
  o = shapes();
  win = shapes();
  band(o, -620, 620, B, 10, 22, r, 20);
  for (let i = 0; i < 8; i++) {
    let gx = -520 + i * 150 + (r() - 0.5) * 40;
    if (Math.abs(gx) < 90) gx += gx < 0 ? -90 : 90;
    giant(o, gx, B, 95 + r() * 55, 5 + r() * 3, 20 + r() * 8, r, true);
  }
  for (let i = 0; i < 4; i++)
    tower(o, win, -260 + i * 170 + r() * 30, B, 55 + r() * 40, 11, r, true);
  push("gigantes", 480, 620, 330, FOREST.giants, o, win, FOREST.windowGiant);

  r = rng(104);
  o = shapes();
  band(o, -380, 380, B, 6, 14, r, 14);
  for (let i = 0; i < 10; i++) {
    let gx = -330 + i * 70 + (r() - 0.5) * 16;
    if (Math.abs(gx) < 40) gx += gx < 0 ? -40 : 40;
    giant(o, gx, B, 55 + r() * 30, 3.5 + r() * 2.5, 13 + r() * 6, r, true);
  }
  push("anel", 320, 380, 220, FOREST.ring, o);

  r = rng(105);
  o = shapes();
  const wallPts: Pt[] = [[-90, B]];
  for (let wx = -90; wx <= 90; wx += 3)
    wallPts.push([wx, B + 7 + r() * 4 - (Math.abs(wx) < 14 ? 3 : 0)]);
  wallPts.push([90, B]);
  o.polys.push(wallPts);
  for (let i = -5; i <= 5; i++) {
    const px = i * 14;
    o.polys.push(rectPoly(px - 1.1, B, 2.2, 13 + r() * 6));
    o.polys.push([
      [px - 1.7, B + 13],
      [px + 1.7, B + 13],
      [px, B + 15.5],
    ]);
  }
  for (let i = 0; i < 14; i++) {
    const sx = (i < 7 ? -1 : 1) * (26 + (i % 7) * 14 + r() * 6);
    giant(o, sx, B, 40 + r() * 22, 2 + r() * 1.4, 9 + r() * 4, r, true);
  }
  const moss: Circle[] = [];
  for (let i = 0; i < 40; i++) moss.push([-88 + i * 4.4 + r(), B + 7.5 + r() * 3, 1 + r() * 1.3]);
  push("muro-de-ruinas", 262, 240, 100, FOREST.ruin, o, undefined, undefined, moss);

  return out;
}

/** A pose da camera da imagem estatica: o ultimo quadro do sobrevoo, diante da clareira. */
export const STATIC_CAMERA = { x: 0, y: 1.75, z: 214 } as const;
/** Centro da clareira e da deck box na imagem estatica. */
export const CLEARING_Z = 236;
