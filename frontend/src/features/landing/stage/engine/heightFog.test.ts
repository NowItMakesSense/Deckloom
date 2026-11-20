// Nevoa de chao (pedido do usuario, 2026-11-15): alem da nevoa de distancia, uma camada baixa que se acumula no
// chao e entre as arvores. Instalada nos pedacos de shader de nevoa do three (vale para todo material com fog).
import { ShaderChunk } from "three";
import { describe, expect, it } from "vitest";

import { GROUND_FOG, installHeightFog } from "./heightFog";

describe("nevoa de chao", () => {
  it("entra nos shaders de nevoa: posicao no mundo no vertice e camada baixa no fragmento", () => {
    installHeightFog();
    expect(ShaderChunk.fog_pars_vertex).toMatch(/vFogWorld/);
    expect(ShaderChunk.fog_vertex).toMatch(/vFogWorld/);
    expect(ShaderChunk.fog_pars_fragment).toMatch(/vFogWorld/);
    expect(ShaderChunk.fog_fragment).toMatch(/GROUND_FOG_DENSITY/);
    // a nevoa de distancia do three continua (FogExp2)
    expect(ShaderChunk.fog_fragment).toMatch(/fogDensity/);
  });

  it("instalar duas vezes nao duplica o codigo", () => {
    installHeightFog();
    const once = ShaderChunk.fog_fragment;
    installHeightFog();
    expect(ShaderChunk.fog_fragment).toBe(once);
  });

  it("a camada e baixa e densa no chao, e nunca cobre tudo (o altar continua visivel)", () => {
    expect(GROUND_FOG.base).toBeLessThan(0.5);
    expect(GROUND_FOG.falloff).toBeGreaterThan(0.8);
    expect(GROUND_FOG.falloff).toBeLessThan(3);
    // densa (pedido do usuario, 2026-11-15), mas o altar so fica parcialmente envolto
    expect(GROUND_FOG.max).toBeLessThanOrEqual(0.92);
    expect(GROUND_FOG.density).toBeGreaterThanOrEqual(0.07);
  });
});
