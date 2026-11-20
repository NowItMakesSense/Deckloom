// T-L02 / AC-L02m (REQ-L10): o fogo da clareira como funcoes puras: velas em pequenos grupos em volta do circulo de
// runas (longe da trajetoria dos tokens), tochas em suportes de ferro, chama animada que encara a camera so no eixo
// Y, tremulacao das luzes, brasas, reacao a explosao e sombras so fora do celular.
import { describe, expect, it } from "vitest";

import {
  CANDLE_MAX,
  CANDLE_WAX_TOP,
  candleScale,
  PORTAL_TORCHES,
  RUNE_RING_RADIUS,
  RUIN_TORCHES,
  STAND_TORCHES,
  candleWake,
  emberPoint,
  fireShadows,
  flameFrame,
  flicker,
  frontLightIntensity,
  lightFrame,
  placeCandles,
  type Flame,
} from "./fire";
import { PH } from "./phases";
import { ARCH_Z } from "./silhouettes";
import { planTokens, simulateTokens, type Token } from "./tokens";

function simulated(mesaK: number): Token[] {
  const tokens = planTokens(mesaK);
  simulateTokens(tokens);
  return tokens;
}

describe("velas em volta do circulo de runas", () => {
  for (const mesaK of [1.45, 3.3]) {
    const tokens = simulated(mesaK);
    const candles = placeCandles(tokens);

    it(`no maximo ${CANDLE_MAX}, em pequenos grupos, logo depois do anel de runas (mesa ${mesaK})`, () => {
      expect(candles.length).toBeGreaterThanOrEqual(4);
      expect(candles.length).toBeLessThanOrEqual(CANDLE_MAX);
      for (const c of candles) {
        const r = Math.hypot(c.x, c.z);
        expect(r).toBeGreaterThanOrEqual(RUNE_RING_RADIUS);
        expect(r).toBeLessThan(RUNE_RING_RADIUS + 0.85);
        expect(Math.abs(c.x)).toBeLessThanOrEqual(6.3);
        expect(Math.abs(c.z)).toBeLessThanOrEqual(4.45);
      }
      // grupos: varias velas compartilham o mesmo grupo
      expect(new Set(candles.map((c) => c.group)).size).toBeLessThan(candles.length);
    });

    it(`nenhuma vela no caminho dos tokens perto do chao nem encostada em outra (mesa ${mesaK})`, () => {
      for (const c of candles) {
        for (const k of tokens) {
          for (let s = 0; s < k.track.length; s += 36) {
            if (k.track[s + 2] >= 0.6) continue;
            const d = Math.hypot(k.track[s] - c.x, k.track[s + 1] - c.z);
            expect(d).toBeGreaterThanOrEqual(k.R + c.r * 2.4 + 0.15 - 1e-6);
          }
        }
      }
      for (let i = 0; i < candles.length; i++) {
        for (let j = i + 1; j < candles.length; j++) {
          const [a, b] = [candles[i], candles[j]];
          expect(Math.hypot(a.x - b.x, a.z - b.z)).toBeGreaterThanOrEqual((a.r + b.r) * 2.4);
        }
      }
    });

    it(`e deterministica (mesa ${mesaK})`, () => {
      expect(placeCandles(simulated(mesaK))).toEqual(candles);
    });
  }
});

describe("velas em castical (candlestick.glb do Poly Haven)", () => {
  it("a altura sorteada vira a escala do castical: a cera fica entre 0,26 e 0,68 m", () => {
    const tops = placeCandles(simulated(1)).map((c) => candleScale(c) * CANDLE_WAX_TOP);
    expect(tops.length).toBeGreaterThan(0);
    for (const t of tops) {
      expect(t).toBeGreaterThanOrEqual(0.26 - 1e-9);
      expect(t).toBeLessThanOrEqual(0.68 + 1e-9);
    }
  });
});

describe("tochas em suportes de ferro", () => {
  it("quatro na clareira, afastadas da laje, e quatro distantes no muro de ruinas", () => {
    expect(STAND_TORCHES).toHaveLength(4);
    for (const t of STAND_TORCHES) {
      expect(Math.abs(t.x) > 6.5 || Math.abs(t.z) > 4.6).toBe(true);
    }
    expect(STAND_TORCHES.filter((t) => t.shadow)).toHaveLength(2);
    expect(RUIN_TORCHES).toHaveLength(4);
    for (const t of RUIN_TORCHES) expect(t.z).toBeLessThan(-20);
  });

  it("as tochas do portal saem da face das ombreiras do modelo, nao de dentro da pedra", () => {
    expect(PORTAL_TORCHES).toHaveLength(2);
    // face das ombreiras do portal.glb: ARCH_Z + 0,24 (a parede lisa fica em ARCH_Z + 0,1)
    for (const t of PORTAL_TORCHES) expect(t.at[2]).toBeGreaterThanOrEqual(ARCH_Z + 0.24);
  });

  it("sombras das tochas so fora do celular", () => {
    expect(fireShadows(true, false)).toBe(true);
    expect(fireShadows(true, true)).toBe(false);
    expect(fireShadows(false, false)).toBe(false);
  });
});

