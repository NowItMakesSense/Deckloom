// O fundo em 3D na cena (T-L05): terreno com relevo, cristas de montanha e de copas, pinheiros, arvores gigantes,
// copas, torres com janelas acesas, o muro de ruinas e as colunas do caminho. As posicoes vem de scenery.ts (puro);
// aqui so a geometria instanciada e os materiais. A distancia e feita a mao: cada camada puxa o material para a cor
// da silhueta que ela substituiu (a nevoa da cena para em 175 m; as montanhas estao a 260).
import {
  BufferAttribute,
  DoubleSide,
  BufferGeometry,
  Color,
  InstancedMesh,
  Matrix4,
  Mesh,
  MeshLambertMaterial,
  MeshStandardMaterial,
  Object3D,
  PlaneGeometry,
  Quaternion,
  RepeatWrapping,
  Vector3,
  type Material,
  type Scene,
} from "three";

import { FOREST, NIGHT } from "../palette";
import { SCENERY_LAYERS, type SceneryPlan, terrainHeight } from "../scenery";
import { GROUND_Y, RUIN_WALL_Z } from "../silhouettes";
import { BUILDINGS_NODES, NATURE_NODES } from "./deckBoxModel";

/** Cor da silhueta de cada camada (a mesma da floresta chapada), ja na noite. */
const LAYER_COLOR: Record<string, string> = {
  montanhas: FOREST.mount,
  cidade: FOREST.city,
  gigantes: FOREST.giants,
  anel: FOREST.ring,
  "muro-de-ruinas": FOREST.ruin,
  "portal-de-troncos": FOREST.portal,
  "ruinas-do-caminho": FOREST.ruins2,
  corredor: FOREST.corridor,
  "dossel-fundo": FOREST.canopyB,
  "dossel-frente": FOREST.canopyA,
};

export interface Scenery {
  /** Malhas solidas (tapam o que fica atras: auditoria de continuidade). */
  readonly occluders: Mesh[];
  /** Relogio do vento nas folhas (s). */
  setTime(t: number): void;
}

/** Variante deterministica (0 a n-1) para a posicao: cada arvore pega um dos modelos. */
export function variantFor(x: number, z: number, n: number): number {
  const s = Math.sin(x * 12.9898 + z * 78.233) * 43758.5453;
  return Math.floor((s - Math.floor(s)) * n);
}

function node(root: Object3D, name: string): Object3D {
  const n = root.getObjectByName(name);
  if (!n) throw new Error(`modelo sem o no ${name}`);
  return n;
}

function firstMaterial(root: Object3D, name: string): MeshStandardMaterial {
  let found: MeshStandardMaterial | undefined;
  node(root, name).traverse((o) => {
    if (!found && o instanceof Mesh && o.material instanceof MeshStandardMaterial)
      found = o.material;
  });
  if (!found) throw new Error(`modelo sem material em ${name}`);
  return found;
}

/** Material da camada: a distancia leva a cor para a da silhueta; janelas acesas continuam acesas. */
function hazed(src: Material, layer: string, cache: Map<string, Material>): Material {
  const key = `${src.uuid}:${layer}`;
  const hit = cache.get(key);
  if (hit) return hit;
  const m = (src as MeshStandardMaterial).clone();
  const haze = SCENERY_LAYERS.find((l) => l.id === layer)?.haze ?? 0;
  const tint = new Color(LAYER_COLOR[layer] ?? FOREST.corridor).multiplyScalar(NIGHT);
  // a nevoa densa da cena (FogExp2) faz a distancia; aqui so um toque da cor da camada, para o fundo nao ficar preto
  if (m.name !== "WindowGlow") {
    m.color.multiplyScalar(1 - haze * 0.35);
    m.emissive = tint.clone().multiplyScalar(haze * 0.25);
    m.emissiveIntensity = 1;
  }
  m.fog = true;
  // cascas mais escuras, como a folhagem (pedido do usuario, 2026-11-15)
  if (m.name === "Bark") m.color.multiplyScalar(0.55);
  // folhas: material so difuso (Lambert), sem o brilho especular rasante que virava riscos claros nos cartoes de lado
  if (m.name === "Leaves") {
    const leaf = new MeshLambertMaterial({
      name: "Leaves",
      map: m.map,
      color: m.color,
      emissive: m.emissive,
      alphaTest: 0.5,
      side: DoubleSide,
      fog: true,
    });
    cache.set(key, leaf);
    return leaf;
  }
  cache.set(key, m);
  return m;
}

