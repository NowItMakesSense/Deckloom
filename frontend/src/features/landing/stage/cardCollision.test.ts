// Cartas que nao se atravessam (revisao da T-L05): no deck e na subida da caixa as cartas ficam em pe, lado a lado,
// a poucos centimetros. Cada uma pode balancar, mas nenhuma pode cruzar o plano da vizinha.
import { describe, expect, it } from "vitest";

import { CARD_COUNT, HOOK_COUNT, cardPose } from "./deck";
import { type Vec3, eulerToQuat } from "./math";
import { PH, boxPhases } from "./phases";

const HALF_W = 0.63;
const HALF_H = 0.88;
const THICK = 0.016;

function rotate(q: readonly number[], v: Vec3): Vec3 {
  const [x, y, z, w] = q;
  const ix = w * v[0] + y * v[2] - z * v[1];
  const iy = w * v[1] + z * v[0] - x * v[2];
  const iz = w * v[2] + x * v[1] - y * v[0];
  const iw = -x * v[0] - y * v[1] - z * v[2];
  return [
    ix * w + iw * -x + iy * -z - iz * -y,
    iy * w + iw * -y + iz * -x - ix * -z,
    iz * w + iw * -z + ix * -y - iy * -x,
  ];
}

/** Cantos da carta no mundo. */
function corners(pos: Vec3, rot: Vec3): Vec3[] {
  const q = eulerToQuat(rot);
  return [
    [-HALF_W, -HALF_H, 0],
    [HALF_W, -HALF_H, 0],
    [HALF_W, HALF_H, 0],
    [-HALF_W, HALF_H, 0],
  ].map((c) => {
    const r = rotate(q, c as unknown as Vec3);
    return [pos[0] + r[0], pos[1] + r[1], pos[2] + r[2]] as Vec3;
  });
}

/** Distancia com sinal de cada canto de `b` ao plano de `a` (normal da carta a). */
function sides(a: { pos: Vec3; rot: Vec3 }, b: { pos: Vec3; rot: Vec3 }): number[] {
  const n = rotate(eulerToQuat(a.rot), [0, 0, 1]);
  return corners(b.pos, b.rot).map(
    (c) => (c[0] - a.pos[0]) * n[0] + (c[1] - a.pos[1]) * n[1] + (c[2] - a.pos[2]) * n[2],
  );
}

describe("cartas do deck nao se atravessam", () => {
  it.each([0, 1.7, 4.2])("na subida da caixa e no deck flutuando (t = %s s)", (t) => {
    for (let raw = PH.open; raw <= PH.story1; raw += 0.0025) {
      const ph = boxPhases(raw, t);
      const poses = Array.from({ length: CARD_COUNT }, (_, i) =>
        cardPose(raw, i, { t, mobile: false, phases: ph }),
      );
      for (let i = HOOK_COUNT; i < CARD_COUNT - 1; i++) {
        const a = poses[i];
        const b = poses[i + 1];
        if (!a.visible || !b.visible) continue;
        const d = sides(a, b);
        // todos os cantos da vizinha de tras ficam do mesmo lado, alem da espessura da carta
        const crossing = Math.min(...d) < THICK / 2 && Math.max(...d) > -THICK / 2;
        expect(
          crossing,
          `cartas ${i} e ${i + 1} se cruzam em raw ${raw.toFixed(4)}: ${d.map((x) => x.toFixed(3))}`,
        ).toBe(false);
      }
    }
  });
});
