// T-L02 / AC-L02e (REQ-L10): as cartas do baralho como funcoes puras do progresso do scroll.
import { describe, expect, it } from "vitest";

import { CARD_COUNT, boxPose, cardPose, deckPose, hookKeys, pilePose } from "./deck";
import { PH, SEGL } from "./phases";

const near = (a: readonly number[], b: readonly number[], digits = 2) => {
  a.forEach((v, i) => expect(v).toBeCloseTo(b[i], digits));
};
const ctx = { t: 0, mobile: false };

describe("posicoes de repouso", () => {
  it("as quatro primeiras cartas ficam de frente (180 graus) para o gancho e o resto de costas", () => {
    for (let i = 0; i < CARD_COUNT; i++) {
      expect(deckPose(i).r[1]).toBe(i < 4 ? Math.PI : 0);
      expect(boxPose(i).r[1]).toBe(i < 4 ? Math.PI : 0);
    }
  });

  it("dentro da caixa as cartas ficam abaixo da altura do deck e empilhadas", () => {
    expect(boxPose(0).p[1]).toBeLessThan(deckPose(0).p[1]);
    expect(boxPose(5).p[2]).toBeLessThan(boxPose(0).p[2]);
  });

  it("a pilha das cartas ja reveladas muda entre desktop e celular", () => {
    expect(pilePose(0, true).p).not.toEqual(pilePose(0, false).p);
  });
});

describe("as quatro cartas dos ganchos", () => {
  it.each([0, 1, 2, 3])(
    "carta %i: sai do deck, aproxima, vira 180 graus, revela e vai para a pilha",
    (k) => {
      for (const mobile of [false, true]) {
        const keys = hookKeys(k, mobile);
        near(keys[0].p, deckPose(k).p, 6);
        near(keys[0].r, deckPose(k).r, 6);
        near(keys[4].p, pilePose(k, mobile).p, 6);
        expect(keys[1].p[1]).toBeGreaterThan(keys[0].p[1]);
        expect(keys[2].p[2]).toBeGreaterThan(keys[1].p[2]);
        expect(keys[0].r[1]).toBeCloseTo(Math.PI);
        expect(keys[2].r[1]).toBe(0);
        expect(Math.abs(keys[3].p[2] - keys[2].p[2])).toBeLessThan(0.1);
        expect(keys.map((key) => key.u)).toEqual([0, 0.27, 0.62, 0.85, 1]);
      }
    },
  );

  it("uma carta por vez: durante o segmento k so a carta k se move", () => {
    for (let k = 0; k < 4; k++) {
      for (const mobile of [false, true]) {
        const start = PH.story0 + SEGL * k;
        const poses = (raw: number) =>
          Array.from({ length: CARD_COUNT }, (_, i) => cardPose(raw, i, { t: 0, mobile }).pos);
        const first = poses(start + SEGL * 0.01);
        const later = poses(start + SEGL * 0.7);
        for (let i = 0; i < CARD_COUNT; i++) {
          const moved = Math.hypot(...later[i].map((v, j) => v - first[i][j]));
          if (i === k) expect(moved, `carta ${i} no segmento ${k}`).toBeGreaterThan(1);
          else expect(moved, `carta ${i} no segmento ${k}`).toBeLessThan(1e-9);
        }
      }
    }
  });

  it("a carta ativa fica com a face para a camera quando o gancho e revelado", () => {
    const k = 2;
    const pose = cardPose(PH.story0 + SEGL * (k + 0.7), k, ctx);
    expect(Math.abs(pose.rot[1])).toBeLessThan(0.1);
    expect(pose.visible).toBe(true);
  });

  it("o brilho passa pela carta ao revelar e nao em outro momento", () => {
    const k = 1;
    const reveal = cardPose(PH.story0 + SEGL * (k + 0.62), k, ctx);
    const away = cardPose(PH.story0 + SEGL * (k + 0.1), k, ctx);
    expect(reveal.sheen?.opacity).toBeGreaterThan(0.1);
    expect(away.sheen?.opacity).toBeCloseTo(0, 5);
    expect(cardPose(0.5, 10, ctx).sheen).toBeUndefined();
  });

  it("a posicao e continua: nenhum teletransporte: no maximo 0,3 por passo de 1/4000 antes da explosao", () => {
    for (const i of [0, 1, 2, 3, 4, 20]) {
      let previous = cardPose(0, i, ctx).pos;
      let max = 0;
      for (let s = 1; s <= 4000 * PH.regroup; s++) {
        const next = cardPose(s / 4000, i, ctx).pos;
        max = Math.max(max, Math.hypot(...next.map((v, j) => v - previous[j])));
        previous = next;
      }
      expect(max, `carta ${i}`).toBeLessThan(0.3);
    }
  });
});

describe("cartas subindo da caixa ate o deck", () => {
  it("comecam dentro da caixa, chegam ao deck e sobem escalonadas", () => {
    near(cardPose(PH.open, 0, ctx).pos, boxPose(0).p, 4);
    near(cardPose(PH.rise, 0, ctx).pos, deckPose(0).p, 2);
    const mid = (PH.open + PH.rise) / 2;
    expect(cardPose(mid, 0, ctx).pos[1]).toBeGreaterThan(cardPose(mid, CARD_COUNT - 1, ctx).pos[1]);
  });
});

describe("o baralho cabe na deck box (2026-11-15)", () => {
  // vao interno da deck box (deckbox.glb): 1,5 x 0,98 x 1,85 por fora, centro em z = -0,4
  const BOX_Z = [-0.4 - 0.49 + 0.06, -0.4 + 0.49 - 0.06] as const;

  it("a pilha dentro da caixa cabe no vao, com folga, e fica centrada", () => {
    const zs = Array.from({ length: CARD_COUNT }, (_, i) => boxPose(i).p[2]);
    expect(Math.min(...zs) - 0.008).toBeGreaterThan(BOX_Z[0]);
    expect(Math.max(...zs) + 0.008).toBeLessThan(BOX_Z[1]);
    const mid = (Math.min(...zs) + Math.max(...zs)) / 2;
    expect(Math.abs(mid - -0.4)).toBeLessThan(0.08);
  });

  it("o deck flutuando nao fica muito mais fundo que a propria caixa", () => {
    const raw = PH.intro - 0.01;
    const zs = Array.from(
      { length: CARD_COUNT },
      (_, i) => cardPose(raw, i, { t: 0, mobile: false }).pos[2],
    );
    expect(Math.max(...zs) - Math.min(...zs)).toBeLessThan(0.98 * 1.3);
  });
});
