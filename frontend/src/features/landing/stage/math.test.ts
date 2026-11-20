// T-L02 / AC-L02e, AC-L02m: a matematica pura que todas as funcoes do palco compartilham.
import { describe, expect, it } from "vitest";

import {
  catmull,
  clamp,
  eulerToQuat,
  keyed,
  lerp,
  lerp3,
  quatToEuler,
  rng,
  slerp,
  smooth,
  trackAt,
} from "./math";

describe("rng", () => {
  it("e deterministico: a mesma semente da a mesma sequencia, e fica em [0, 1)", () => {
    const a = rng(2026);
    const b = rng(2026);
    const seq = Array.from({ length: 50 }, () => a());

    expect(seq).toEqual(Array.from({ length: 50 }, () => b()));
    for (const v of seq) {
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThan(1);
    }
    expect(rng(1)()).not.toBe(rng(2)());
  });
});

describe("clamp, smooth, lerp", () => {
  it("clamp limita, smooth vai de 0 a 1 sem degrau e lerp interpola", () => {
    expect(clamp(5, 0, 1)).toBe(1);
    expect(clamp(-5, 0, 1)).toBe(0);
    expect(smooth(-1)).toBe(0);
    expect(smooth(2)).toBe(1);
    expect(smooth(0.5)).toBeCloseTo(0.5);
    expect(lerp(2, 4, 0.5)).toBe(3);
    expect(lerp3([0, 0, 0], [2, 4, 6], 0.5)).toEqual([1, 2, 3]);
  });
});

describe("keyed", () => {
  const keys = [
    { u: 0, p: [0, 0, 0] as const },
    { u: 0.5, p: [10, 0, 0] as const },
    { u: 1, p: [10, 10, 0] as const },
  ];

  it("passa exatamente pelas chaves e interpola entre elas", () => {
    expect(keyed(keys, 0, ["p"]).p).toEqual([0, 0, 0]);
    expect(keyed(keys, 0.5, ["p"]).p).toEqual([10, 0, 0]);
    expect(keyed(keys, 1, ["p"]).p).toEqual([10, 10, 0]);
    expect(keyed(keys, 0.25, ["p"]).p[0]).toBeCloseTo(5);
  });
});

describe("catmull", () => {
  it("passa por p1 em u = 0 e por p2 em u = 1", () => {
    expect(catmull(0, 1, 2, 5, 0)).toBeCloseTo(1);
    expect(catmull(0, 1, 2, 5, 1)).toBeCloseTo(2);
  });
});

describe("track: Hermite com nos nao uniformes (velocidade continua)", () => {
  const keys = [
    { t: 0, v: [0, 0, 0] as const },
    { t: 1, v: [10, 0, 0] as const },
    { t: 3, v: [10, 10, 0] as const },
    { t: 4, v: [0, 10, 0] as const },
  ];

  it("passa exatamente por cada chave", () => {
    for (const k of keys) expect(trackAt(keys, k.t)).toEqual(k.v);
  });

  it("nao para nas chaves do meio: a velocidade nelas e a mesma dos dois lados e nao e zero", () => {
    const h = 1e-4;
    for (const k of keys.slice(1, -1)) {
      const a = trackAt(keys, k.t - h);
      const b = trackAt(keys, k.t + h);
      const c = trackAt(keys, k.t);
      const vin = Math.hypot(c[0] - a[0], c[1] - a[1], c[2] - a[2]) / h;
      const vout = Math.hypot(b[0] - c[0], b[1] - c[1], b[2] - c[2]) / h;
      expect(vin).toBeGreaterThan(1);
      expect(Math.abs(vin - vout) / vin).toBeLessThan(0.01);
    }
  });

  it("chave repetida e uma pausa: parado entre as duas, sem passar do ponto", () => {
    const hold = [
      { t: 0, v: [0, 0, 0] as const },
      { t: 1, v: [5, 0, 0] as const },
      { t: 2, v: [5, 0, 0] as const },
      { t: 3, v: [9, 0, 0] as const },
    ];
    expect(trackAt(hold, 1.5)).toEqual([5, 0, 0]);
    for (let t = 0; t <= 3; t += 0.01) expect(trackAt(hold, t)[0]).toBeLessThanOrEqual(9 + 1e-9);
    for (let t = 0; t <= 2; t += 0.01) expect(trackAt(hold, t)[0]).toBeLessThanOrEqual(5 + 1e-9);
  });

  it("fora do intervalo fica na primeira ou na ultima chave", () => {
    expect(trackAt(keys, -1)).toEqual(keys[0].v);
    expect(trackAt(keys, 9)).toEqual(keys[3].v);
  });
});

describe("rotacao por quaternion", () => {
  it("de Euler para quaternion e de volta da o mesmo angulo", () => {
    const r: [number, number, number] = [0.3, -1.1, 0.4];
    const back = quatToEuler(eulerToQuat(r));
    for (let i = 0; i < 3; i++) expect(back[i]).toBeCloseTo(r[i], 6);
  });

  it("slerp faz a virada de 180 graus pelo caminho mais curto, sem cambalhota", () => {
    const a = eulerToQuat([-0.28, Math.PI, 0.16]);
    const b = eulerToQuat([0, 0, 0]);
    let prev = a;
    let total = 0;
    for (let s = 0.01; s <= 1.0001; s += 0.01) {
      const q = slerp(a, b, s);
      const dot = Math.abs(q[0] * prev[0] + q[1] * prev[1] + q[2] * prev[2] + q[3] * prev[3]);
      total += 2 * Math.acos(Math.min(1, dot));
      prev = q;
    }
    const direct =
      2 * Math.acos(Math.min(1, Math.abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3])));
    expect(total).toBeCloseTo(direct, 3);
  });
});
