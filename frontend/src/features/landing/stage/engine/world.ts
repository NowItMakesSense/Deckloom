// O mundo do palco: floresta, clareira, deck box, cartas e tokens. Constroi a cena three uma vez e, a cada
// quadro, aplica a ela os numeros das funcoes puras (camera, fases, cartas, tokens). Sem logica de scroll aqui.
import {
  AdditiveBlending,
  BackSide,
  type Blending,
  type Object3D,
  BufferAttribute,
  BufferGeometry,
  Color,
  DirectionalLight,
  DoubleSide,
  Euler,
  FogExp2,
  Group,
  HemisphereLight,
  Mesh,
  MeshBasicMaterial,
  MeshPhysicalMaterial,
  MeshStandardMaterial,
  NormalBlending,
  PerspectiveCamera,
  PlaneGeometry,
  PointLight,
  Points,
  PointsMaterial,
  Quaternion,
  RingGeometry,
  Scene,
  ShaderMaterial,
  Shape,
  ShapeGeometry,
  SphereGeometry,
  SpotLight,
  RepeatWrapping,
  SRGBColorSpace,
  Texture,
  Vector3,
} from "three";

import { boardLayout } from "../board";
import { aperture, camPose } from "../camera";
import { frontLightIntensity } from "../fire";
import { CARD_COUNT, HOOK_COUNT, cardPose } from "../deck";
import { clamp, lerp, rng, smooth } from "../math";
import { fogBankOpacity, fogBanks, mistBands, mistDrift, mistOpacity } from "../mist";
import { FOREST, LIGHTS, MIST, NIGHT, SKY, WORLD } from "../palette";
import {
  BOXP,
  BOXT,
  BOX_HEIGHT,
  DY,
  PH,
  archRuneGlow,
  boxPhases,
  mesaK,
  pathRuneGlow,
} from "../phases";
import {
  ARCH_Z,
  trunkPlacements,
  GROUND_Y,
  archRunePlacements,
  fernPlacements,
  pathRunePlacements,
  type Pt,
} from "../silhouettes";
import {
  MANA_CHIPS,
  planTokens,
  simulateTokens,
  tokenPose,
  tokenTime,
  type GameId,
  type Token,
} from "../tokens";
import {
  type CardFace,
  type LogoImage,
  cardBackTexture,
  cardFrontTexture,
  glowTexture,
  manaTopTexture,
  medallionBottomTexture,
  medallionTopTexture,
  runeGlowTexture,
  sheenTexture,
  softTexture,
} from "./textures";
import { PROPS } from "../props";
import { MOON_DIR, MOON_RADIUS_DEG, moonLightPosition } from "../sky";
import { sceneryPlan, terrainHeight } from "../scenery";
import { buildScenery } from "./scenery3d";
import { installHeightFog } from "./heightFog";
import { buildCampfire } from "./campfire";
import {
  NATURE_NODES,
  PIECES_NODES,
  type StageModels,
  applyAltarGlow,
  applyDeckBoxPose,
  cloneWithOwnMaterials,
  materialNamed,
  replaceMaterial,
  rigAltar,
  rigDeckBox,
} from "./deckBoxModel";
import { buildNoticeBoard } from "./noticeBoard";

export interface WorldOptions {
  readonly mobile: boolean;
  readonly hooks: readonly CardFace[];
  readonly logos: Readonly<Record<GameId, LogoImage>>;
  /** Modelos glTF ja carregados (sob demanda, antes de montar o palco). */
  readonly models: StageModels;
}

export interface FrameResult {
  /** Distancia de foco da profundidade de campo e abertura. */
  readonly focus: number;
  readonly aperture: number;
}

export interface World {
  readonly scene: Scene;
  readonly camera: PerspectiveCamera;
  /** Malhas da floresta: oclusores para a auditoria de continuidade. */
  readonly occluders: Mesh[];
  readonly cards: Object3D[];
  readonly tokens: Token[];
  readonly tokenMeshes: Object3D[];
  update(raw: number, t: number): FrameResult;
  /** Depuracao: liga ou desliga so as faixas de nevoa (medir com e sem). */
  setMist(visible: boolean): void;
}

function toShape(points: readonly Pt[]): Shape {
  const s = new Shape();
  s.moveTo(points[0][0], points[0][1]);
  for (let i = 1; i < points.length; i++) s.lineTo(points[i][0], points[i][1]);
  s.closePath();
  return s;
}

const SKY_VERTEX =
  "varying vec3 vD; void main(){ vD = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }";
