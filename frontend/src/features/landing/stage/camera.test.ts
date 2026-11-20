// T-L02 / AC-L02e, AC-L02l (REQ-L10): uma unica funcao de camera, continua, com uma pose por segmento.
import { describe, expect, it } from "vitest";

import { boardLayout, boardView, EPILOGUE_END } from "./board";
import { aperture, camPose, fovFor, type CamPose } from "./camera";
import { END, PAN0, PAN1, PH, SEGL } from "./phases";

const STEPS = 4000;

function forward(c: CamPose): [number, number, number] {
  const d = [c.tgt[0] - c.pos[0], c.tgt[1] - c.pos[1], c.tgt[2] - c.pos[2]];
  const len = Math.hypot(...d) || 1;
  return [d[0] / len, d[1] / len, d[2] / len];
}

function angleDeg(a: CamPose, b: CamPose): number {
  const [fa, fb] = [forward(a), forward(b)];
  const dot = fa[0] * fb[0] + fa[1] * fb[1] + fa[2] * fb[2];
  return (Math.acos(Math.min(1, Math.max(-1, dot))) * 180) / Math.PI;
}

function move(a: CamPose, b: CamPose): number {
  return Math.hypot(b.pos[0] - a.pos[0], b.pos[1] - a.pos[1], b.pos[2] - a.pos[2]);
}

describe.each([false, true])("camPose (celular: %s)", (mobile) => {
  const aspect = mobile ? 375 / 812 : 1366 / 768;

  it("sem salto: deslocamento <= 0,6 e giro <= 4 graus a cada 1/4000 do progresso, ate o mural (END)", () => {
    let previous = camPose(0, 0, mobile, aspect);
    let maxMove = 0;
    let maxTurn = 0;
    for (let i = 1; i <= STEPS; i++) {
      const next = camPose((i / STEPS) * END, 0, mobile, aspect);
      maxMove = Math.max(maxMove, move(previous, next));
      maxTurn = Math.max(maxTurn, angleDeg(previous, next));
      previous = next;
    }

    expect(maxMove).toBeLessThanOrEqual(0.6);
    expect(maxTurn).toBeLessThanOrEqual(4);
  });

  it("continuidade nos limites de cada fase (sem corte entre segmentos)", () => {
    const limits = [
      PH.forest,
      PH.awake,
      PH.open,
      PH.rise,
      PH.intro,
      PH.story1,
      PH.regroup,
      PH.mesa,
      1,
      PAN0,
      PAN1,
    ];
    for (const limit of limits) {
      const before = camPose(limit - 1e-6, 0, mobile, aspect);
      const after = camPose(limit + 1e-6, 0, mobile, aspect);
      expect(move(before, after), `limite ${limit}`).toBeLessThan(0.05);
      expect(angleDeg(before, after), `limite ${limit}`).toBeLessThan(0.5);
    }
  });

  it("os quatro segmentos dos ganchos: uma carta ativa por vez, na ordem", () => {
    const seen: number[] = [];
    for (let i = 0; i < 400; i++) {
      const raw = PH.story0 + ((PH.story1 - PH.story0) * i) / 400;
      const { active } = camPose(raw, 0, mobile);
      if (seen.at(-1) !== active) seen.push(active);
    }
    expect(seen).toEqual([0, 1, 2, 3]);
    expect(camPose(PH.story0 + SEGL * 2.5, 0, mobile).active).toBe(2);
    expect(camPose(PH.rise, 0, mobile).active).toBe(-1);
    expect(camPose(PH.regroup, 0, mobile).active).toBe(-1);
  });

  it("a camera de cada segmento volta a pose-base antes de passar para a proxima carta", () => {
    for (let k = 1; k < 4; k++) {
      const end = camPose(PH.story0 + SEGL * k - 1e-7, 0, mobile);
      const start = camPose(PH.story0 + SEGL * k + 1e-7, 0, mobile);
      expect(end.pos[2]).toBeCloseTo(start.pos[2], 3);
    }
  });

  it("o epilogo volta pelo caminho e para atras do portal de pedra", () => {
    const end = camPose(1, 0, mobile);
    expect(end.pos[2]).toBeGreaterThan(50);
    expect(end.tgt[1]).toBeGreaterThan(3);
  });

  it("depois do portal a camera fica parada ate o pan e desliza para a esquerda ate o mural de avisos", () => {
    const portal = camPose(1, 0, mobile, aspect);
    const hold = camPose((1 + PAN0) / 2, 0, mobile, aspect);
    for (let j = 0; j < 3; j++) {
      expect(hold.pos[j]).toBeCloseTo(EPILOGUE_END.pos[j], 6);
      expect(portal.pos[j]).toBeCloseTo(EPILOGUE_END.pos[j], 6);
    }
    const fov = fovFor(aspect);
    const view = boardView(boardLayout(mobile), fov, aspect, mobile);
    for (const raw of [PAN1, (PAN1 + END) / 2, END]) {
      const end = camPose(raw, 0, mobile, aspect);
      for (let j = 0; j < 3; j++) {
        expect(end.pos[j]).toBeCloseTo(view.pos[j], 6);
        expect(end.tgt[j]).toBeCloseTo(view.tgt[j], 6);
      }
      expect(end.roll).toBe(0);
      expect(end.active).toBe(-1);
    }
    expect(camPose(END, 0, mobile, aspect).pos[0]).toBeLessThan(portal.pos[0] - 4);
  });

  it("a camera da mesa recua com MESA_K: mais longe dos tokens no celular", () => {
    const raw = PH.regroup + (PH.mesa - PH.regroup) * 0.8;
    const c = camPose(raw, 0, mobile, aspect);
    const dist = Math.hypot(c.pos[0] - c.tgt[0], c.pos[1] - c.tgt[1], c.pos[2] - c.tgt[2]);
    expect(dist).toBeGreaterThan(mobile ? 15 : 9);
  });

  it("entra pelas copas, alto e longe", () => {
    const start = camPose(0, 0, mobile);
    expect(start.pos[1]).toBeGreaterThan(15);
    expect(start.pos[2]).toBeGreaterThan(140);
  });

  it("e deterministica: o mesmo progresso e tempo dao a mesma pose", () => {
    expect(camPose(0.5, 3, mobile)).toEqual(camPose(0.5, 3, mobile));
  });
});

