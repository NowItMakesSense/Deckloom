// O baralho (REQ-L10, AC-L02e): posicao das cartas na caixa, a subida ate o deck em destaque, as quatro cartas
// dos ganchos e a explosao, tudo como funcao pura do progresso do scroll.
import { explodedOffset, planExplosion } from "./explosion";
import { type Vec3, clamp, eulerToQuat, lerp, quatToEuler, slerp, smooth, trackAt } from "./math";
import { type BoxPhases, BOXP, DY, PH, SEGL, boxPhases, mesaProgress } from "./phases";

/** Cartas do baralho: as quatro dos ganchos e o resto de costas. */
/** 24 cartas: o baralho cabe de verdade na deck box (44 virava um bloco maior que a propria caixa). */
export const CARD_COUNT = 24;
export const HOOK_COUNT = 4;

export interface RestPose {
  readonly p: Vec3;
  readonly r: Vec3;
}

export function deckPose(i: number): RestPose {
  return {
    p: [(((i * 37) % 7) - 3) * 0.004, 2.1 + DY + i * 0.002, -i * 0.018],
    r: [0, i < HOOK_COUNT ? Math.PI : 0, (((i * 53) % 9) - 4) * 0.004],
  };
}

/** Espaco entre as cartas em pe dentro da caixa: maior que a espessura (0,016) e cabe no vao de 0,98 da deck box. */
export const BOX_CARD_GAP = 0.02;

export function boxPose(i: number): RestPose {
  return {
    // a pilha fica centrada no vao da caixa (centro em BOXP z)
    p: [0, BOXP[1] + 0.08 + 0.88, BOXP[2] + ((CARD_COUNT - 1) / 2 - i) * BOX_CARD_GAP],
    r: [0, i < HOOK_COUNT ? Math.PI : 0, 0],
  };
}

/** Pilha das cartas ja reveladas: a esquerda no desktop, no alto no celular. */
export function pilePose(j: number, mobile: boolean): RestPose {
  return mobile
    ? { p: [-1.7 + j * 0.05, 3.55 + DY - j * 0.03, -0.6 + j * 0.02], r: [0, 0, -0.2 + j * 0.08] }
    : { p: [-2.45 + j * 0.05, 2.0 + DY - j * 0.04, 0.3 + j * 0.02], r: [0, 0, -0.17 + j * 0.07] };
}

export interface HookKey {
  readonly u: number;
  readonly p: Vec3;
  readonly r: Vec3;
}

/** Sair do deck, aproximar, virar 180 graus, revelar e ir para a pilha. */
export function hookKeys(k: number, mobile: boolean): HookKey[] {
  const side = k % 2 ? 1 : -1;
  const d = deckPose(k);
  const pile = pilePose(k, mobile);
  // no celular a carta fica mais longe e mais baixa: entre a barra do topo e o cartao HTML do gancho
  const near: Vec3 = mobile ? [0, 2.75 + DY, 4.6] : [-0.85, 2.45 + DY, 5.6];
  return [
    { u: 0, p: d.p, r: d.r },
    { u: 0.27, p: [side * 0.9, 3.3 + DY, 1.2], r: [-0.28, Math.PI, side * 0.16] },
    // revelacao: a carta para de frente para a camera (a camera tambem para; CAMERA_HOLDS)
    { u: 0.62, p: near, r: [0, 0, 0] },
    { u: 0.85, p: near, r: [0, 0, 0] },
    { u: 1, p: pile.p, r: pile.r },
  ];
}

export interface CardContext {
  readonly t: number;
  readonly mobile: boolean;
  /** Fases ja calculadas neste quadro (evita refazer a conta por carta). */
  readonly phases?: BoxPhases;
}

export interface CardPoseResult {
  readonly pos: Vec3;
  readonly rot: Vec3;
  readonly visible: boolean;
  /** Escala da carta (encolhe no fim da explosao; ver explosion.ts). */
  readonly scale: number;
  /** Brilho que passa pela carta quando o gancho e revelado (so as quatro dos ganchos). */
  readonly sheen?: { readonly offset: number; readonly opacity: number };
}