// ceu noturno: degrade, estrelas que cintilam (duas camadas), Via Lactea discreta, nuvens finas e a lua com halo
const SKY_FRAGMENT = `
uniform vec3 top, mid, hor, moon;
uniform float t, moonR;
uniform sampler2D moonMap;
varying vec3 vD;
float h31(vec3 p) { p = fract(p * vec3(443.897, 441.423, 437.195)); p += dot(p, p.yzx + 19.19); return fract((p.x + p.y) * p.z); }
float h21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float n2(vec2 p) {
  vec2 i = floor(p), f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(h21(i), h21(i + vec2(1, 0)), u.x), mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) { float v = 0.0, a = 0.5; for (int i = 0; i < 4; i++) { v += a * n2(p); p *= 2.07; a *= 0.5; } return v; }
// uma camada de estrelas: cada celula da esfera tem no maximo uma estrela, em posicao e brilho sorteados
float stars(vec3 d, float scale, float keep) {
  vec3 q = d * scale;
  vec3 c = floor(q);
  float h = h31(c);
  if (h < keep) return 0.0;
  vec3 off = vec3(h31(c + 7.1), h31(c + 3.7), h31(c + 1.3)) * 0.6 + 0.2;
  float r = length(fract(q) - off);
  float tw = 0.6 + 0.4 * sin(t * (1.3 + h * 3.7) + h * 61.0);
  return smoothstep(0.32, 0.0, r) * (h - keep) / (1.0 - keep) * tw;
}
// o disco da lua: mapa de cor real da NASA (LROC) projetado numa esfera, com o terminador suave de uma lua quase
// cheia e escurecimento na borda
vec3 moonDisc(vec3 d, out float cover) {
  float ang = acos(clamp(dot(d, moon), -1.0, 1.0));
  cover = smoothstep(moonR, moonR * 0.985, ang);
  if (cover <= 0.0) return vec3(0.0);
  vec3 u = normalize(cross(moon, vec3(0.0, 1.0, 0.0)));
  vec3 v = cross(u, moon);
  vec2 p = vec2(dot(d, u), dot(d, v)) / sin(moonR);
  vec3 n = vec3(p, sqrt(max(0.0, 1.0 - dot(p, p))));
  vec2 uv = vec2(0.5 + atan(n.x, n.z) / 6.2831853, 0.5 + asin(clamp(n.y, -1.0, 1.0)) / 3.1415927);
  vec3 alb = texture2D(moonMap, uv).rgb;
  float lit = smoothstep(-0.12, 0.35, dot(n, normalize(vec3(0.55, 0.2, 0.81))));
  return alb * vec3(1.05, 1.05, 1.12) * (0.18 + 1.05 * lit) * (0.82 + 0.18 * n.z);
}
void main() {
  vec3 d = normalize(vD);
  float h = d.y;
  vec3 c = h > 0.0 ? mix(mix(hor, mid, smoothstep(0.0, 0.18, h)), top, smoothstep(0.18, 0.6, h))
                   : mix(hor, vec3(0.08, 0.06, 0.15), smoothstep(0.0, 0.2, -h));
  float dm = max(dot(d, moon), 0.0);
  // Via Lactea: faixa larga e fraca ao longo de um grande circulo
  vec3 gal = normalize(vec3(0.35, 0.55, 0.76));
  float band = exp(-pow(dot(d, gal) / 0.2, 2.0)) * (0.55 + 0.45 * fbm(vec2(atan(d.z, d.x) * 4.0, d.y * 9.0)));
  c += vec3(0.32, 0.3, 0.55) * band * 0.09 * smoothstep(0.05, 0.4, h);
  // estrelas: somem no horizonte (bruma) e perto da lua (o luar ofusca)
  float fade = smoothstep(0.02, 0.22, h) * (1.0 - smoothstep(0.95, 0.997, dm));
  // tres camadas: poeira fina de estrelas, estrelas medias e as mais brilhantes (mais densas na Via Lactea)
  float s = stars(d, 640.0, 0.9 - band * 0.05) * 0.55 + stars(d, 320.0, 0.94 - band * 0.03) * 0.9
          + stars(d, 150.0, 0.975) * 1.3 + stars(d, 70.0, 0.988) * 1.8;
  // nuvens finas que passam devagar: escondem as estrelas e ganham borda clara perto da lua
  float cl = smoothstep(0.52, 0.8, fbm(vec2(atan(d.z, d.x) * 3.0 + t * 0.004, h * 7.0)));
  cl *= smoothstep(0.02, 0.12, h) * (1.0 - smoothstep(0.35, 0.6, h));
  c += vec3(0.85, 0.88, 1.0) * s * fade * (1.0 - cl);
  // halo da lua: um brilho largo e fraco no ar umido, sem anel duro
  c += vec3(0.5, 0.56, 0.8) * (pow(dm, 40.0) * 0.1 + pow(dm, 600.0) * 0.18);
  c = mix(c, c * 0.55 + vec3(0.2, 0.2, 0.3) * pow(dm, 8.0), cl * 0.7);
  float cover;
  vec3 md = moonDisc(d, cover);
  c = mix(c, md, cover * (1.0 - cl * 0.5));
  gl_FragColor = vec4(c, 1.0);
}
`;

/** A noite escurece as silhuetas; as janelas acesas (cores claras, #f...) continuam como estao. */
function nightColor(hex: string, keepLit: boolean): Color {
  const c = new Color(hex);
  return keepLit && /^#f/i.test(hex) ? c : c.multiplyScalar(NIGHT);
}

