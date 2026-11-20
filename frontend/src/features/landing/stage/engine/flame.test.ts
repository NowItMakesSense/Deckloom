// Fogo procedural (REQ-L10, refeito na T-L05): ruido fbm que sobe e se distorce mais no alto, recortado em gota e
// pintado por uma rampa de calor (brasa -> laranja -> amarelo -> branco). Sem textura nem imagem externa.
import { AdditiveBlending } from "three";
import { describe, expect, it } from "vitest";

import { FLAME_PLANE_WIDTH, flameMaterial } from "./flame";

describe("material da chama", () => {
  it("usa ruido fbm com distorcao crescente na altura e uma rampa de calor", () => {
    const m = flameMaterial(1.3, false, 0.4);
    expect(m.fragmentShader).toMatch(/float fbm\(/);
    expect(m.fragmentShader).toMatch(/heat/);
    // a distorcao cresce com a altura (as labaredas no topo, o pe firme)
    expect(m.fragmentShader).toMatch(/\* \(0\.\d+ \+ [\d.]+ \* y \* y\)/);
    expect(m.blending).toBe(AdditiveBlending);
    expect(m.depthWrite).toBe(false);
  });

  it("chamas de tocha sao mais altas que largas e tem o plano mais largo que a lingua (espaco para as labaredas)", () => {
    expect(FLAME_PLANE_WIDTH.torch).toBeGreaterThan(1.3);
    expect(FLAME_PLANE_WIDTH.candle).toBeGreaterThanOrEqual(1);
    expect(FLAME_PLANE_WIDTH.torch).toBeGreaterThan(FLAME_PLANE_WIDTH.candle);
  });

  it("cada chama tem a propria semente e a vela guarda o pe azul", () => {
    const a = flameMaterial(1, true, 0.1);
    const b = flameMaterial(2, false, 0.4);
    expect(a.uniforms.seed.value).not.toBe(b.uniforms.seed.value);
    expect(a.uniforms.candle.value).toBe(1);
    expect(b.uniforms.candle.value).toBe(0);
  });
});
