// Chama procedural das velas e tochas (REQ-L10; refeita na T-L05). Tecnica classica de fogo em billboard: ruido fbm
// que sobe com o tempo, com a distorcao crescendo na altura (pe firme, labaredas no topo), recortado num perfil de
// gota e pintado por uma rampa de calor de brasa ate o branco. A cena gira o plano em Y para a camera
// (fire.flameFrame). Sem textura e sem imagem externa.
import { AdditiveBlending, DoubleSide, ShaderMaterial } from "three";

/** Quanto o plano da chama e mais largo que a lingua de fogo (o resto e espaco para as labaredas). */
export const FLAME_PLANE_WIDTH = { candle: 1.15, torch: 1.7 } as const;

const VERTEX = [
  "uniform float t, seed, lean, amp; varying vec2 vUv;",
  "void main(){ vUv = uv; vec3 p = position; float y = uv.y * uv.y;",
  "  p.x += (sin(t * 5.1 + seed) * 0.5 + sin(t * 11.3 + seed * 1.7) * 0.2) * amp * y + lean * y;",
  "  gl_Position = projectionMatrix * modelViewMatrix * vec4(p, 1.0); }",
].join("\n");

const FRAGMENT = `
uniform float t, seed, hot, candle;
varying vec2 vUv;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
  float v = 0.0, a = 0.5;
  for (int i = 0; i < 5; i++) { v += a * noise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
  return v;
}

void main() {
  float y = vUv.y;
  float wide = mix(${FLAME_PLANE_WIDTH.torch.toFixed(2)}, ${FLAME_PLANE_WIDTH.candle.toFixed(2)}, candle);
  float x = (vUv.x - 0.5) * 2.0 * wide;
  float speed = mix(2.6, 1.4, candle);
  // dois ruidos subindo; o segundo e deslocado pelo primeiro (turbulencia)
  vec2 q = vec2(x * 1.3, y * 2.4 - t * speed) + seed * 3.7;
  float n = fbm(q * vec2(1.8, 1.4));
  float n2 = fbm(q * 2.9 + vec2(n * 1.6, -t * 1.1));
  // a distorcao cresce com a altura: pe firme, labaredas no topo
  float dx = (n2 - 0.5) * (0.18 + 1.1 * y * y) * mix(1.0, 0.35, candle);
  // perfil de gota: base arredondada, ponta fina
  float w = mix(0.78, 0.05, pow(y, mix(0.85, 0.75, candle))) * smoothstep(0.0, 0.16, y + 0.05) + 0.0001;
  float d = abs(x + dx) / w;
  float body = 1.0 - smoothstep(0.45, 1.0, d);
  // o topo se desfaz em linguas pelo ruido
  float cut = y + (n - 0.5) * mix(0.75, 0.25, candle) * (0.25 + y);
  float heat = body * (1.0 - smoothstep(0.42, 1.0, cut)) * smoothstep(0.0, 0.06, y);
  // no alto o ruido abre vaos: a chama se parte em linguas
  heat *= mix(1.0, smoothstep(0.3, 0.62, n2 + (1.0 - y) * 0.55), 1.0 - candle * 0.7);
  // rampa de calor: brasa, laranja, amarelo; o miolo quase branco so no pe da chama
  float h = clamp(heat * 1.05, 0.0, 1.0) * (0.72 + 0.28 * smoothstep(0.75, 0.1, y));
  vec3 col = mix(vec3(0.4, 0.035, 0.0), vec3(0.95, 0.26, 0.02), smoothstep(0.0, 0.3, h));
  col = mix(col, vec3(1.0, 0.58, 0.12), smoothstep(0.3, 0.62, h));
  col = mix(col, vec3(1.0, 0.86, 0.5), smoothstep(0.62, 0.86, h));
  col = mix(col, vec3(1.0, 0.97, 0.88), smoothstep(0.86, 1.0, h) * smoothstep(0.6, 0.12, y));
  // o pe azul da chama da vela
  col = mix(col, vec3(0.35, 0.45, 1.0), candle * smoothstep(0.16, 0.02, y) * 0.55 * (1.0 - h));
  float a = smoothstep(0.02, 0.45, heat);
  gl_FragColor = vec4(col * hot * a * 0.9, a);
}
`;

export function flameMaterial(seed: number, candle: boolean, width: number): ShaderMaterial {
  return new ShaderMaterial({
    transparent: true,
    depthWrite: false,
    blending: AdditiveBlending,
    fog: false,
    side: DoubleSide,
    uniforms: {
      t: { value: 0 },
      seed: { value: seed },
      lean: { value: 0 },
      amp: { value: width * (candle ? 0.32 : 0.12) },
      hot: { value: 1 },
      candle: { value: candle ? 1 : 0 },
    },
    vertexShader: VERTEX,
    fragmentShader: FRAGMENT,
  });
}