const axisX = new Vector3();
/**
 * Mantem o logo do medalhao de pe para quem olha a mesa (o topo da imagem para o fundo da cena, -z), qualquer que
 * seja o giro da ficha: gira o mapa da face em sentido contrario ao giro da ficha em torno do eixo vertical.
 */
export function uprightLogo(tex: Texture, q: Quaternion): void {
  axisX.set(1, 0, 0).applyQuaternion(q);
  const yaw = Math.atan2(-axisX.z, axisX.x);
  tex.center.set(0.5, 0.5);
  tex.rotation = yaw;
}

/** Mapa de cor da lua (NASA, LROC), que vem no nature.glb como o material do no MatMoon. */
function moonTexture(nature: Object3D): Texture | null {
  let map: Texture | null = null;
  nature.getObjectByName(NATURE_NODES.moon)?.traverse((o) => {
    if (!map && o instanceof Mesh && o.material instanceof MeshStandardMaterial && o.material.map)
      map = o.material.map;
  });
  if (map) (map as Texture).colorSpace = SRGBColorSpace;
  return map;
}

/** Densidade da nevoa exponencial: ~60% de nevoa a 100 m, quase tudo coberto a 170 m. */
export const FOG_DENSITY = 0.024;

export function buildWorld(options: WorldOptions): World {
  const { mobile } = options;
  const scene = new Scene();
  // nevoa densa (pedido do usuario, 2026-11-15): esconde o fundo distante; o ceu fica fora dela
  installHeightFog();
  scene.fog = new FogExp2(SKY.fog, FOG_DENSITY);
  const camera = new PerspectiveCamera(42, 1, 0.1, 1200);

  // ceu noturno com estrelas e a lua (sky.ts); a esfera acompanha a camera, entao a lua fica sempre na mesma direcao
  const skyMat = new ShaderMaterial({
    side: BackSide,
    depthWrite: false,
    fog: false,
    uniforms: {
      top: { value: new Color(SKY.top) },
      mid: { value: new Color(SKY.mid) },
      hor: { value: new Color(SKY.horizon) },
      moon: { value: new Vector3(...MOON_DIR) },
      moonR: { value: (MOON_RADIUS_DEG * Math.PI) / 180 },
      moonMap: { value: moonTexture(options.models.nature) },
      t: { value: 0 },
    },
    vertexShader: SKY_VERTEX,
    fragmentShader: SKY_FRAGMENT,
  });
  const skyDome = new Mesh(new SphereGeometry(900, 48, 24), skyMat);
  skyDome.name = "ceu";
  skyDome.renderOrder = -1;
  scene.add(skyDome);

  // luzes
  // a noite: as velas e as tochas viram a luz principal
  scene.add(new HemisphereLight(WORLD.hemiSky, WORLD.hemiGround, LIGHTS.hemi));
  // luar: a luz principal vem da direcao da lua (frio, rasante)
  const key = new DirectionalLight(SKY.moonLight, LIGHTS.sun);
  key.position.set(...moonLightPosition([0, 0, -0.4], 30));
  key.target.position.set(0, 0, -0.4);
  scene.add(key.target);
  key.castShadow = true;
  key.shadow.mapSize.set(2048, 2048);
  Object.assign(key.shadow.camera, { left: -9, right: 9, top: 9, bottom: -9, near: 1, far: 40 });
  key.shadow.bias = -0.0004;
  scene.add(key);
  const frontLight = new DirectionalLight(WORLD.frontLight, frontLightIntensity(0));
  frontLight.position.set(3, 5, 9);
  frontLight.userData.front = true;
  scene.add(frontLight);
  const rim = new PointLight(WORLD.rim, LIGHTS.rim, 18);
  rim.position.set(-5, 4, -4);
  scene.add(rim);
  const rimSpot = new SpotLight(SKY.sunLight, 90, 0, 0.55, 0.6, 1.4);
  rimSpot.position.set(1.5, 4.8 + DY, -5.5);
  rimSpot.target.position.set(0, 2.1 + DY, 0);
  scene.add(rimSpot, rimSpot.target);

  // chao e altar (a mesa); a deck box apoia direto no tampo (decisao do usuario, 2026-11-14)
  const ground = new Mesh(
    new PlaneGeometry(1400, 1400),
    new MeshStandardMaterial({ color: WORLD.ground, roughness: 1 }),
  );
  ground.rotation.x = -Math.PI / 2;
  ground.position.y = GROUND_Y;
  ground.receiveShadow = true;
  scene.add(ground);
  // altar de pedra em glTF (design/3d/altar.py): tampo plano em y = 0, circulo e entalhes no mapa emissivo
  const altar = rigAltar(options.models.altar);
  scene.add(altar.root);

  // fundo em 3D (scenery.ts): terreno com relevo, cristas, arvores, torres e ruinas, no lugar das silhuetas chapadas
  const occluders: Mesh[] = [];
  const scenery = buildScenery(
    scene,
    sceneryPlan(),
    options.models.nature,
    options.models.buildings,
  );
  occluders.push(...scenery.occluders);
  // portal de pedra em glTF (design/3d/portal.py): a camera atravessa o arco; tapa o que fica atras dele
  const portal = options.models.portal;
  portal.position.z = ARCH_Z;
  portal.traverse((o) => {
    if (!(o instanceof Mesh)) return;
    o.receiveShadow = true;
    occluders.push(o);
  });
  scene.add(portal);
  // pedras com musgo e tocos do Poly Haven (rocks.glb, stump.glb) no caminho e na borda da clareira
  for (const p of PROPS) {
    const src =
      p.kind === "rock"
        ? options.models.rocks.getObjectByName(`Rock${p.variant}`)
        : options.models.stump;
    if (!src) throw new Error(`modelo sem a pedra Rock${p.variant}`);
    const prop = src.clone();
    // enterrada um pouco: a pedra nasce do chao em vez de pousar nele (de longe parecia flutuar)
    prop.position.set(p.x, GROUND_Y - (p.kind === "rock" ? 0.22 : 0.05) * p.scale, p.z);
    prop.rotation.y = p.yaw;
    prop.scale.setScalar(p.scale);
    prop.userData.prop = true;
    prop.traverse((o) => {
      if (!(o instanceof Mesh)) return;
      o.receiveShadow = true;
      occluders.push(o);
    });
    scene.add(prop);
  }
  // troncos do portal de troncos em glTF (design/3d/trunk.py): copias do tronco de referencia (h 30, tw 1,6)
  for (const t of trunkPlacements()) {
    const trunk = options.models.trunk.clone();
    trunk.position.set(t.x, GROUND_Y + terrainHeight(t.x, t.z), t.z);
    trunk.scale.set(t.tw / 1.6, t.h / 30, t.tw / 1.6);
    trunk.rotation.y = t.x * 1.7; // cada copia mostra outro lado das raizes
    trunk.traverse((o) => {
      if (o instanceof Mesh) occluders.push(o);
    });
    scene.add(trunk);
  }

  // caminho de terra (pedido do usuario, 2026-11-15): reto, plano e continuo da entrada (z = 69) ate o altar (z = 5),
  // terra batida do mesmo scan do chao, mais escura e sem musgo; as bordas se misturam com o chao (vertice escurecido)
  const pathGeo = new PlaneGeometry(3.4, 64, 6, 64);
  const pathColors = new Float32Array(pathGeo.getAttribute("position").count * 3);
  const pp = pathGeo.getAttribute("position");
  for (let i = 0; i < pp.count; i++) {
    const edge = Math.abs(pp.getX(i)) / 1.7;
    const k = 1 - 0.35 * edge * edge;
    pathColors.set([k, k * 0.96, k * 0.92], i * 3);
  }
  pathGeo.setAttribute("color", new BufferAttribute(pathColors, 3));
  const groundMap = (
    options.models.nature
      .getObjectByName(NATURE_NODES.ground)
      ?.getObjectByProperty("type", "Mesh") as Mesh | undefined
  )?.material as MeshStandardMaterial | undefined;
  const dirtMap = groundMap?.map ? groundMap.map.clone() : null;
  if (dirtMap) {
    dirtMap.wrapS = RepeatWrapping;
    dirtMap.wrapT = RepeatWrapping;
    dirtMap.repeat.set(1.2, 22);
    dirtMap.needsUpdate = true;
  }
  const pathMesh = new Mesh(
    pathGeo,
    new MeshStandardMaterial({
      name: "Dirt",
      map: dirtMap,
      color: WORLD.dirt,
      roughness: 1,
      metalness: 0,
      vertexColors: true,
    }),
  );
  pathMesh.name = "caminho-de-terra";
  pathMesh.rotation.x = -Math.PI / 2;
  pathMesh.position.set(0, GROUND_Y + 0.012, 37);
  pathMesh.receiveShadow = true;
  scene.add(pathMesh);
  const fernMat = new MeshBasicMaterial({
    color: nightColor(FOREST.fern, false),
    fog: false,
    side: DoubleSide,
  });
  const ferns = fernPlacements().map((f) => {
    const m = new Mesh(new ShapeGeometry(f.leaves.map(toShape), 2), fernMat);
    m.name = "samambaia";
    m.position.set(f.x, GROUND_Y, f.z);
    m.scale.setScalar(f.scale);
    scene.add(m);
    return { mesh: m, phase: f.phase };
  });

  // runas do arco do portal e do caminho (a mesma escrita da caixa)
  const runeTextures: Texture[] = [0, 1, 2, 3, 4, 5].map(runeGlowTexture);
  const runeMesh = (size: number, k: number) =>
    new Mesh(
      new PlaneGeometry(size, size),
      new MeshBasicMaterial({
        map: runeTextures[k % 6],
        color: WORLD.runeGlow,
        transparent: true,
        opacity: 0,
        blending: AdditiveBlending,
        depthWrite: false,
        fog: false,
      }),
    );
  const archRunes = archRunePlacements().map((p, k) => {
    const m = runeMesh(0.85, k);
    m.position.set(p.x, p.y, p.z);
    scene.add(m);
    return m;
  });
  const pathRunes = pathRunePlacements().map((p, k) => {
    const m = runeMesh(1.15, k);
    m.rotation.x = -Math.PI / 2;
    m.position.set(p.x, p.y, p.z);
    scene.add(m);
    return m;
  });

  // aneis de luz no altar: um quando a tampa abre, outro quando o deck explode
  const ring = (color: string) => {
    const m = new Mesh(
      new RingGeometry(0.96, 1, 128),
      new MeshBasicMaterial({
        color,
        transparent: true,
        opacity: 0,
        blending: AdditiveBlending,
        depthWrite: false,
        side: DoubleSide,
        fog: false,
      }),
    );
    m.rotation.x = -Math.PI / 2;
    m.position.set(BOXP[0], 0.015, BOXP[2]);
    scene.add(m);
    return m;
  };
  const ringOpen = ring(WORLD.ringOpen);
  const ringBurst = ring(WORLD.ringBurst);

  // raios de luz
  const shaftTex = softTexture("shaft");
  const rs = rng(130);
  const shafts = [
    [-9, -12],
    [5, -32],
    [12, 8],
    [-14, 28],
    [3, 62],
    [-6, -48],
    [9, -78],
    [-2, 18],
    [7, 46],
  ].map(([x, z], k) => {
    const m = new Mesh(
      new PlaneGeometry(3 + rs() * 3, 46),
      new MeshBasicMaterial({
        map: shaftTex,
        color: WORLD.shaft,
        transparent: true,
        opacity: 0.06,
        blending: AdditiveBlending,
        depthWrite: false,
        fog: false,
      }),
    );
    m.position.set(x, 18, z);
    m.rotation.z = 0.38;
    scene.add(m);
    return { mesh: m, phase: k };
  });
  // faixas de bruma noturna entre as camadas de silhueta (mistura normal, opacidade moderada)
  const mistTex = softTexture("mist");
  const mists = mistBands().map((band) => {
    const m = new Mesh(
      new PlaneGeometry(band.w, band.h),
      new MeshBasicMaterial({
        map: mistTex,
        color: MIST.color,
        transparent: true,
        opacity: mistOpacity(band, 0),
        blending: NormalBlending,
        depthWrite: false,
        fog: false,
      }),
    );
    m.position.set(band.x, band.y, band.z);
    scene.add(m);
    return { mesh: m, band };
  });

  // bancos de nevoa volumetrica (pedido do usuario, 2026-11-15): veus baixos ao longo do caminho, entre as arvores e
  // em volta do altar, sempre de frente para a camera e sumindo de perto
  const banks = fogBanks().map((bank) => {
    const m = new Mesh(
      new PlaneGeometry(bank.w, bank.h),
      new MeshBasicMaterial({
        map: mistTex,
        color: MIST.color,
        transparent: true,
        opacity: bank.opacity,
        blending: NormalBlending,
        depthWrite: false,
        fog: false,
      }),
    );
    m.name = "banco-de-nevoa";
    m.position.set(bank.x, GROUND_Y + bank.y + bank.h / 2, bank.z);
    m.renderOrder = 1;
    scene.add(m);
    return { mesh: m, bank };
  });

  // particulas: poeira, folhas, vaga-lumes e a magia que sai da caixa
  const dotTex = softTexture("dot");
  const leafTex = softTexture("leaf");
  const makePoints = (
    n: number,
    color: string,
    size: number,
    map: Texture,
    blending: Blending = AdditiveBlending,
  ) => {
    const g = new BufferGeometry();
    g.setAttribute("position", new BufferAttribute(new Float32Array(n * 3), 3));
    const p = new Points(
      g,
      new PointsMaterial({
        color,
        size,
        map,
        transparent: true,
        depthWrite: false,
        blending,
        opacity: 0.9,
      }),
    );
    p.frustumCulled = false;
    scene.add(p);
    return {
      points: p,
      array: g.attributes.position.array as Float32Array,
      material: p.material as PointsMaterial,
    };
  };
  const pr = rng(5);
  const ND = mobile ? 900 : 1800;
  const dust = makePoints(ND, WORLD.dust, 0.07, dotTex);
  for (let i = 0; i < ND; i++) {
    dust.array[i * 3] = (pr() - 0.5) * 80;
    dust.array[i * 3 + 1] = 0.3 + pr() * 22;
    dust.array[i * 3 + 2] = -40 + pr() * 190;
  }
  const NL = 120;
  const leaves = makePoints(NL, WORLD.leaf, 0.16, leafTex, NormalBlending);
  const lseed = Array.from({ length: NL }, () => [pr(), pr(), pr(), pr()]);
  const NF = 140;
  const flies = makePoints(NF, WORLD.firefly, 0.16, dotTex);
  const fseed = Array.from({ length: NF }, () => {
    const a = pr() * 6.28;
    const d = 2.5 + pr() * 9;
    return [Math.cos(a) * d, 0.4 + pr() * 4.5, BOXP[2] + Math.sin(a) * d * 0.8, pr() * 6.28];
  });
  const NM = 180;
  const magic = makePoints(NM, WORLD.magic, 0.1, dotTex);
  const mseed = Array.from({ length: NM }, () => [pr(), pr(), pr()]);

  // deck box de madeira entalhada: modelo glTF proprio (design/3d/deckbox.py), animado pelos nos
  const BH = BOX_HEIGHT;
  const BW = 1.5;
  const box = new Group();
  box.position.set(BOXP[0], BOXP[1], BOXP[2]);
  scene.add(box);
  const deckBox = rigDeckBox(options.models.deckBox);
  box.add(deckBox.root);
  const inner = new PointLight(WORLD.innerLight, 0, 9, 1.6);
  inner.position.set(0, BH * 0.7, 0);
  box.add(inner);
  const glow = new Mesh(
    new PlaneGeometry(BW * 2.6, BW * 2.6),
    new MeshBasicMaterial({
      map: glowTexture(),
      transparent: true,
      opacity: 0,
      blending: AdditiveBlending,
      depthWrite: false,
      fog: false,
    }),
  );
  glow.position.set(0, BH + 0.2, 0);
  box.add(glow);

  // cartas (pieces.glb: cantos arredondados e borda chanfrada): as quatro primeiras com a face dos ganchos, o resto
  // de costas. Foil: a frente do gancho e um material fisico com iridescencia, que acende junto com o brilho dourado.
  const cardSrc = options.models.pieces.getObjectByName(PIECES_NODES.card);
  if (!cardSrc) throw new Error("modelo sem a carta");
  // texturas de canvas em malhas do glTF: o UV do glTF ja vem com v para baixo, entao sem flipY
  const gltfMap = <T extends Texture>(t: T): T => {
    t.flipY = false;
    t.needsUpdate = true;
    return t;
  };
  const backMap = gltfMap(cardBackTexture());
  const backMat = new MeshStandardMaterial({ name: "CardBack", map: backMap, roughness: 0.55 });
  // cartas de costas: a frente tambem mostra o verso (material proprio, com o nome da face do modelo)
  const backFaceMat = new MeshStandardMaterial({ name: "CardFace", map: backMap, roughness: 0.55 });
  const sheenBase = sheenTexture();
  const sheenGeo = new PlaneGeometry(1.26, 1.76);
  const cards: Object3D[] = [];
  const sheens: Array<Mesh | undefined> = [];
  const foils: Array<MeshPhysicalMaterial | undefined> = [];
  for (let i = 0; i < CARD_COUNT; i++) {
    const hook = i < HOOK_COUNT;
    const card = cardSrc.clone();
    card.traverse((o) => {
      if (o instanceof Mesh) o.castShadow = true;
    });
    replaceMaterial(card, "CardBack", backMat);
    if (hook) {
      const foil = new MeshPhysicalMaterial({
        name: "CardFace",
        map: gltfMap(cardFrontTexture(options.hooks[i], i)),
        roughness: 0.45,
        iridescence: 0,
        iridescenceIOR: 1.7,
        iridescenceThicknessRange: [180, 520],
      });
      replaceMaterial(card, "CardFace", foil);
      foils.push(foil);
      const sheenMap = sheenBase.clone();
      sheenMap.needsUpdate = true;
      const sheen = new Mesh(
        sheenGeo,
        new MeshBasicMaterial({
          map: sheenMap,
          transparent: true,
          opacity: 0,
          blending: AdditiveBlending,
          depthWrite: false,
        }),
      );
      sheen.position.z = 0.0086;
      card.add(sheen);
      sheens.push(sheen);
    } else {
      replaceMaterial(card, "CardFace", backFaceMat);
      foils.push(undefined);
      sheens.push(undefined);
    }
    cards.push(card);
    scene.add(card);
  }

  // tokens: medalhoes com o logo oficial e fichas de mana
  const tokens = planTokens(mesaK(mobile));
  simulateTokens(tokens);
  // pieces.glb: medalhao de aro serrilhado e ficha com faixas; cada copia recebe a propria textura na face
  const medalSrc = options.models.pieces.getObjectByName(PIECES_NODES.medal);
  const chipSrc = options.models.pieces.getObjectByName(PIECES_NODES.chip);
  if (!medalSrc || !chipSrc) throw new Error("modelo sem o medalhao ou a ficha");
  const medalBack = gltfMap(medallionBottomTexture());
  const tokenFaces: Array<Texture | null> = [];
  const tokenMeshes: Object3D[] = tokens.map((k) => {
    const medal = k.kind === "medal";
    const obj = cloneWithOwnMaterials(medal ? medalSrc : chipSrc);
    const face = materialNamed(obj, medal ? "MedalFace" : "ChipFace");
    face.map = gltfMap(
      medal
        ? medallionTopTexture(options.logos[k.game as GameId])
        : manaTopTexture(k.mana ?? MANA_CHIPS[0]),
    );
    face.color.setScalar(1);
    tokenFaces.push(medal ? face.map : null);
    face.needsUpdate = true;
    if (medal) {
      const back = materialNamed(obj, "MedalBack");
      back.map = medalBack;
      back.color.setScalar(1);
      back.needsUpdate = true;
    }
    obj.traverse((o) => {
      if (!(o instanceof Mesh)) return;
      o.castShadow = true;
      o.receiveShadow = true;
    });
    obj.visible = false;
    scene.add(obj);
    return obj;
  });

  // a noite: velas, tochas e o mural de avisos dos planos
  const board = boardLayout(mobile);
  buildNoticeBoard(scene, board, options.models.noticeBoard);
  const campfire = buildCampfire(scene, {
    mobile,
    tokens,
    dotTex,
    board,
    candlestick: options.models.candlestick,
    torches: options.models.torches,
  });

  const qA = new Quaternion();
  const qB = new Quaternion();
  const qC = new Quaternion();
  const eul = new Euler();
  const axis = new Vector3();
  const up = new Vector3(0, 1, 0);
  const tmp = new Vector3();

  function update(raw: number, t: number): FrameResult {
    const c = camPose(raw, t, mobile, camera.aspect);
    const ph = boxPhases(raw, t);
    camera.position.set(c.pos[0], c.pos[1], c.pos[2]);
    camera.lookAt(c.tgt[0], c.tgt[1], c.tgt[2]);
    camera.rotateZ(c.roll);
    camera.updateMatrixWorld();
    skyDome.position.copy(camera.position);
    skyMat.uniforms.t.value = t;

    let focus = camera.position.distanceTo(tmp.set(c.tgt[0], c.tgt[1], c.tgt[2]));
    if (c.active >= 0)
      focus = lerp(focus, camera.position.distanceTo(cards[c.active].position), c.cardFocus);
    if (c.boxFocus) focus = camera.position.distanceTo(tmp.set(BOXT[0], BOXT[1] + 0.5, BOXT[2]));

    // runas do arco e rastro do caminho
    const cz = camera.position.z;
    archRunes.forEach(
      (m, k) => ((m.material as MeshBasicMaterial).opacity = archRuneGlow(raw, cz, k)),
    );
    pathRunes.forEach(
      (m) => ((m.material as MeshBasicMaterial).opacity = pathRuneGlow(raw, cz, m.position.z, t)),
    );

    // altar, aneis e samambaias reagem
    applyAltarGlow(altar, ph.altarGlow);
    ringOpen.scale.setScalar(ph.ringOpen.scale);
    (ringOpen.material as MeshBasicMaterial).opacity = ph.ringOpen.opacity;
    ringBurst.scale.setScalar(ph.ringBurst.scale);
    (ringBurst.material as MeshBasicMaterial).opacity = ph.ringBurst.opacity;
    for (const f of ferns) {
      f.mesh.rotation.z =
        Math.sin(t * 0.9 + f.phase) * 0.06 +
        Math.sin(t * 2.1 + f.phase) * 0.02 * (1 + ph.awake * 2) +
        ph.burst * 0.25 * Math.sign(f.mesh.position.x);
    }

    // ambiente
    for (const s of shafts)
      (s.mesh.material as MeshBasicMaterial).opacity =
        LIGHTS.shaft + Math.sin(t * 0.4 + s.phase) * LIGHTS.shaftWave;
    for (const m of mists) {
      m.mesh.position.x = mistDrift(m.band, t);
      (m.mesh.material as MeshBasicMaterial).opacity = mistOpacity(
        m.band,
        ph.burst,
        camera.position.z - m.band.z,
      );
    }
    for (const b of banks) {
      b.mesh.position.x = b.bank.x + Math.sin(t * 0.06 + b.bank.phase) * 1.5;
      b.mesh.rotation.y = Math.atan2(
        camera.position.x - b.mesh.position.x,
        camera.position.z - b.mesh.position.z,
      );
      (b.mesh.material as MeshBasicMaterial).opacity = fogBankOpacity(
        b.bank,
        camera.position.distanceTo(b.mesh.position),
      );
    }
    const wind = 1 + ph.awake * 1.5 + ph.burst * 2;
    const cp = camera.position;
    for (let li = 0; li < NL; li++) {
      const sd = lseed[li];
      const prog = (t * 0.03 * (0.6 + sd[3]) * wind + sd[0]) % 1;
      const lx = cp.x - 9 + prog * 18 + Math.sin(t * 1.3 + li) * 0.4;
      const ly = cp.y + 2.5 - sd[1] * 5 - prog * 2.5 + Math.sin(t * 0.9 + li) * 0.3;
      const lz = cp.z - 6 - sd[2] * 18;
      const bx = lx - BOXP[0];
      const bz = lz - BOXP[2];
      const bd = Math.hypot(bx, bz) || 1;
      const push = ph.burst * 7 * Math.max(0, 1 - bd / 12);
      leaves.array[li * 3] = lx + (bx / bd) * push;
      leaves.array[li * 3 + 1] = ly + push * 0.3;
      leaves.array[li * 3 + 2] = lz + (bz / bd) * push;
    }
    leaves.points.geometry.attributes.position.needsUpdate = true;
    for (let fi = 0; fi < NF; fi++) {
      const fs = fseed[fi];
      const hx = fs[0] + Math.sin(t * 0.6 + fs[3]) * 0.6;
      const hy = fs[1] + Math.sin(t * 0.9 + fs[3] * 2) * 0.4;
      const hz = fs[2] + Math.cos(t * 0.5 + fs[3]) * 0.6;
      const sp = (1 - ph.conv) * 1.0 + 0.15;
      const ang = t * 1.2 + fs[3];
      flies.array[fi * 3] = lerp(hx, BOXT[0] + Math.cos(ang) * sp, ph.conv);
      flies.array[fi * 3 + 1] = lerp(hy, BOXT[1] + 0.6 + Math.sin(t * 2 + fs[3]) * 0.3, ph.conv);
      flies.array[fi * 3 + 2] = lerp(hz, BOXT[2] + Math.sin(ang) * sp, ph.conv);
    }
    flies.points.geometry.attributes.position.needsUpdate = true;
    flies.material.opacity =
      0.9 * (0.6 + 0.4 * Math.sin(t * 3)) * (1 - smooth((raw - PH.open) / 0.02));
    for (let mi = 0; mi < NM; mi++) {
      const ms = mseed[mi];
      const prog = (t * 0.22 * (0.5 + ms[2]) + ms[0]) % 1;
      const a2 = ms[1] * 6.283 + prog * 3;
      magic.array[mi * 3] = BOXP[0] + Math.cos(a2) * (0.2 + prog * 1.2) * 0.6;
      magic.array[mi * 3 + 1] = BOXP[1] + BH - 0.2 + prog * 4.2;
      magic.array[mi * 3 + 2] = BOXP[2] + Math.sin(a2) * (0.2 + prog * 1.2) * 0.45;
    }
    magic.points.geometry.attributes.position.needsUpdate = true;
    magic.material.opacity = ph.light * 0.9;
    dust.material.opacity = LIGHTS.dust * (1 - 0.5 * smooth((raw - PH.intro) / 0.1));

    // deck box: runas, fecho, travas, tampa, luz
    applyDeckBoxPose(deckBox, ph);
    inner.intensity = ph.light * 32;
    (glow.material as MeshBasicMaterial).opacity = ph.light * 0.9;
    glow.quaternion.copy(camera.quaternion);
    rimSpot.intensity = 70 * Math.max(ph.float, raw > PH.intro && raw < PH.story1 ? 0.5 : 0.25);
    // os ganchos continuam legiveis: a luz frontal sobe so enquanto as cartas viram
    frontLight.intensity = frontLightIntensity(raw);
    campfire.update(raw, t, ph.burst, camera);
    scenery.setTime(t);

    // cartas
    cards.forEach((mesh, i) => {
      const pose = cardPose(raw, i, { t, mobile, phases: ph });
      mesh.visible = pose.visible;
      mesh.scale.setScalar(Math.max(1e-4, pose.scale));
      mesh.position.set(pose.pos[0], pose.pos[1], pose.pos[2]);
      mesh.rotation.set(pose.rot[0], pose.rot[1], pose.rot[2]);
      const sheen = sheens[i];
      if (sheen && pose.sheen) {
        const material = sheen.material as MeshBasicMaterial;
        if (material.map) material.map.offset.x = pose.sheen.offset;
        material.opacity = pose.sheen.opacity;
      }
      const foil = foils[i];
      if (foil) foil.iridescence = pose.sheen?.opacity ?? 0;
    });

    // tokens: entram em arco e depois seguem a simulacao
    const tau = tokenTime(raw);
    tokens.forEach((k, i) => {
      const mesh = tokenMeshes[i];
      const pose = tokenPose(k, tau);
      mesh.visible = pose.visible;
      if (!pose.visible) return;
      mesh.scale.setScalar(Math.max(1e-4, pose.scale));
      mesh.position.set(pose.pos[0], pose.pos[1], pose.pos[2]);
      axis.set(Math.cos(pose.phi), 0, Math.sin(pose.phi));
      qB.setFromAxisAngle(axis, pose.tilt);
      qC.setFromAxisAngle(up, pose.yaw);
      qA.copy(qB).multiply(qC);
      eul.set(pose.spin[0], pose.spin[1], pose.spin[2]);
      qB.setFromEuler(eul);
      mesh.quaternion.copy(qA).multiply(qB);
      const logo = tokenFaces[i];
      if (logo) uprightLogo(logo, mesh.quaternion);
    });

    // no celular o desfoque e bem mais suave: a tela e pequena e o cartao HTML ja cobre a parte de baixo
    return { focus: clamp(focus, 0.3, 400), aperture: aperture(raw) * (mobile ? 0.45 : 1) };
  }

  function setMist(visible: boolean): void {
    for (const m of mists) m.mesh.visible = visible;
    for (const b of banks) b.mesh.visible = visible;
  }

  return { scene, camera, occluders, cards, tokens, tokenMeshes, update, setMist };
}
