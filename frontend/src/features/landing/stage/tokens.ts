// Tokens da mesa (REQ-L10, AC-L02k): cinco medalhoes com o logo dos jogos e fichas de mana caem no altar.
// A fisica e simulada uma vez, com passo fixo e semente fixa (deterministica), e depois so lida por tempo.
import { clamp, lerp, rng, smooth } from "./math";
import { MANA } from "./palette";
import { mesaProgress } from "./phases";

export const SIM_T = 7;
export const DT = 1 / 120;
const GRAVITY = 12;
/** Obstaculo: a deck box ao centro do altar (circulo no plano do chao). */
export const BOX_CENTER: readonly [number, number] = [0, -0.4];
export const BOX_RADIUS = 1.1;
/** Meia-largura e meia-profundidade livres do altar (as bordas rebatem). */
const SLAB_HALF_X = 5.6;
const SLAB_HALF_Z = 3.9;

export type Behavior = "bounce" | "spin" | "slide" | "tumble";
export type GameId = "magic" | "pokemon" | "yugioh" | "onepiece" | "lorcana";
const GAME_ORDER: readonly GameId[] = ["magic", "pokemon", "yugioh", "onepiece", "lorcana"];

export interface ManaChip {
  /** Glifo da fonte de mana. */
  readonly glyph: string;
  /** Cor da ficha. */
  readonly color: string;
}

/** As cinco cores de mana da fonte local (mana.woff2). */
export const MANA_CHIPS: readonly ManaChip[] = [
  { glyph: "\ue600", color: MANA[0] },
  { glyph: "\ue601", color: MANA[1] },
  { glyph: "\ue602", color: MANA[2] },
  { glyph: "\ue603", color: MANA[3] },
  { glyph: "\ue604", color: MANA[4] },
];

export interface Token {
  readonly i: number;
  readonly kind: "medal" | "mana";
  readonly game: GameId | null;
  readonly mana: ManaChip | null;
  /** Raio e espessura do disco. */
  readonly R: number;
  readonly H: number;
  readonly behav: Behavior;
  /** Origem do voo, ponto de pouso, instante e duracao da entrada, altura do arco. */
  readonly org: readonly [number, number, number];
  readonly L: readonly [number, number];
  readonly t0: number;
  readonly dur: number;
  readonly arc: number;
  /** Velocidade horizontal e vertical ao pousar. */
  readonly v: readonly [number, number];
  readonly vy: number;
  readonly w: number;
  readonly tilt0: number;
  readonly phi0: number;
  readonly yaw0: number;
  /** Giro extra do voo, por eixo (rad), que se desfaz ate o pouso. */
  readonly fr: readonly [number, number, number];
  readonly mu: number;
  readonly mass: number;
  /** x, z, y, yaw, inclinacao, fase: 6 floats por passo da simulacao. */
  track: Float32Array;
}

const BEHAVIORS: readonly Behavior[] = ["bounce", "spin", "slide", "tumble"];
const ORIGINS = [
  "left",
  "back",
  "front",
  "right",
  "back",
  "front",
  "left",
  "right",
  "back",
  "front",
  "left",
] as const;
const SPOTS: ReadonlyArray<readonly [number, number]> = [
  [-2.6, -0.6],
  [0.5, 2.55],
  [2.7, -0.4],
  [-1.0, 1.4],
  [1.5, 1.3],
  [-3.6, 1.5],
  [3.8, 1.8],
  [-1.8, 0.2],
  [1.7, -1.0],
  [-2.0, -2.3],
  [2.6, -2.5],
];
const SPEED: Record<Behavior, number> = { slide: 2.8, bounce: 0.9, spin: 0.45, tumble: 1.3 };
const LIFT: Record<Behavior, number> = { bounce: 3.4, spin: 1.0, slide: 0.7, tumble: 1.2 };

/**
 * Plano deterministico: 5 medalhoes (um por jogo) e 6 fichas de mana, em instantes e origens distintos. `mesaK` e o
 * recuo da camera da mesa (phases.mesaK): quando ela recua muito (celular), os de tras nascem colados atras do muro
 * de ruinas e os da frente sobem e vao para tras da camera, para continuarem entrando de fora do quadro.
 */