describe("chama animada", () => {
  const candle: Flame = { x: 4.8, z: 0.3, w: 0.08, h: 0.2, seed: 1.3, candle: true };
  const torch: Flame = { x: -7.4, z: -6.6, w: 0.44, h: 1, seed: 7.7, candle: false };

  it("a tremulacao fica numa faixa estreita em volta de 1 e muda com o tempo", () => {
    const values = Array.from({ length: 400 }, (_, i) => flicker(i / 37, 2.1));
    expect(Math.min(...values)).toBeGreaterThan(0.82);
    expect(Math.max(...values)).toBeLessThan(1.18);
    expect(new Set(values.map((v) => v.toFixed(3))).size).toBeGreaterThan(50);
  });

  it("encara a camera girando so no eixo Y (billboard vertical)", () => {
    const f = flameFrame(torch, [torch.x, torch.z + 10], 0.5, 1, 0);
    expect(f.yaw).toBeCloseTo(0);
    const side = flameFrame(torch, [torch.x + 10, torch.z], 0.5, 1, 0);
    expect(side.yaw).toBeCloseTo(Math.PI / 2);
  });

  it("anima: altura, brilho e inclinacao mudam com o tempo", () => {
    const a = flameFrame(torch, [0, 50], 0.5, 1, 0);
    const b = flameFrame(torch, [0, 50], 0.5, 1.37, 0);
    expect(a.scaleY).not.toBeCloseTo(b.scaleY, 4);
    expect(a.hot).not.toBeCloseTo(b.hot, 4);
    expect(a.lean).not.toBeCloseTo(b.lean, 4);
  });

  it("as velas crescem enquanto a caixa desperta e voltam depois", () => {
    expect(candleWake(PH.forest - 0.01)).toBe(0);
    expect(candleWake((PH.forest + PH.awake) / 2)).toBeCloseTo(1);
    expect(candleWake(PH.awake + 0.01)).toBeCloseTo(0);
    const mid = (PH.forest + PH.awake) / 2;
    expect(flameFrame(candle, [0, 9], mid, 0, 0).hot).toBeGreaterThan(
      flameFrame(candle, [0, 9], 0.5, 0, 0).hot,
    );
    // a tocha nao muda com o despertar
    expect(flameFrame(torch, [0, 9], mid, 0, 0).hot).toBeCloseTo(
      flameFrame(torch, [0, 9], 0.5, 0, 0).hot,
    );
  });

  it("a explosao do deck abaixa as chamas e as empurra para fora da caixa", () => {
    const calm = flameFrame(candle, [0, 9], 0.7, 0, 0);
    const blast = flameFrame(candle, [0, 9], 0.7, 0, 1);
    expect(blast.scaleY).toBeLessThan(calm.scaleY);
    expect(Math.abs(blast.lean)).toBeGreaterThan(Math.abs(calm.lean));
  });
});

describe("luzes do fogo", () => {
  it("a luz da tocha tremula em volta da intensidade base e balanca poucos centimetros", () => {
    const L = { p: [0, 3, 0] as const, i0: 26, seed: 5.3, kind: "torch" as const };
    for (let i = 0; i < 200; i++) {
      const f = lightFrame(L, 0.5, i / 13);
      expect(f.intensity).toBeGreaterThan(26 * 0.69);
      expect(f.intensity).toBeLessThan(26 * 1.15);
      expect(Math.hypot(f.pos[0] - 0, f.pos[1] - 3, f.pos[2] - 0)).toBeLessThan(0.08);
    }
  });

  it("a luz das velas sobe com o despertar da caixa", () => {
    const L = { p: [4, 0.6, 0] as const, i0: 6.5, seed: 3.1, kind: "candle" as const };
    const mid = (PH.forest + PH.awake) / 2;
    expect(lightFrame(L, mid, 0).intensity).toBeGreaterThan(lightFrame(L, 0.5, 0).intensity);
  });

  it("a luz frontal sobe so enquanto as cartas dos ganchos viram", () => {
    expect(frontLightIntensity(0.1)).toBeCloseTo(0.4);
    expect(frontLightIntensity((PH.story0 + PH.story1) / 2)).toBeCloseTo(0.9);
    expect(frontLightIntensity(PH.mesa)).toBeCloseTo(0.4);
  });
});

describe("brasas", () => {
  it("sobem a partir da chama e apagam ao subir; o ciclo e continuo no tempo", () => {
    const seed = [0.3, 0.5, 0.7] as const;
    const src = [1, 2, 3] as const;
    const a = emberPoint(seed, src, 0);
    const b = emberPoint(seed, src, 0.4);
    expect(b.pos[1]).not.toBeCloseTo(a.pos[1]);
    for (let i = 0; i < 300; i++) {
      const e = emberPoint(seed, src, i / 50);
      expect(e.pos[1]).toBeGreaterThanOrEqual(src[1]);
      expect(e.pos[1]).toBeLessThanOrEqual(src[1] + 1.9);
      expect(e.fade).toBeGreaterThanOrEqual(0);
      expect(e.fade).toBeLessThanOrEqual(1);
    }
  });
});
