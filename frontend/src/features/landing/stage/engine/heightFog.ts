// Nevoa de chao (pedido do usuario, 2026-11-15). Tecnica dos exemplos de nevoa de altura do three.js (MIT): os
// pedacos de shader de nevoa recebem a posicao no mundo e somam, a nevoa de distancia, uma camada baixa que fica mais
// densa perto do chao e varia em manchas. Vale para todo material com `fog: true`; o ceu e o fogo ficam fora.
import { ShaderChunk } from "three";

/** Camada de nevoa de chao: altura da base (y), queda com a altura (m), densidade por metro e teto da mistura. */
export const GROUND_FOG = { base: 0.1, falloff: 2.2, density: 0.08, max: 0.9 } as const;

const MARK = "/* nevoa de chao */";

export function installHeightFog(): void {
  if (ShaderChunk.fog_fragment.includes(MARK)) return;
  const f = (n: number) => n.toFixed(4);
  ShaderChunk.fog_pars_vertex += `\n${MARK}\n#ifdef USE_FOG\n  varying vec3 vFogWorld;\n#endif\n`;
  ShaderChunk.fog_vertex += `
#ifdef USE_FOG
  vFogWorld = cameraPosition + transpose(mat3(viewMatrix)) * mvPosition.xyz;
#endif
`;
  ShaderChunk.fog_pars_fragment += `\n${MARK}\n#ifdef USE_FOG\n  varying vec3 vFogWorld;\n#endif\n`;
  ShaderChunk.fog_fragment = `${MARK}
#ifdef USE_FOG
  #ifdef FOG_EXP2
    float fogFactor = 1.0 - exp( - fogDensity * fogDensity * vFogDepth * vFogDepth );
  #else
    float fogFactor = smoothstep( fogNear, fogFar, vFogDepth );
  #endif
  #define GROUND_FOG_DENSITY ${f(GROUND_FOG.density)}
  float gfLayer = exp( - max( vFogWorld.y - ${f(GROUND_FOG.base)}, 0.0 ) / ${f(GROUND_FOG.falloff)} );
  float gfPatch = 0.6 + 0.4 * sin( vFogWorld.x * 0.11 + sin( vFogWorld.z * 0.07 ) * 2.0 ) * sin( vFogWorld.z * 0.09 + 1.3 );
  float gfAmount = ( 1.0 - exp( - vFogDepth * GROUND_FOG_DENSITY * gfLayer * gfPatch ) ) * ${f(GROUND_FOG.max)};
  fogFactor = max( fogFactor, gfAmount );
  gl_FragColor.rgb = mix( gl_FragColor.rgb, fogColor, fogFactor );
#endif
`;
}