/** Vento: as folhas balancam devagar, mais no alto; cada arvore com a sua fase (posicao da instancia). */
function windy(m: Material, time: { value: number }, amp: number): Material {
  const mat = m as MeshLambertMaterial;
  if (mat.name !== "Leaves") return m;
  mat.onBeforeCompile = (shader) => {
    shader.uniforms.uWind = time;
    shader.vertexShader = `uniform float uWind;
${shader.vertexShader}`.replace(
      "#include <begin_vertex>",
      `#include <begin_vertex>
      #ifdef USE_INSTANCING
        float ph = instanceMatrix[3].x * 0.13 + instanceMatrix[3].z * 0.07;
      #else
        float ph = 0.0;
      #endif
      float sw = max(transformed.y, 0.0) * ${amp.toFixed(4)};
      transformed.x += sin(uWind * 1.1 + ph + transformed.y * 0.3) * sw;
      transformed.z += cos(uWind * 0.9 + ph * 1.3) * sw * 0.6;`,
    );
  };
  mat.customProgramCacheKey = () => `vento-${amp}`;
  return mat;
}

/** Uma InstancedMesh por malha do no, com as matrizes dadas (o no no espaco de cada instancia). */
function instanced(
  scene: Scene,
  src: Object3D,
  tag: string,
  layer: string,
  mats: Matrix4[],
  cache: Map<string, Material>,
  occluders: Mesh[],
  colors?: Color[],
  wind?: { time: { value: number }; amp: number },
): void {
  if (!mats.length) return;
  src.updateMatrixWorld(true);
  const inv = new Matrix4().copy(src.matrixWorld).invert();
  src.traverse((o) => {
    if (!(o instanceof Mesh)) return;
    const local = new Matrix4().multiplyMatrices(inv, o.matrixWorld);
    let material = hazed(o.material as Material, layer, cache);
    if (wind && material.name === "Leaves") {
      const key = `${material.uuid}:vento:${wind.amp}`;
      const hit = cache.get(key);
      material = hit ?? windy(material.clone(), wind.time, wind.amp);
      if (!hit) cache.set(key, material);
    }
    const im = new InstancedMesh(o.geometry, material, mats.length);
    const m = new Matrix4();
    mats.forEach((mat, i) => im.setMatrixAt(i, m.multiplyMatrices(mat, local)));
    if (colors) colors.forEach((c, i) => im.setColorAt(i, c));
    im.instanceMatrix.needsUpdate = true;
    im.computeBoundingSphere();
    im.userData.scenery = tag;
    im.name = `cenario-${tag}-${layer}`;
    im.frustumCulled = false;
    scene.add(im);
    occluders.push(im);
  });
}

const tmpQ = new Quaternion();
const up = new Vector3(0, 1, 0);
function trs(
  x: number,
  y: number,
  z: number,
  sx: number,
  sy: number,
  sz: number,
  yaw: number,
): Matrix4 {
  tmpQ.setFromAxisAngle(up, yaw);
  return new Matrix4().compose(new Vector3(x, y, z), tmpQ, new Vector3(sx, sy, sz));
}

/** Cristas (faixas das silhuetas): prisma triangular com a crista no meio, encostas para a frente e para tras. */
function ridgeGeometry(plan: SceneryPlan, layerIds: readonly string[]): BufferGeometry {
  const pos: number[] = [];
  const uv: number[] = [];
  const idx: number[] = [];
  for (const r of plan.ridges.filter((x) => layerIds.includes(x.layer))) {
    const base = pos.length / 3;
    r.pts.forEach(([x, h], i) => {
      const d = Math.max(4, h * 1.4);
      const wob = Math.sin(x * 0.13 + i) * h * 0.15;
      // frente (no chao), crista, tras (no chao)
      pos.push(x, GROUND_Y, r.z + d, x, GROUND_Y + h, r.z + wob, x, GROUND_Y, r.z - d);
      uv.push(x / 9, 0, x / 9, h / 9, x / 9, 0);
    });
    for (let i = 0; i < r.pts.length - 1; i++) {
      const a = base + i * 3;
      const b = a + 3;
      idx.push(a, b, a + 1, b, b + 1, a + 1, a + 1, b + 1, a + 2, b + 1, b + 2, a + 2);
    }
  }
  const g = new BufferGeometry();
  g.setAttribute("position", new BufferAttribute(new Float32Array(pos), 3));
  g.setAttribute("uv", new BufferAttribute(new Float32Array(uv), 2));
  g.setIndex(idx);
  g.computeVertexNormals();
  return g;
}

/** Divisoes da grade do terreno em cada lado (orcamento de triangulos da cena, sceneBudget.test.ts). */
export const TERRAIN_SEGMENTS = 80;

