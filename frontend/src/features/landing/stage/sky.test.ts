// Ceu noturno (pedido do usuario, 2026-11-15): lua visivel nos enquadramentos principais e estrelas; a luz principal
// da cena vem da lua.
import { describe, expect, it } from "vitest";

import { camPose, fovFor } from "./camera";
import type { Vec3 } from "./math";
import { PH } from "./phases";
import { MOON_DIR, MOON_RADIUS_DEG, moonLightPosition } from "./sky";

const deg = (r: number) => (r * 180) / Math.PI;

function norm(v: Vec3): Vec3 {
  const l = Math.hypot(v[0], v[1], v[2]);
  return [v[0] / l, v[1] / l, v[2] / l];
}

/** A lua cabe no quadro da camera (fov vertical e horizontal do desktop)? */
function inFrame(raw: number): boolean {
  const c = camPose(raw, 0, false);
  const f = norm([c.tgt[0] - c.pos[0], c.tgt[1] - c.pos[1], c.tgt[2] - c.pos[2]]);
  const aspect = 1366 / 768;
  const vHalf = fovFor(aspect) / 2;
  const hHalf = deg(Math.atan(Math.tan((vHalf * Math.PI) / 180) * aspect));
  const elev = (v: Vec3) => deg(Math.asin(v[1]));
  const az = (v: Vec3) => deg(Math.atan2(v[0], -v[2]));
  return (
    Math.abs(elev(MOON_DIR) - elev(f)) < vHalf - MOON_RADIUS_DEG &&
    Math.abs(az(MOON_DIR) - az(f)) < hHalf - MOON_RADIUS_DEG
  );
}

describe("lua", () => {
  it("fica acima do horizonte, baixa (entre 8 e 16 graus) e a esquerda", () => {
    const elev = deg(Math.asin(MOON_DIR[1]));
    expect(elev).toBeGreaterThan(8);
    expect(elev).toBeLessThan(16);
    expect(MOON_DIR[0]).toBeLessThan(0);
    expect(Math.hypot(...MOON_DIR)).toBeCloseTo(1, 6);
  });

  it("aparece no voo de entrada e na clareira (pose-base dos ganchos)", () => {
    expect(inFrame(0)).toBe(true);
    expect(inFrame(PH.forest * 0.3)).toBe(true);
    expect(inFrame(PH.story0)).toBe(true);
  });

  it("a luz principal vem da direcao da lua", () => {
    const target: Vec3 = [0, 0, -0.4];
    const p = moonLightPosition(target, 30);
    const d = norm([p[0] - target[0], p[1] - target[1], p[2] - target[2]]);
    expect(d[0] * MOON_DIR[0] + d[1] * MOON_DIR[1] + d[2] * MOON_DIR[2]).toBeGreaterThan(0.999);
  });
});
