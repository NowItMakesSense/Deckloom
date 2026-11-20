// T-L02 / AC-L02m (REQ-L10): as fases da floresta e da deck box como funcoes puras do progresso do scroll.
import { describe, expect, it } from "vitest";

import {
  END,
  PAN0,
  PAN1,
  PH,
  archRuneGlow,
  boxPhases,
  burstAt,
  mesaK,
  mesaProgress,
  pathRuneGlow,
} from "./phases";

const AFTER_FOREST = PH.forest + 0.0001;

describe("ordem das fases", () => {
  it("floresta, despertar, abertura, subida, introducao, ganchos, reagrupar, mesa, epilogo", () => {
    const order = [
      0,
      PH.forest,
      PH.awake,
      PH.open,
      PH.rise,
      PH.intro,
      PH.story1,
      PH.regroup,
      PH.mesa,
      1,
    ];
    expect([...order].sort((a, b) => a - b)).toEqual(order);
    expect(PH.story0).toBe(PH.intro);
  });

  it("a linha do tempo passa de 1: depois do portal (1) a camera desliza ate o mural e para antes do fim", () => {
    expect(END).toBeCloseTo(1.1);
    expect([1, PAN0, PAN1, END]).toEqual([1, PAN0, PAN1, END].sort((a, b) => a - b));
    expect(PAN0).toBeGreaterThan(1);
    expect(PAN1).toBeLessThan(END);
  });

  it("a camera da mesa recua mais no celular (todos os tokens cabem no quadro em pe)", () => {
    expect(mesaK(true)).toBeGreaterThan(mesaK(false));
    expect(mesaK(false)).toBeGreaterThanOrEqual(1);
  });
});

describe("runas do portal e rastro do caminho (a camera passa e elas acendem)", () => {
  const cameraZs = Array.from({ length: 150 }, (_, i) => 150 - i);

  it("apagadas na entrada da floresta, acesas uma a uma conforme a camera passa, na ordem", () => {
    expect(archRuneGlow(0, 150, 0)).toBe(0);
    const litAt = (k: number) => cameraZs.find((z) => archRuneGlow(0, z, k) > 0.4) ?? -1;
    const order = [0, 1, 2, 3, 4, 5, 6, 7, 8].map(litAt);

    expect(order.every((z) => z > 0)).toBe(true);
    expect([...order].sort((a, b) => b - a)).toEqual(order);
    expect(new Set(order).size).toBeGreaterThan(5);
  });

  it("depois da floresta todas as runas ficam acesas", () => {
    for (let k = 0; k < 9; k++) expect(archRuneGlow(AFTER_FOREST, 3, k)).toBeGreaterThan(0.9);
  });

  it("o rastro do caminho acende a frente da camera e nao acende o que esta muito longe", () => {
    const far = pathRuneGlow(0, 140, 6, 0);
    const ahead = pathRuneGlow(0, 30, 20, 0);
    expect(far).toBe(0);
    expect(ahead).toBeGreaterThan(0.3);
    expect(pathRuneGlow(AFTER_FOREST, 3, 6, 0)).toBeGreaterThan(0.5);
  });
});

