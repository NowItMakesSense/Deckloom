// Profundidade de campo em pos-processamento (REQ-L10): a cena vai para um alvo com textura de profundidade e um
// passe em tela cheia desfoca por distancia ao plano de foco. Sem tonemapping: as cores da paleta saem exatas.
import {
  Camera,
  DepthTexture,
  HalfFloatType,
  Mesh,
  PlaneGeometry,
  Scene,
  ShaderMaterial,
  UnsignedIntType,
  Vector2,
  WebGLRenderTarget,
} from "three";

const VERTEX =
  "varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }";

const FRAGMENT = [
  "uniform sampler2D tColor; uniform sampler2D tDepth; uniform float near, far, focus, aperture, maxBlur; uniform vec2 texel; varying vec2 vUv;",
  "float dist(vec2 uv){ float d = texture2D(tDepth, uv).x; return near * far / (far - d * (far - near)); }",
  "float coc(float z){ return clamp(abs(z - focus) / z * aperture, 0.0, maxBlur); }",
  "void main(){",
  "  float c0 = coc(dist(vUv)); vec3 acc = texture2D(tColor, vUv).rgb; float wsum = 1.0;",
  "  for (int i = 0; i < 28; i++) { float fi = float(i); float r = sqrt((fi + 0.5) / 28.0); float a = fi * 2.39996;",
  "    vec2 uv = vUv + vec2(cos(a), sin(a)) * r * c0 * texel; float cs = coc(dist(uv));",
  "    float w = smoothstep(0.0, 1.0, cs / max(c0, 0.001) + 0.25); acc += texture2D(tColor, uv).rgb * w; wsum += w; }",
  "  vec2 q = vUv - 0.5; float vig = 1.0 - dot(q, q) * 0.9;",
  "  gl_FragColor = vec4(acc / wsum * vig, 1.0);",
  "  #include <colorspace_fragment>",
  "}",
].join("\n");

export interface DepthOfField {
  readonly target: WebGLRenderTarget;
  readonly scene: Scene;
  readonly camera: Camera;
  setSize(width: number, height: number, pixelRatio: number): void;
  setFocus(focus: number, aperture: number): void;
  dispose(): void;
}

export function createDepthOfField(): DepthOfField {
  const target = new WebGLRenderTarget(1, 1, { type: HalfFloatType });
  target.depthTexture = new DepthTexture(1, 1);
  target.depthTexture.type = UnsignedIntType;
  const material = new ShaderMaterial({
    uniforms: {
      tColor: { value: target.texture },
      tDepth: { value: target.depthTexture },
      near: { value: 0.1 },
      far: { value: 1200 },
      focus: { value: 8 },
      aperture: { value: 9 },
      maxBlur: { value: 14 },
      texel: { value: new Vector2() },
    },
    vertexShader: VERTEX,
    fragmentShader: FRAGMENT,
  });
  const geometry = new PlaneGeometry(2, 2);
  const quad = new Mesh(geometry, material);
  quad.frustumCulled = false;
  const scene = new Scene();
  scene.add(quad);
  return {
    target,
    scene,
    camera: new Camera(),
    setSize(width, height, pixelRatio) {
      target.setSize(Math.floor(width * pixelRatio), Math.floor(height * pixelRatio));
      material.uniforms.texel.value.set(1 / (width * pixelRatio), 1 / (height * pixelRatio));
      material.uniforms.maxBlur.value = 14 * pixelRatio;
    },
    setFocus(focus, aperture) {
      material.uniforms.focus.value = focus;
      material.uniforms.aperture.value = aperture;
    },
    dispose() {
      target.dispose();
      target.depthTexture?.dispose();
      material.dispose();
      geometry.dispose();
    },
  };
}