/** Terreno: grade com a altura de terrainHeight (detalhe perto, o chao plano antigo cobre o resto). */
function terrain(material: MeshStandardMaterial): Mesh {
  const W = 420;
  const D = 420;
  const g = new PlaneGeometry(W, D, TERRAIN_SEGMENTS, TERRAIN_SEGMENTS);
  g.rotateX(-Math.PI / 2);
  const cx = 0;
  const cz = -20;
  const p = g.getAttribute("position");
  const uv = g.getAttribute("uv");
  // cores por vertice: musgo nas manchas umidas, terra escura nas baixadas, folhas secas nos barrancos
  const col = new Float32Array(p.count * 3);
  const moss = new Color(0.42, 0.6, 0.36);
  const soil = new Color(0.36, 0.3, 0.26);
  const litter = new Color(0.78, 0.6, 0.42);
  const c = new Color();
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i) + cx;
    const z = p.getZ(i) + cz;
    const hgt = terrainHeight(x, z);
    p.setY(i, GROUND_Y + hgt);
    uv.setXY(i, x / 2.2, z / 2.2);
    const n1 = Math.sin(x * 0.21 + Math.sin(z * 0.13) * 2.1) * 0.5 + 0.5;
    const n2 = Math.sin(z * 0.17 + Math.cos(x * 0.11) * 1.7) * 0.5 + 0.5;
    c.setRGB(0.72, 0.62, 0.52)
      .lerp(moss, 0.55 * n1 * n2)
      .lerp(soil, 0.5 * Math.max(0, 1 - hgt / 0.6) * (1 - n1))
      .lerp(litter, 0.35 * Math.min(1, hgt / 1.4) * n2);
    col.set([c.r, c.g, c.b], i * 3);
  }
  g.setAttribute("color", new BufferAttribute(col, 3));
  g.computeVertexNormals();
  const mat = material.clone();
  mat.vertexColors = true;
  mat.normalScale.set(1.6, 1.6);
  for (const t of [mat.map, mat.normalMap, mat.roughnessMap, mat.metalnessMap])
    if (t) {
      t.wrapS = RepeatWrapping;
      t.wrapT = RepeatWrapping;
    }
  mat.color.multiplyScalar(0.85);
  mat.roughness = 1;
  mat.metalness = 0;
  const mesh = new Mesh(g, mat);
  mesh.name = "terreno";
  mesh.position.set(cx, 0, cz);
  mesh.receiveShadow = true;
  return mesh;
}

