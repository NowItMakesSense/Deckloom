// T-L02 / AC-L02k (REQ-L10): os tokens dos cinco jogos caem no altar com fisica simulada e deterministica.
import { describe, expect, it } from "vitest";

import {
  BOX_CENTER,
  BOX_RADIUS,
  DT,
  SIM_T,
  planTokens,
  sampleToken,
  simulateTokens,
  tokenPose,
  tokenTime,
  type Token,
} from "./tokens";

const GAMES = ["magic", "pokemon", "yugioh", "onepiece", "lorcana"];
const RUIN_WALL_Z = -24;

function simulated(): Token[] {
  const tokens = planTokens();
  simulateTokens(tokens);
  return tokens;
}

const at = (token: Token, tau: number) => sampleToken(token, tau, new Float32Array(6));
const speed = (token: Token, tau: number) => {
  const a = at(token, tau);
  const b = at(token, tau + DT);
  return Math.hypot(b[0] - a[0], b[1] - a[1]) / DT;
};

describe("planTokens", () => {
  const tokens = planTokens();

  it("sao cinco medalhoes (um por jogo, com o logo) e seis fichas de mana", () => {
    const medals = tokens.filter((k) => k.kind === "medal");
    expect(medals.map((k) => k.game)).toEqual(GAMES);
    expect(tokens.filter((k) => k.kind === "mana")).toHaveLength(6);
    expect(tokens.filter((k) => k.kind === "mana").every((k) => k.mana !== null)).toBe(true);
  });

  it("entram em instantes e de origens distintos", () => {
    expect(new Set(tokens.map((k) => k.t0.toFixed(4))).size).toBe(tokens.length);
    expect(new Set(tokens.map((k) => k.org.map((v) => v.toFixed(2)).join(","))).size).toBe(
      tokens.length,
    );
    const sides = new Set(
      tokens.map((k) =>
        k.org[0] < -10 ? "esq" : k.org[0] > 10 ? "dir" : k.org[2] < -30 ? "fundo" : "frente",
      ),
    );
    expect(sides).toEqual(new Set(["esq", "dir", "fundo", "frente"]));
  });

  it("os de tras vem de alem do muro de ruinas e sobem em arco por cima dele", () => {
    const back = tokens.filter((k) => k.org[2] < -30);
    expect(back.length).toBeGreaterThan(2);
    for (const k of back) {
      expect(k.org[2]).toBeLessThan(RUIN_WALL_Z);
      expect(k.arc).toBeGreaterThanOrEqual(6);
      const middle = tokenPose(k, k.t0 + k.dur / 2);
      expect(middle.pos[1]).toBeGreaterThan(5);
    }
  });

  it("pousam de quatro jeitos: quicar, girar, deslizar e tombar", () => {
    expect(new Set(tokens.map((k) => k.behav))).toEqual(
      new Set(["bounce", "spin", "slide", "tumble"]),
    );
  });

  it("no celular a camera da mesa recua: os de tras nascem colados atras do muro e os da frente sobem atras da camera", () => {
    const wide = planTokens(1.45);
    const narrow = planTokens(3.3);
    expect(wide.map((k) => k.org)).toEqual(planTokens(1).map((k) => k.org));
    narrow.forEach((k, i) => {
      if (wide[i].org[2] < -30) {
        expect(k.org[2]).toBeLessThan(RUIN_WALL_Z);
        expect(k.org[2]).toBeGreaterThan(-26);
        expect(k.org[1]).toBeLessThan(1);
      } else if (wide[i].org[2] > 10) {
        expect(k.org[1]).toBeGreaterThan(wide[i].org[1]);
        expect(k.org[2]).toBeGreaterThan(wide[i].org[2]);
      } else {
        expect(k.org).toEqual(wide[i].org);
      }
      expect(k.L).toEqual(wide[i].L);
    });
  });

  it("e deterministico", () => {
    const a = simulated();
    const b = simulated();
    for (let i = 0; i < a.length; i++) expect(a[i].track).toEqual(b[i].track);
  });
});