export function planTokens(mesaK = 1): Token[] {
  const far = mesaK > 2;
  const r = rng(2026);
  const list: Token[] = [];
  for (let i = 0; i < 11; i++) {
    const medal = i < 5;
    const R = medal ? 0.62 : 0.34;
    const H = medal ? 0.14 : 0.09;
    const behav = BEHAVIORS[(i * 3 + (i > 5 ? 1 : 0)) % 4];
    const o = ORIGINS[i];
    const L: [number, number] = [SPOTS[i][0] + (r() - 0.5) * 0.4, SPOTS[i][1] + (r() - 0.5) * 0.3];
    const org: [number, number, number] =
      o === "left"
        ? [-13, 3 + r() * 2, L[1] + (r() - 0.5) * 4]
        : o === "right"
          ? [13, 3 + r() * 2, L[1] + (r() - 0.5) * 4]
          : o === "back"
            ? far
              ? [L[0] + (r() - 0.5) * 6, 0.2 + r() * 0.3, -24.6]
              : [L[0] + (r() - 0.5) * 6, 1 + r() * 0.5, -40]
            : [
                L[0] * 0.4 + (r() - 0.5) * 1.5,
                6.5 + r() + (far ? 6 * mesaK - 3.5 : 0),
                13 + (far ? 6 * mesaK - 13 : 0),
              ];
    const dx = L[0] - org[0];
    const dz = L[1] - org[2];
    const dl = Math.hypot(dx, dz) || 1;
    const spd = SPEED[behav] * (0.85 + r() * 0.3);
    list.push({
      i,
      kind: medal ? "medal" : "mana",
      game: medal ? GAME_ORDER[i] : null,
      mana: medal ? null : MANA_CHIPS[(i - 5) % 5],
      R,
      H,
      behav,
      org,
      L,
      t0: 0.15 + i * 0.27 + r() * 0.12,
      dur: 0.75 + r() * 0.5,
      arc: (o === "back" ? 6 : 1.5) + r() * 2.5,
      v: [(dx / dl) * spd, (dz / dl) * spd],
      vy: LIFT[behav],
      w: behav === "spin" ? 15 + r() * 4 : 2 + r() * 3,
      tilt0: behav === "tumble" ? 1.0 : 0.18 + r() * 0.12,
      phi0: r() * 6.28,
      yaw0: r() * 6.28,
      fr: [(r() - 0.5) * 18, (r() - 0.5) * 14, (r() - 0.5) * 18],
      mu: behav === "slide" ? 0.32 : 0.7,
      mass: R * R,
      track: new Float32Array(0),
    });
  }
  return list;
}

interface SimState {
  on: boolean;
  x: number;
  z: number;
  y: number;
  vx: number;
  vz: number;
  vy: number;
  yaw: number;
  w: number;
  th: number;
  phi: number;
}

/** Simula todos os tokens por SIM_T segundos e grava a trilha de cada um (passo fixo, deterministico). */
export function simulateTokens(tokens: Token[]): void {
  const steps = Math.ceil(SIM_T / DT) + 1;
  const states: SimState[] = tokens.map((k) => {
    k.track = new Float32Array(steps * 6);
    return {
      on: false,
      x: k.L[0],
      z: k.L[1],
      y: 0,
      vx: k.v[0],
      vz: k.v[1],
      vy: k.vy,
      yaw: k.yaw0,
      w: k.w,
      th: k.tilt0,
      phi: k.phi0,
    };
  });

  for (let step = 0; step < steps; step++) {
    const tau = step * DT;
    tokens.forEach((k, n) => {
      if (!states[n].on && tau >= k.t0 + k.dur) states[n].on = true;
    });
    const active = tokens.map((k, n) => ({ k, q: states[n] })).filter(({ q }) => q.on);
    for (const { k, q } of active) advance(k, q);
    for (let a = 0; a < active.length; a++) {
      for (let b = a + 1; b < active.length; b++) collide(active[a], active[b]);
    }
    tokens.forEach((k, n) => {
      const q = states[n];
      k.track.set([q.x, q.z, q.y, q.yaw, q.th, q.phi], step * 6);
    });
  }
}

function advance(k: Token, q: SimState): void {
  const speed = Math.hypot(q.vx, q.vz);
  const decay = k.mu * GRAVITY * DT * (q.y > 0.01 ? 0.15 : 1);
  if (speed > 0) {
    const next = Math.max(0, speed - decay);
    q.vx *= next / speed;
    q.vz *= next / speed;
  }
  q.x += q.vx * DT;
  q.z += q.vz * DT;
  if (Math.abs(q.x) > SLAB_HALF_X - k.R) {
    q.x = Math.sign(q.x) * (SLAB_HALF_X - k.R);
    q.vx *= -0.3;
  }
  if (Math.abs(q.z) > SLAB_HALF_Z - k.R) {
    q.z = Math.sign(q.z) * (SLAB_HALF_Z - k.R);
    q.vz *= -0.3;
  }
  // a deck box e um obstaculo circular
  const bx = q.x - BOX_CENTER[0];
  const bz = q.z - BOX_CENTER[1];
  const bd = Math.hypot(bx, bz);
  const min = k.R + BOX_RADIUS;
  if (bd < min && bd > 1e-6) {
    const nx = bx / bd;
    const nz = bz / bd;
    q.x = BOX_CENTER[0] + nx * min;
    q.z = BOX_CENTER[1] + nz * min;
    const vn = q.vx * nx + q.vz * nz;
    if (vn < 0) {
      q.vx -= 1.35 * vn * nx;
      q.vz -= 1.35 * vn * nz;
    }
  }
  q.vy -= GRAVITY * DT;
  q.y += q.vy * DT;
  if (q.y < 0) {
    q.y = 0;
    q.vy = Math.abs(q.vy) > 0.7 ? -q.vy * 0.42 : 0;
    if (q.vy) q.th = Math.min(q.th + 0.12, 1.1);
  }
  q.yaw += q.w * DT;
  q.w *= Math.exp(-(k.behav === "spin" ? 0.9 : 1.8) * DT);
  q.th *= Math.exp(-(k.behav === "tumble" ? 1.1 : 2.6) * DT);
  q.phi += (2.5 + 10 * (1 - q.th / Math.max(k.tilt0, 0.01))) * q.th * 4 * DT;
}