describe("despertar e abertura da deck box", () => {
  const at = (raw: number) => boxPhases(raw, 0);

  it("as runas da caixa estao apagadas na floresta e despertam depois", () => {
    expect(at(PH.forest * 0.5).runeGlow).toBe(0);
    expect(at(PH.awake).runeGlow).toBeGreaterThan(1);
    expect(at(PH.forest).awake).toBe(0);
    expect(at(PH.awake).awake).toBe(1);
  });

  it("o fecho e as travas soltam antes de a tampa comecar a abrir", () => {
    const openU = 0.2;
    const raw = PH.awake + (PH.open - PH.awake) * openU;
    const phases = at(raw);

    expect(phases.clasp).toBeGreaterThan(0.1);
    expect(phases.latch).toBeGreaterThan(0.01);
    // as travas so destravam (no maximo ~26 graus): abertas demais viravam hastes espetadas para os lados
    expect(boxPhases(PH.open, 0).latch).toBeLessThanOrEqual(0.46);
    // o fecho so destrava e pende para fora (em pe, ele tapava a abertura da caixa)
    expect(boxPhases(PH.open, 0).clasp).toBeLessThanOrEqual(0.55);
    expect(phases.lid).toBeCloseTo(0, 5);
    expect(at(PH.awake).clasp).toBe(0);
  });

  it("a tampa abre pela dobradica de forma monotona, ate ~112 graus, e fica aberta", () => {
    let previous = 0;
    for (let i = 0; i <= 200; i++) {
      const raw = PH.awake + ((PH.open - PH.awake) * i) / 200;
      const lid = at(raw).lid;
      expect(lid).toBeLessThanOrEqual(previous + 0.0301);
      previous = Math.min(previous, lid);
    }
    expect(at(PH.open).lid).toBeLessThan(-1.9);
    expect(at(PH.mesa).lid).toBeLessThan(-1.9);
  });

  it("a luz sai da caixa depois que a tampa comeca a abrir, e baixa quando o deck entra em cena", () => {
    expect(at(PH.awake).light).toBe(0);
    expect(at(PH.open).light).toBeGreaterThan(0.9);
    expect(at(PH.intro + 0.05).light).toBeLessThan(0.4);
  });

  it("as cartas sobem da caixa ate o deck entre a abertura e a subida", () => {
    expect(at(PH.open).riseU).toBe(0);
    expect(at((PH.open + PH.rise) / 2).riseU).toBeCloseTo(0.5);
    expect(at(PH.rise).riseU).toBe(1);
  });
});

describe("altar e aneis de luz", () => {
  it("o altar acende com a caixa e o anel de luz cresce e some", () => {
    const before = boxPhases(PH.awake, 0);
    const mid = boxPhases(PH.awake + 0.008 + 0.0175, 0);
    const end = boxPhases(PH.awake + 0.1, 0);

    expect(before.altarGlow).toBe(0);
    expect(before.ringOpen.opacity).toBe(0);
    expect(mid.altarGlow).toBeGreaterThan(0.3);
    expect(mid.ringOpen.opacity).toBeGreaterThan(0.5);
    expect(mid.ringOpen.scale).toBeGreaterThan(1);
    expect(end.ringOpen.opacity).toBeCloseTo(0, 5);
    expect(end.altarGlow).toBeGreaterThan(0.8);
  });

  it("a explosao do deck corre como um segundo anel e faz a floresta reagir", () => {
    expect(burstAt(0.0)).toBe(0);
    expect(burstAt(0.1)).toBeGreaterThan(0.5);
    expect(burstAt(1)).toBe(0);
    const raw = PH.regroup + (PH.mesa - PH.regroup) * 0.14;
    const phases = boxPhases(raw, 0);
    expect(phases.burst).toBeGreaterThan(0.5);
    expect(phases.ringBurst.opacity).toBeGreaterThan(0.5);
    expect(phases.ringBurst.scale).toBeGreaterThan(1);
  });

  it("o progresso da mesa vai de 0 a 1 entre reagrupar e a mesa", () => {
    expect(mesaProgress(PH.regroup)).toBe(0);
    expect(mesaProgress(PH.mesa)).toBe(1);
    expect(mesaProgress(0)).toBe(0);
    expect(mesaProgress(1)).toBe(1);
  });
});

describe("vaga-lumes convergem para a caixa", () => {
  it("convergem entre a floresta e a abertura", () => {
    expect(boxPhases(PH.forest, 0).conv).toBe(0);
    expect(boxPhases(PH.open, 0).conv).toBe(1);
    expect(boxPhases(PH.mesa, 0).conv).toBe(1);
  });
});