describe("simulateTokens", () => {
  const tokens = simulated();

  it("quicar: o token que quica sai do chao depois de pousar e volta", () => {
    const bounce = tokens.find((k) => k.behav === "bounce") as Token;
    let rises = 0;
    for (let tau = bounce.t0 + bounce.dur + 0.05; tau < SIM_T; tau += DT) {
      if (at(bounce, tau)[2] > 0.05) rises++;
    }
    expect(rises).toBeGreaterThan(5);
    expect(at(bounce, SIM_T)[2]).toBe(0);
  });

  it("girar: o token que gira muda de angulo depois de pousar", () => {
    const spin = tokens.find((k) => k.behav === "spin") as Token;
    const landed = bounceFree(spin);
    expect(Math.abs(at(spin, landed + 1)[3] - at(spin, landed)[3])).toBeGreaterThan(1);
  });

  it("deslizar: o token que desliza se desloca depois de pousar", () => {
    const slide = tokens.find((k) => k.behav === "slide") as Token;
    const landed = slide.t0 + slide.dur;
    const a = at(slide, landed + 0.05);
    const b = at(slide, landed + 0.6);
    expect(Math.hypot(b[0] - a[0], b[1] - a[1])).toBeGreaterThan(0.2);
  });

  it("tombar: o token que tomba comeca inclinado e termina quase plano", () => {
    const tumble = tokens.find((k) => k.behav === "tumble") as Token;
    expect(at(tumble, tumble.t0 + tumble.dur + DT)[4]).toBeGreaterThan(0.3);
    expect(at(tumble, SIM_T)[4]).toBeLessThan(0.05);
  });

  it("a velocidade decresce ate zero no repouso", () => {
    for (const k of tokens) {
      const mid = speed(k, k.t0 + k.dur + 0.3);
      const end = speed(k, SIM_T - 0.1);
      expect(end, `token ${k.i}`).toBeLessThan(0.05);
      expect(end, `token ${k.i}`).toBeLessThanOrEqual(mid + 1e-6);
    }
  });

  it("nenhum par fica interpenetrado no repouso, nem com a deck box", () => {
    for (let a = 0; a < tokens.length; a++) {
      const pa = at(tokens[a], SIM_T);
      const boxDistance = Math.hypot(pa[0] - BOX_CENTER[0], pa[1] - BOX_CENTER[1]);
      expect(boxDistance, `token ${a} e a caixa`).toBeGreaterThanOrEqual(
        tokens[a].R + BOX_RADIUS - 0.05,
      );
      for (let b = a + 1; b < tokens.length; b++) {
        const pb = at(tokens[b], SIM_T);
        const d = Math.hypot(pa[0] - pb[0], pa[1] - pb[1]);
        expect(d, `tokens ${a} e ${b}`).toBeGreaterThanOrEqual(tokens[a].R + tokens[b].R - 0.05);
      }
    }
  });

  it("ficam dentro da laje do altar", () => {
    for (const k of tokens) {
      const p = at(k, SIM_T);
      expect(Math.abs(p[0])).toBeLessThanOrEqual(5.6);
      expect(Math.abs(p[1])).toBeLessThanOrEqual(3.9);
    }
  });
});

describe("medalhao do Pokemon", () => {
  it("pousa a frente da deck box e a direita, visivel da camera da mesa (nao atras dela)", () => {
    for (const mesaK of [1.45, 3.3]) {
      const tokens = planTokens(mesaK);
      simulateTokens(tokens);
      const pokemon = tokens.find((k) => k.game === "pokemon") as Token;
      const rest = at(pokemon, SIM_T);
      expect(rest[0]).toBeGreaterThan(0);
      expect(rest[1]).toBeGreaterThan(BOX_CENTER[1] + BOX_RADIUS);
    }
  });
});

describe("tokenPose e tokenTime", () => {
  const tokens = simulated();

  it("o relogio dos tokens sai do progresso da mesa", () => {
    expect(tokenTime(0.3)).toBe(0);
    expect(tokenTime(0.92)).toBe(SIM_T);
    expect(tokenTime(0)).toBe(0);
    expect(tokenTime(1)).toBe(SIM_T);
  });

  it("antes de entrar o token nao aparece; ao entrar vem da sua origem", () => {
    const k = tokens[0];
    expect(tokenPose(k, k.t0 - 0.01).visible).toBe(false);
    const entering = tokenPose(k, k.t0);
    expect(entering.visible).toBe(true);
    expect(entering.pos[0]).toBeCloseTo(k.org[0], 5);
    expect(entering.pos[2]).toBeCloseTo(k.org[2], 5);
  });

  it("a posicao e continua na passagem da queda para a simulacao", () => {
    for (const k of tokens) {
      const before = tokenPose(k, k.t0 + k.dur - 1e-4).pos;
      const after = tokenPose(k, k.t0 + k.dur + 1e-4).pos;
      expect(Math.hypot(...before.map((v, i) => v - after[i])), `token ${k.i}`).toBeLessThan(0.2);
    }
  });
});

function bounceFree(k: Token): number {
  return k.t0 + k.dur + 0.1;
}