describe("fovFor", () => {
  it("tela em pe usa lente mais aberta", () => {
    expect(fovFor(375 / 812)).toBe(62);
    expect(fovFor(1366 / 768)).toBe(42);
  });
});

describe("aperture (profundidade de campo)", () => {
  it("e sempre positiva e continua na entrada da caixa", () => {
    for (let i = 0; i <= 200; i++) expect(aperture((i / 200) * END)).toBeGreaterThan(0);
    expect(Math.abs(aperture(PH.forest - 1e-6) - aperture(PH.forest + 1e-6))).toBeLessThan(10);
  });
});

describe("closes no celular (2026-11-15)", () => {
  const aspect = 390 / 844;
  const vHalf = (fovFor(aspect) * Math.PI) / 360;
  const hHalf = Math.atan(Math.tan(vHalf) * aspect);

  /** A deck box inteira (1,5 de largura, 1,85 de altura) cabe na largura e na altura da tela do celular? */
  function boxFits(raw: number): boolean {
    const c = camPose(raw, 0, true, aspect);
    const d = Math.hypot(c.pos[0] - 0, c.pos[2] - -0.4);
    const halfW = Math.atan((0.75 * 1.15) / d);
    const halfH = Math.atan(0.95 / d);
    return halfW < hHalf * 0.95 && halfH < vHalf;
  }

  it("a deck box cabe inteira na tela durante a orbita, a abertura e a subida", () => {
    for (let raw = PH.forest + 0.002; raw < PH.rise; raw += 0.004)
      expect(boxFits(raw), `raw ${raw.toFixed(3)}`).toBe(true);
  });
});