export function buildScenery(
  scene: Scene,
  plan: SceneryPlan,
  nature: Object3D,
  buildings: Object3D,
): Scenery {
  const occluders: Mesh[] = [];
  const cache = new Map<string, Material>();

  scene.add(terrain(firstMaterial(nature, NATURE_NODES.ground)));

  // cristas: montanhas de rocha, a base da cidade e as linhas de copas
  const cliff = firstMaterial(nature, NATURE_NODES.cliff);
  const ridges = new Object3D();
  ridges.name = "cristas";
  // (as faixas de copas das silhuetas viraram arvores de verdade; so as cristas de rocha ficam)
  for (const [ids, mat] of [[["montanhas", "cidade"], cliff]] as const) {
    for (const id of ids) {
      const g = ridgeGeometry(plan, [id]);
      if (!g.getIndex()?.count) continue;
      const m = new Mesh(g, hazed(mat, id, cache));
      m.name = `crista-${id}`;
      ridges.add(m);
      occluders.push(m);
    }
  }
  scene.add(ridges);

  // vegetacao e torres, uma InstancedMesh por camada e por variante (cada camada tem a sua nevoa); cada arvore
  // ganha giro, inclinacao e um tom proprio (nada de copias iguais)
  const time = { value: 0 };
  const pines = NATURE_NODES.pines.map((n) => node(nature, n));
  const broad = NATURE_NODES.broadleaves.map((n) => node(nature, n));
  const crown = node(nature, NATURE_NODES.leafCrown);
  const spire = node(buildings, BUILDINGS_NODES.spire);
  const tone = (x: number, z: number) => {
    const k = variantFor(x * 1.7, z * 0.9, 1000) / 1000;
    // verdes profundos e sombreados (pedido do usuario, 2026-11-15): a folhagem fica escura na noite
    return new Color().setHSL(0.29 + (k - 0.5) * 0.06, 0.38 + k * 0.15, 0.17 + (k - 0.5) * 0.08);
  };
  const lean = (x: number, z: number) => (variantFor(z, x, 1000) / 1000 - 0.5) * 0.08;
  /** Matriz com inclinacao em torno do pe da arvore: T * Rz(tilt) * T^-1 * (T * Ry * S). */
  function trsLean(
    x: number,
    y: number,
    z: number,
    sx: number,
    sy: number,
    sz: number,
    yaw: number,
    tilt: number,
  ): Matrix4 {
    return new Matrix4()
      .makeTranslation(x, y, z)
      .multiply(new Matrix4().makeRotationZ(tilt))
      .multiply(new Matrix4().makeTranslation(-x, -y, -z))
      .multiply(trs(x, y, z, sx, sy, sz, yaw));
  }

  // cerca viva densa dos dois lados do caminho de terra
  instanced(
    scene,
    node(nature, NATURE_NODES.bush),
    "hedge",
    "corredor",
    plan.hedge.map((b) => trs(b.x, GROUND_Y + b.y, b.z, b.s * 1.15, b.s * 1.05, b.s * 1.15, b.yaw)),
    cache,
    occluders,
    plan.hedge.map((b) => tone(b.x, b.z).multiplyScalar(0.85)),
    { time, amp: 0.04 },
  );
  // arbustos nos barrancos e nas bordas da clareira (uma camada so: perto do caminho)
  instanced(
    scene,
    node(nature, NATURE_NODES.bush),
    "bush",
    "corredor",
    plan.bushes.map((b) => trs(b.x, GROUND_Y + b.y, b.z, b.s, b.s * 0.8, b.s, b.yaw)),
    cache,
    occluders,
    plan.bushes.map((b) => tone(b.x, b.z)),
    { time, amp: 0.05 },
  );
  for (const l of SCENERY_LAYERS) {
    const id = l.id;
    const yaw = (x: number) => x * 2.3;
    pines.forEach((src, v) => {
      const ps = plan.pines.filter((p) => p.layer === id && variantFor(p.x, p.z, 3) === v);
      instanced(
        scene,
        src,
        "pine",
        id,
        ps.map((p) => trsLean(p.x, GROUND_Y + p.y, p.z, p.w, p.h, p.w, yaw(p.x), lean(p.x, p.z))),
        cache,
        occluders,
        ps.map((p) => tone(p.x, p.z)),
        { time, amp: 0.012 },
      );
    });
    broad.forEach((src, v) => {
      const gs = plan.giants.filter((g) => g.layer === id && variantFor(g.x, g.z, 3) === v);
      instanced(
        scene,
        src,
        "giant",
        id,
        gs.map((g) => {
          const s = g.h / 30;
          return trsLean(g.x, GROUND_Y + g.y, g.z, s, s, s, yaw(g.x), lean(g.x, g.z));
        }),
        cache,
        occluders,
        gs.map((g) => tone(g.x, g.z)),
        { time, amp: 0.006 },
      );
    });
    // as copas so nos troncos do portal de troncos (trunk.glb); as gigantes ja tem copa no modelo
    const cs = plan.crowns.filter((c) => c.layer === id && id === "portal-de-troncos");
    instanced(
      scene,
      crown,
      "crown",
      id,
      cs.map((c) => trs(c.x, GROUND_Y + c.y, c.z, c.r * 1.25, c.r * 1.0, c.r * 1.25, yaw(c.x))),
      cache,
      occluders,
      cs.map((c) => tone(c.x, c.z)),
      { time, amp: 0.05 },
    );
    const towers = plan.towers.filter((t) => t.layer === id);
    for (const hb of BUILDINGS_NODES.bodies) {
      const pick = towers.filter(
        (t) =>
          BUILDINGS_NODES.bodies.reduce((a, b) =>
            Math.abs(b - t.h) < Math.abs(a - t.h) ? b : a,
          ) === hb,
      );
      instanced(
        scene,
        node(buildings, `TowerBody${hb}`),
        `TowerBody${hb}`,
        id,
        pick.map((t) => trs(t.x, GROUND_Y + t.y, t.z, t.w / 4.5, t.h / hb, t.w / 4.5, yaw(t.x))),
        cache,
        occluders,
      );
    }
    instanced(
      scene,
      spire,
      "spire",
      id,
      towers.map((t) => trs(t.x, GROUND_Y + t.y + t.h, t.z, t.w, t.w, t.w, yaw(t.x))),
      cache,
      occluders,
    );
  }

  // muro de ruinas atras da clareira e as colunas partidas junto do caminho
  const wall = node(buildings, BUILDINGS_NODES.ruinWall).clone();
  wall.position.set(0, GROUND_Y, RUIN_WALL_Z);
  const path = node(buildings, BUILDINGS_NODES.ruinPath).clone();
  path.position.set(0, GROUND_Y, 60);
  for (const [obj, layer] of [
    [wall, "muro-de-ruinas"],
    [path, "ruinas-do-caminho"],
  ] as const) {
    obj.traverse((o) => {
      if (!(o instanceof Mesh)) return;
      o.material = hazed(o.material as Material, layer, cache);
      o.receiveShadow = true;
      occluders.push(o);
    });
    scene.add(obj);
  }
  return {
    occluders,
    setTime(t: number) {
      time.value = t;
    },
  };
}
