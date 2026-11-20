// O fogo da clareira (REQ-L10) como funcoes puras: onde ficam as velas e as tochas, a tremulacao das chamas e das
// luzes, a reacao ao despertar da caixa e a explosao do deck, e as brasas. A cena so aplica os numeros (sem three).
import { type Vec3, clamp, lerp, rng, smooth } from "./math";
import { BOXP, PH } from "./phases";
import type { Token } from "./tokens";

/** Raio do anel de runas entalhado no altar: as velas ficam logo depois dele, sem cobrir as inscricoes. */
export const RUNE_RING_RADIUS = 4.62;
/** No maximo oito velas, em pequenos grupos (angulo do grupo em graus, quantas velas). */
const CANDLE_GROUPS: ReadonlyArray<readonly [number, number]> = [
  [0, 2],
  [180, 2],
  [30, 1],
  [150, 1],
  [-28, 1],
  [-152, 1],
];
export const CANDLE_MAX = CANDLE_GROUPS.reduce((n, g) => n + g[1], 0);

export interface Candle {
  readonly x: number;
  readonly z: number;
  /** Raio e altura da vela. */
  readonly r: number;
  readonly h: number;
  /** Indice do grupo. */
  readonly group: number;
}

/** Topo da cera do castical (candlestick.glb) na escala 1, em metros. */
export const CANDLE_WAX_TOP = 0.21;

/** Escala do castical de cada vela: a altura sorteada (0,16 a 0,58 m) vira um castical de 0,26 a 0,68 m. */
export function candleScale(c: Candle): number {
  return (c.h + 0.1) / CANDLE_WAX_TOP;
}

/** Algum token passa perto do chao (y < 0,6) a menos de `rad` deste ponto? */
function clearOfTokens(tokens: readonly Token[], x: number, z: number, rad: number): boolean {
  for (const k of tokens) {
    const tr = k.track;
    for (let s = 0; s < tr.length; s += 36) {
      if (tr[s + 2] < 0.6 && Math.hypot(tr[s] - x, tr[s + 1] - z) < k.R + rad + 0.15) return false;
    }
  }
  return true;
}

/** Velas em grupos irregulares em volta do anel de runas, longe da trajetoria dos tokens (ja simulados). */
export function placeCandles(tokens: readonly Token[]): Candle[] {
  const r = rng(160);
  const out: Candle[] = [];
  CANDLE_GROUPS.forEach(([deg, count], group) => {
    for (let q = 0; q < count * 3 && out.filter((o) => o.group === group).length < count; q++) {
      const rad = RUNE_RING_RADIUS + r() * 0.8;
      const a = (deg * Math.PI) / 180 + (r() - 0.5) * 0.24;
      const x = Math.cos(a) * rad;
      const z = Math.sin(a) * rad;
      const cr = 0.045 + r() * 0.04;
      const ch = 0.16 + r() * 0.42;
      if (Math.abs(x) > 6.3 || Math.abs(z) > 4.45 || !clearOfTokens(tokens, x, z, cr * 2.4))
        continue;
      if (out.some((o) => Math.hypot(o.x - x, o.z - z) < (o.r + cr) * 2.4)) continue;
      out.push({ x, z, r: cr, h: ch, group });
    }
  });
  return out;
}

export interface StandTorch {
  readonly x: number;
  readonly z: number;
  readonly seed: number;
  /** As duas de tras projetam sombra (so fora do celular). */
  readonly shadow: boolean;
}

/** Tochas em suportes de ferro na clareira, afastadas da laje do altar. */
export const STAND_TORCHES: readonly StandTorch[] = [
  { x: -7.4, z: -6.6, seed: 1.1, shadow: true },
  { x: 7.4, z: -6.6, seed: 2.3, shadow: true },
  { x: -9.2, z: 5.6, seed: 3.7, shadow: false },
  { x: 9.2, z: 5.6, seed: 4.9, shadow: false },
];

/** Tochas de parede no portal de pedra: posicao do suporte (na face das ombreiras do portal.glb) e semente. */
export const PORTAL_TORCHES: ReadonlyArray<{ readonly at: Vec3; readonly seed: number }> = [
  { at: [-4.5, 3.6, 42.26], seed: 5.5 },
  { at: [4.5, 3.6, 42.26], seed: 6.6 },
];

/** Tochas distantes no muro de ruinas: so chama e brilho (profundidade). */
export const RUIN_TORCHES: ReadonlyArray<{
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly seed: number;
}> = [-27, -9, 9, 27].map((x, k) => ({ x, y: 4.4 + (k % 2) * 0.5, z: -23.6, seed: 20 + k * 3.3 }));

