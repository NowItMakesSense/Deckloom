// Continuidade do movimento (revisao da T-L05, 2026-11-14): a camera e as cartas andam sem parar e reacelerar a cada
// chave ("aos trancos"). Mede a velocidade em funcao do progresso do scroll: entre amostras vizinhas ela pode subir
// ou descer devagar, nunca cair a zero no meio de um movimento (fora das pausas declaradas).
import { describe, expect, it } from "vitest";

import { CAMERA_HOLDS, camPose } from "./camera";
import { CARD_COUNT, HOOK_COUNT, cardPose } from "./deck";
import type { Vec3 } from "./math";
import { END, PH, SEGL, boxPhases } from "./phases";
import { planTokens, simulateTokens, tokenPose } from "./tokens";

const N = 4400;
const H = END / N;

function dist(a: Vec3, b: Vec3): number {
  return Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
}

/** Maior velocidade na vizinhanca (+- 60 amostras): a escala local do movimento (o voo e rapido, a caixa e lenta). */
function localMax(sp: Array<[number, number]>, i: number): number {
  let m = 0;
  for (let j = Math.max(0, i - 60); j <= Math.min(sp.length - 1, i + 60); j++)
    m = Math.max(m, sp[j][1]);
  return m;
}

/** Velocidade (m por unidade de progresso) em cada amostra. */
function speeds(at: (raw: number) => Vec3, from: number, to: number): Array<[number, number]> {
  const out: Array<[number, number]> = [];
  for (let raw = from + H; raw < to - H; raw += H)
    out.push([raw, dist(at(raw + H / 2), at(raw - H / 2)) / H]);
  return out;
}

/** Dentro de uma pausa, ou freando para ela / saindo dela (a velocidade vai a zero de proposito). */
const EASE = 0.012;
const inHold = (raw: number) => CAMERA_HOLDS.some(([a, b]) => raw >= a - EASE && raw <= b + EASE);

describe.each([false, true])("camera continua (celular: %s)", (mobile) => {
  const pos = (raw: number) => camPose(raw, 0, mobile).pos;
  const tgt = (raw: number) => camPose(raw, 0, mobile).tgt;

  // (o alvo pode ficar parado enquanto a camera orbita a caixa: so a posicao precisa nunca parar)
  it.each([["posicao", pos]] as const)(
    "a %s nunca para no meio do caminho (so nas pausas declaradas)",
    (_n, f) => {
      const all = speeds(f, 0, END);
      all.forEach(([raw, v], i) => {
        if (inHold(raw) || raw < 4 * H) return;
        expect(v, `parada em raw ${raw.toFixed(4)}`).toBeGreaterThan(localMax(all, i) * 0.04);
      });
    },
  );

  it.each([
    ["posicao", pos],
    ["alvo", tgt],
  ] as const)(
    "a velocidade da %s muda aos poucos (sem tranco entre amostras vizinhas)",
    (_n, f) => {
      const sp = speeds(f, 0, END);
      for (let i = 1; i < sp.length; i++) {
        if (inHold(sp[i][0])) continue;
        const jump = Math.abs(sp[i][1] - sp[i - 1][1]);
        expect(jump, `tranco em raw ${sp[i][0].toFixed(4)}`).toBeLessThan(localMax(sp, i) * 0.1);
      }
    },
  );

  it("sem tremor de alta frequencia: a direcao do movimento nao vai e volta", () => {
    let reversals = 0;
    let prev: Vec3 | null = null;
    for (let raw = H; raw < END - H; raw += H * 4) {
      if (inHold(raw)) continue;
      const a = pos(raw - H * 2);
      const b = pos(raw + H * 2);
      const d: Vec3 = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
      if (prev && d[0] * prev[0] + d[1] * prev[1] + d[2] * prev[2] < 0) reversals++;
      prev = d;
    }
    expect(reversals).toBeLessThanOrEqual(12);
  });
});

describe("cartas dos ganchos continuas", () => {
  it.each([0, 1, 2, 3].slice(0, HOOK_COUNT))(
    "a carta %i anda sem parar entre as chaves do proprio segmento",
    (i) => {
      const from = PH.story0 + i * SEGL + SEGL * 0.06;
      const to = PH.story0 + (i + 1) * SEGL - SEGL * 0.06;
      const at = (raw: number) =>
        cardPose(raw, i, { t: 0, mobile: false, phases: boxPhases(raw, 0) }).pos;
      // a revelacao (0,62 a 0,85 do segmento) e uma pausa: a carta para de frente para a camera
      const hold = (raw: number) => {
        const u = (raw - PH.story0 - i * SEGL) / SEGL;
        return u > 0.45 && u < 0.97;
      };
      const sp = speeds(at, from, to);
      sp.forEach(([raw, v], j) => {
        if (hold(raw)) return;
        expect(v, `carta ${i} parada em raw ${raw.toFixed(4)}`).toBeGreaterThan(
          localMax(sp, j) * 0.04,
        );
      });
    },
  );
});

describe("nada surge nem some de repente", () => {
  it("as cartas da explosao encolhem ate sumir: quando deixam de ser desenhadas a escala ja e quase zero", () => {
    for (let i = 0; i < CARD_COUNT; i++) {
      let prev = cardPose(PH.regroup, i, { t: 0, mobile: false });
      for (let raw = PH.regroup; raw <= END; raw += H) {
        const c = cardPose(raw, i, { t: 0, mobile: false });
        if (prev.visible && !c.visible)
          expect(prev.scale, `carta ${i} em raw ${raw}`).toBeLessThan(0.05);
        if (c.visible)
          expect(Math.abs(c.scale - prev.scale), `carta ${i} salta de escala`).toBeLessThan(0.08);
        prev = c;
      }
    }
  });

  it("os tokens crescem do zero ao entrar na mesa", () => {
    const tokens = planTokens(1);
    simulateTokens(tokens);
    for (const k of tokens) {
      let prev = tokenPose(k, 0);
      for (let tau = 0; tau <= 7; tau += 0.004) {
        const p = tokenPose(k, tau);
        if (!prev.visible && p.visible)
          expect(p.scale, `token ${k.i} em ${tau}`).toBeLessThan(0.05);
        if (p.visible && prev.visible) expect(Math.abs(p.scale - prev.scale)).toBeLessThan(0.08);
        prev = p;
      }
    }
  });
});