/** Rotacao entre as chaves por quaternion (slerp pelo caminho mais curto): a virada de 180 graus sem cambalhota. */
function hookRotation(keys: readonly HookKey[], u: number): Vec3 {
  let i = 0;
  while (i < keys.length - 2 && u > keys[i + 1].u) i++;
  const a = keys[i];
  const b = keys[i + 1];
  const s = clamp((u - a.u) / (b.u - a.u), 0, 1);
  return quatToEuler(slerp(eulerToQuat(a.r), eulerToQuat(b.r), s));
}

const HOOK_KEYS = new Map<string, HookKey[]>();
function cachedHookKeys(k: number, mobile: boolean): HookKey[] {
  const id = `${k}:${mobile}`;
  let keys = HOOK_KEYS.get(id);
  if (!keys) {
    keys = hookKeys(k, mobile);
    HOOK_KEYS.set(id, keys);
  }
  return keys;
}

const BLAST = planExplosion(CARD_COUNT);

export function cardPose(raw: number, i: number, ctx: CardContext): CardPoseResult {
  const ph = ctx.phases ?? boxPhases(raw, ctx.t);
  const { t } = ctx;
  const f = ph.float;
  // o leque do deck abre pouco: continua do tamanho da caixa de onde saiu
  const spread = 1 + 0.5 * f;
  const dp = deckPose(i);
  let bp: Vec3;
  let br: Vec3;
  let sheen: CardPoseResult["sheen"];

  if (i < HOOK_COUNT) {
    const uk = clamp((raw - PH.story0 - i * SEGL) / SEGL, 0, 1);
    const sv = smooth((uk - 0.55) / 0.15);
    sheen = { offset: lerp(0.9, -0.9, sv), opacity: Math.sin(sv * Math.PI) * 0.9 };
  }

  if (raw < PH.rise) {
    const box = boxPose(i);
    const s = smooth((ph.riseU - (i / CARD_COUNT) * 0.45) / 0.55);
    const arc = Math.sin(Math.PI * s);
    bp = [
      lerp(box.p[0], dp.p[0], s) + arc * ((i % 5) - 2) * 0.05,
      lerp(box.p[1], dp.p[1], s) + arc * 0.35,
      lerp(box.p[2], dp.p[2] * spread, s),
    ];
    // so gira no proprio plano (eixo z): tombar para a frente ou para tras cruzaria a carta vizinha
    br = [0, box.r[1], arc * ((i % 3) - 1) * 0.12];
  } else if (i < HOOK_COUNT) {
    const uk = clamp((raw - PH.story0 - i * SEGL) / SEGL, 0, 1);
    const keys = cachedHookKeys(i, ctx.mobile);
    const p = trackAt(
      keys.map((k) => ({ t: k.u, v: k.p })),
      uk,
    );
    bp = uk === 0 ? [p[0], p[1], p[2] * spread] : p;
    br = hookRotation(keys, uk);
  } else {
    bp = [dp.p[0], dp.p[1], dp.p[2] * spread];
    br = dp.r;
  }

  const fl = f * (i < HOOK_COUNT ? 1.4 : 1);
  bp = [
    bp[0] + Math.sin(t * 0.5 + i * 0.7) * 0.012 * fl,
    bp[1] + Math.sin(t * 0.8 + i * 0.41) * 0.03 * fl,
    bp[2],
  ];
  // balanco da flutuacao: o tombo (x) e o giro (y) ficam pequenos para a borda nunca alcancar a carta vizinha
  // (cardCollision.test.ts); o giro no proprio plano (z) pode ser maior
  br = [
    br[0] + Math.sin(t * 0.7 + i * 0.3) * 0.008 * fl,
    br[1] + Math.sin(t * 0.6 + i * 0.23) * 0.008 * fl,
    br[2] + Math.sin(t * 0.45 + i) * 0.03 * fl,
  ];

  const blast = explodedOffset(mesaProgress(raw), BLAST[i]);
  return {
    pos: [bp[0] + blast.offset[0], bp[1] + blast.offset[1], bp[2] + blast.offset[2]],
    rot: [br[0] + blast.rotation[0], br[1] + blast.rotation[1], br[2] + blast.rotation[2]],
    visible: blast.visible,
    scale: blast.scale,
    sheen,
  };
}