/** Sombra de luz pontual so fora do celular (custo de GPU). */
export function fireShadows(wanted: boolean, mobile: boolean): boolean {
  return wanted && !mobile;
}

/** Tremulacao (perto de 1) do fogo de semente `seed` no tempo `t` (s). */
export function flicker(t: number, seed: number): number {
  return (
    1 +
    0.08 * Math.sin(t * 9.1 + seed) +
    0.05 * Math.sin(t * 23.3 + seed * 2) +
    0.04 * Math.sin(t * 41.7 + seed * 3)
  );
}

/** As velas crescem enquanto as runas da caixa despertam (0 fora, 1 no meio). */
export function candleWake(raw: number): number {
  return Math.sin(Math.PI * clamp((raw - PH.forest) / (PH.awake - PH.forest), 0, 1));
}

export interface Flame {
  readonly x: number;
  readonly z: number;
  /** Largura e altura da chama. */
  readonly w: number;
  readonly h: number;
  readonly seed: number;
  readonly candle: boolean;
}

export interface FlameFrame {
  /** Giro em Y para encarar a camera (billboard so no eixo Y). */
  readonly yaw: number;
  readonly scaleY: number;
  /** Inclinacao do topo da chama (vento e explosao). */
  readonly lean: number;
  /** Brilho da chama (multiplica a cor no shader). */
  readonly hot: number;
  /** Fator da opacidade do halo. */
  readonly halo: number;
}

export function flameFrame(
  f: Flame,
  cam: readonly [number, number],
  raw: number,
  t: number,
  burst: number,
): FlameFrame {
  const fl = flicker(t, f.seed);
  const yaw = Math.atan2(cam[0] - f.x, cam[1] - f.z);
  const boost = f.candle ? 1 + 0.35 * candleWake(raw) : 1;
  const wx = f.x - BOXP[0];
  const wz = f.z - BOXP[2];
  const wd = Math.hypot(wx, wz) || 1;
  const gust = burst * 2.2 * Math.exp(-wd / 7);
  const lean =
    ((wx / wd) * Math.cos(yaw) - (wz / wd) * Math.sin(yaw)) * gust * f.h +
    Math.sin(t * 0.7 + f.seed) * 0.12 * f.w;
  return {
    yaw,
    scaleY: fl * boost * (1 - 0.3 * burst),
    lean,
    hot: (0.92 + (fl - 1) * 1.5) * boost,
    halo: (0.9 + (fl - 1) * 2.2) * boost,
  };
}

export interface FireLight {
  readonly p: Vec3;
  /** Intensidade base. */
  readonly i0: number;
  readonly seed: number;
  readonly kind: "candle" | "torch";
}

/** A luz pontual do fogo tremula com a chama e balanca alguns centimetros. */
export function lightFrame(L: FireLight, raw: number, t: number): { intensity: number; pos: Vec3 } {
  const s = L.seed;
  const fl = flicker(t, s);
  return {
    intensity:
      L.i0 * (0.9 + (fl - 1) * 1.2) * (L.kind === "candle" ? 1 + 0.35 * candleWake(raw) : 1),
    pos: [
      L.p[0] + Math.sin(t * 7.3 + s) * 0.04,
      L.p[1] + Math.sin(t * 11.1 + s) * 0.03,
      L.p[2] + Math.cos(t * 6.1 + s) * 0.04,
    ],
  };
}

/** A luz frontal da cena (noite) sobe so enquanto as cartas dos ganchos viram, para o texto delas ficar legivel. */
export function frontLightIntensity(raw: number): number {
  return lerp(0.4, 0.9, smooth((raw - PH.intro) / 0.02) * (1 - smooth((raw - PH.story1) / 0.02)));
}

/** Uma brasa: sobe da chama `src` em ciclo continuo e apaga ao subir. */
export function emberPoint(
  seed: readonly [number, number, number],
  src: Vec3,
  t: number,
): { pos: Vec3; fade: number } {
  const pr = (((t * 0.42 * (0.6 + seed[0]) + seed[1]) % 1) + 1) % 1;
  return {
    pos: [
      src[0] + Math.sin(pr * 9 + seed[2] * 6) * 0.2 * pr + pr * 0.25,
      src[1] + pr * 1.9,
      src[2] + Math.cos(pr * 7 + seed[2] * 6) * 0.2 * pr,
    ],
    fade: Math.pow(1 - pr, 1.6),
  };
}