interface Body {
  k: Token;
  q: SimState;
}

function collide(a: Body, b: Body): void {
  const dx = b.q.x - a.q.x;
  const dz = b.q.z - a.q.z;
  const d = Math.hypot(dx, dz);
  const min = a.k.R + b.k.R + 0.04;
  if (d >= min || d <= 1e-6) return;
  const nx = dx / d;
  const nz = dz / d;
  const ma = a.k.mass;
  const mb = b.k.mass;
  const pen = min - d;
  a.q.x -= (nx * pen * mb) / (ma + mb);
  a.q.z -= (nz * pen * mb) / (ma + mb);
  b.q.x += (nx * pen * ma) / (ma + mb);
  b.q.z += (nz * pen * ma) / (ma + mb);
  const rv = (b.q.vx - a.q.vx) * nx + (b.q.vz - a.q.vz) * nz;
  if (rv < 0) {
    const j = (-(1 + 0.35) * rv) / (1 / ma + 1 / mb);
    a.q.vx -= (j * nx) / ma;
    a.q.vz -= (j * nz) / ma;
    b.q.vx += (j * nx) / mb;
    b.q.vz += (j * nz) / mb;
    a.q.w += j * 3;
    b.q.w -= j * 3;
  }
}

/** Le a trilha em `tau` (s): [x, z, y, yaw, inclinacao, fase], interpolada entre passos. */
export function sampleToken(k: Token, tau: number, out: Float32Array): Float32Array {
  const f = clamp(tau / DT, 0, k.track.length / 6 - 1.001);
  const i = Math.floor(f);
  const u = f - i;
  const a = i * 6;
  for (let j = 0; j < 6; j++) out[j] = lerp(k.track[a + j], k.track[a + 6 + j], u);
  return out;
}

/** Relogio dos tokens (s) a partir do progresso `raw` do palco. */
export function tokenTime(raw: number): number {
  return clamp((mesaProgress(raw) - 0.3) / 0.62, 0, 1) * SIM_T;
}

export interface TokenPose {
  readonly visible: boolean;
  /** Escala do token: cresce do zero no comeco do voo de entrada (nada surge de repente). */
  readonly scale: number;
  /** Centro do disco no mundo. */
  readonly pos: readonly [number, number, number];
  readonly yaw: number;
  /** Inclinacao do disco e a fase do eixo dela. */
  readonly tilt: number;
  readonly phi: number;
  /** Giro extra do voo (euler); zero depois do pouso. */
  readonly spin: readonly [number, number, number];
}

const scratch = new Float32Array(6);

export function tokenPose(k: Token, tau: number): TokenPose {
  if (tau < k.t0) {
    return { visible: false, scale: 0, pos: k.org, yaw: 0, tilt: 0, phi: 0, spin: [0, 0, 0] };
  }
  if (tau < k.t0 + k.dur) {
    const u = (tau - k.t0) / k.dur;
    return {
      visible: true,
      scale: smooth(u / 0.3),
      pos: [
        lerp(k.org[0], k.L[0], u),
        lerp(k.org[1], (k.H / 2) * Math.cos(k.tilt0) + k.R * Math.sin(k.tilt0), u) +
          4 * k.arc * u * (1 - u),
        lerp(k.org[2], k.L[1], u),
      ],
      yaw: k.yaw0,
      tilt: k.tilt0,
      phi: k.phi0,
      spin: [k.fr[0] * (1 - u), k.fr[1] * (1 - u), k.fr[2] * (1 - u)],
    };
  }
  const s = sampleToken(k, tau, scratch);
  return {
    visible: true,
    scale: 1,
    pos: [s[0], (k.H / 2) * Math.cos(s[4]) + k.R * Math.sin(s[4]) + s[2], s[1]],
    yaw: s[3],
    tilt: s[4],
    phi: s[5],
    spin: [0, 0, 0],
  };
}
