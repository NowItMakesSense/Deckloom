// T-L02 / AC-L02m, AC-L02h (REQ-L10): a cena a noite com velas, tochas e o mural de avisos, montada com um canvas
// falso (sem WebGL no jsdom). As fases sao testadas como funcoes puras; aqui so o que a cena liga a elas.
import {
  Color,
  Mesh,
  MeshBasicMaterial,
  MeshPhysicalMaterial,
  MeshStandardMaterial,
  NormalBlending,
  PlaneGeometry,
  PointLight,
  Raycaster,
  ShaderMaterial,
  Vector3,
  type DirectionalLight,
  type Object3D,
} from "three";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { boardLayout, boardView } from "../board";
import { CANDLE_MAX } from "../fire";
import { MIST_MAX_OPACITY } from "../mist";
import { MIST, SKY, WORLD } from "../palette";
import { BOXP, END, PH, boxPhases, burstAt } from "../phases";
import { fakeModels } from "./testModels";
import { ARCH_Z, GROUND_Y, TRUNKS_Z, trunkPlacements } from "../silhouettes";
import { PROPS } from "../props";
import { MOON_DIR } from "../sky";
import { sceneryPlan, terrainHeight } from "../scenery";
import { FogExp2, InstancedMesh, Quaternion, ShapeGeometry, Texture, Vector2 } from "three";
import { cardPose } from "../deck";
import { PORTAL_TORCHES, STAND_TORCHES } from "../fire";
import { buildWorld, uprightLogo } from "./world";

function fakeContext(): CanvasRenderingContext2D {
  const proxy: unknown = new Proxy(function noop() {}, {
    get: (_target, key) =>
      typeof key === "symbol" ? undefined : key === "measureText" ? () => ({ width: 10 }) : proxy,
    set: () => true,
    apply: () => proxy,
  });
  return proxy as CanvasRenderingContext2D;
}

const logo = { naturalWidth: 200, naturalHeight: 80 } as unknown as HTMLImageElement;
const LOGOS = { magic: logo, pokemon: logo, yugioh: logo, onepiece: logo, lorcana: logo };
const HOOKS = [0, 1, 2, 3].map((i) => ({ title: `Gancho ${i}`, type: "Recurso", text: "Texto" }));

function world(mobile: boolean) {
  const w = buildWorld({ mobile, hooks: HOOKS, logos: LOGOS, models: fakeModels() });
  w.camera.aspect = mobile ? 375 / 812 : 1366 / 768;
  w.camera.fov = mobile ? 62 : 42;
  w.camera.updateProjectionMatrix();
  return w;
}

function all<T extends Object3D>(root: Object3D, pick: (o: Object3D) => o is T): T[] {
  const out: T[] = [];
  root.traverse((o) => {
    if (pick(o)) out.push(o);
  });
  return out;
}

const isFlame = (o: Object3D): o is Mesh =>
  o instanceof Mesh && o.material instanceof ShaderMaterial && "candle" in o.material.uniforms;
const isPoint = (o: Object3D): o is PointLight => o instanceof PointLight;

beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(
    () => fakeContext() as never,
  );
  vi.stubGlobal("Path2D", class {});
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("o fogo da clareira", () => {
  it("velas (no maximo oito) e tochas acesas com chama procedural aditiva", () => {
    const w = world(false);
    const flames = all(w.scene, isFlame);
    const candles = flames.filter(
      (m) => (m.material as ShaderMaterial).uniforms.candle.value === 1,
    );
    const torches = flames.filter(
      (m) => (m.material as ShaderMaterial).uniforms.candle.value === 0,
    );

    expect(candles.length).toBeGreaterThanOrEqual(4);
    expect(candles.length).toBeLessThanOrEqual(CANDLE_MAX);
    // 4 na clareira, 2 no portal, 2 no mural e 4 distantes no muro de ruinas
    expect(torches).toHaveLength(12);
    for (const f of flames) expect((f.material as ShaderMaterial).depthWrite).toBe(false);
  });

  it("a chama anima: o relogio do shader, a altura e o giro para a camera mudam a cada quadro", () => {
    const w = world(false);
    const flame = all(w.scene, isFlame)[0];
    const mat = flame.material as ShaderMaterial;

    w.update(0.5, 1);
    const first = [mat.uniforms.t.value, flame.scale.y, mat.uniforms.lean.value];
    w.update(0.5, 1.7);
    const second = [mat.uniforms.t.value, flame.scale.y, mat.uniforms.lean.value];

    first.forEach((v, i) => expect(v).not.toBeCloseTo(second[i], 4));
    expect(flame.rotation.x).toBe(0);
    expect(flame.rotation.z).toBe(0);
  });

  it("luzes pontuais do fogo tremulam; sombra so nas duas tochas de tras e so fora do celular", () => {
    const desk = world(false);
    const lights = all(desk.scene, isPoint).filter((l) => l.userData.fire);
    desk.update(0.5, 1);
    const a = lights.map((l) => l.intensity);
    desk.update(0.5, 1.3);
    const b = lights.map((l) => l.intensity);

    expect(lights.length).toBeGreaterThanOrEqual(7);
    expect(a.some((v, i) => Math.abs(v - b[i]) > 1e-3)).toBe(true);
    expect(lights.filter((l) => l.castShadow)).toHaveLength(2);
    expect(all(world(true).scene, isPoint).filter((l) => l.castShadow)).toHaveLength(0);
  });
});

describe("a noite", () => {
  it("nevoa densa exponencial esconde o fundo: o cenario 3D entra nela; as janelas acesas continuam acesas", () => {
    const w = world(false);
    expect(w.scene.fog).toBeInstanceOf(FogExp2);
    // densa (pedido do usuario, 2026-11-15): a 40 m mais da metade ja e nevoa
    expect((w.scene.fog as FogExp2).density).toBeGreaterThanOrEqual(0.02);
    const far = w.scene.getObjectByName("crista-montanhas") as Mesh;
    expect((far.material as MeshStandardMaterial).fog).toBe(true);
    const windows: MeshStandardMaterial[] = [];
    w.scene.traverse((o) => {
      if (
        o instanceof Mesh &&
        o.material instanceof MeshStandardMaterial &&
        o.material.name === "WindowGlow"
      )
        windows.push(o.material);
    });
    expect(windows.length).toBeGreaterThan(0);
  });

  it("doze faixas de bruma noturna: cor da paleta, mistura normal, opacidade moderada, afinando na explosao", () => {
    const w = world(false);
    const bands = all(w.scene, (o): o is Mesh => o instanceof Mesh).filter(
      (m) =>
        m.geometry instanceof PlaneGeometry &&
        m.name !== "terreno" &&
        m.geometry.parameters.width >= 90 &&
        m.geometry.parameters.width < 1000,
    );
    const mats = bands.map((m) => m.material as MeshBasicMaterial);

    expect(w.scene.fog?.color.getHex()).toBe(new Color(SKY.fog).getHex());
    expect(bands).toHaveLength(12);
    for (const m of mats) {
      expect(m.color.getHex()).toBe(new Color(MIST.color).getHex());
      expect(m.blending).toBe(NormalBlending);
    }
    w.update(0.5, 0);
    for (const o of mats.map((m) => m.opacity)) expect(o).toBeLessThanOrEqual(MIST_MAX_OPACITY);
    // pico da explosao: progresso da mesa ~0,1; a nevoa afina pela metade
    const blast = burstAt(0.1);
    expect(blast).toBeGreaterThan(0.8);
    w.update(PH.regroup + (PH.mesa - PH.regroup) * 0.1, 0);
    for (const m of mats)
      expect(m.opacity).toBeLessThanOrEqual(MIST_MAX_OPACITY * (1 - 0.5 * blast) + 1e-9);
    expect(mats.some((m) => m.opacity > 0)).toBe(true);
    expect(bands.every((m) => Math.abs(m.position.z - 46) >= 6)).toBe(true);
  });

  it("as faixas derivam devagar com o tempo e a depuracao pode desligar so a nevoa (para medir com e sem)", () => {
    const w = world(false);
    const bands = all(w.scene, (o): o is Mesh => o instanceof Mesh).filter(
      (m) =>
        m.geometry instanceof PlaneGeometry &&
        m.name !== "terreno" &&
        m.geometry.parameters.width >= 90 &&
        m.geometry.parameters.width < 1000,
    );
    w.update(0.5, 0);
    const x0 = bands.map((m) => m.position.x);
    w.update(0.5, 10);
    bands.forEach((m, i) => {
      expect(m.position.x).not.toBe(x0[i]);
      expect(Math.abs(m.position.x - x0[i])).toBeLessThan(3);
    });

    // a faixa mais perto da camera some (camera dentro da bruma)
    w.update(0.5, 0);
    const near = bands.reduce((a, b) =>
      Math.abs(a.position.z - w.camera.position.z) < Math.abs(b.position.z - w.camera.position.z)
        ? a
        : b,
    );
    if (Math.abs(near.position.z - w.camera.position.z) <= 4) {
      expect((near.material as MeshBasicMaterial).opacity).toBe(0);
    }

    w.setMist(false);
    expect(bands.every((m) => !m.visible)).toBe(true);
    w.setMist(true);
    expect(bands.every((m) => m.visible)).toBe(true);
  });

  it("a luz frontal sobe so enquanto as cartas dos ganchos viram", () => {
    const w = world(false);
    const front = w.scene.children.find(
      (o) => (o as DirectionalLight).isDirectionalLight && o.userData.front,
    ) as DirectionalLight;

    w.update(0.1, 0);
    const forest = front.intensity;
    w.update((PH.story0 + PH.story1) / 2, 0);
    const hooks = front.intensity;

    expect(hooks).toBeGreaterThan(forest);
  });
});

describe("o mural de avisos dos planos", () => {
  it.each([false, true])(
    "existe com os dois avisos e a camera termina diante dele (celular: %s)",
    (mobile) => {
      const w = world(mobile);
      expect(w.scene.getObjectByName("aviso-free")).toBeDefined();
      expect(w.scene.getObjectByName("aviso-paid")).toBeDefined();

      w.update(END, 0);
      const view = boardView(boardLayout(mobile), w.camera.fov, w.camera.aspect, mobile);
      expect(w.camera.position.x).toBeCloseTo(view.pos[0], 4);
      expect(w.camera.position.z).toBeCloseTo(view.pos[2], 4);
    },
  );
});

describe("deck box em glTF (T-L05, AC-L05a)", () => {
  it("a cena usa o modelo carregado, uma vez, na posicao da caixa", () => {
    const w = world(false);
    const box = w.scene.getObjectByName("DeckBox");
    expect(box).toBeDefined();
    w.scene.updateMatrixWorld(true);
    const wp = box!.getWorldPosition(new Vector3());
    expect(wp.x).toBeCloseTo(BOXP[0]);
    expect(wp.y).toBeCloseTo(BOXP[1]);
    expect(wp.z).toBeCloseTo(BOXP[2]);
    expect(w.scene.getObjectsByProperty("name", "DeckBox")).toHaveLength(1);
  });

  it("o scroll abre a tampa, solta fecho e travas pelos nos do modelo", () => {
    const w = world(false);
    let raw = 0;
    while (raw < END && boxPhases(raw, 0).lid === 0) raw += 0.002;
    w.update(raw, 0);
    const ph = boxPhases(raw, 0);
    expect(w.scene.getObjectByName("Lid")!.rotation.x).toBe(ph.lid);
    expect(w.scene.getObjectByName("Clasp")!.rotation.x).toBe(-ph.clasp);
    expect(w.scene.getObjectByName("LatchL")!.rotation.z).toBe(ph.latch);
    expect(w.scene.getObjectByName("LatchR")!.rotation.z).toBe(-ph.latch);
  });
});

describe("cor das runas (decisao do usuario, 2026-11-14)", () => {
  it("todas as runas da cena brilham no violeta da marca, nao mais no verde-amarelado", () => {
    expect(WORLD.runeGlow.toLowerCase()).toBe("#9a77ff");
  });
});

describe("deck box direto no chao do altar (decisao do usuario, 2026-11-14)", () => {
  it("sem pedestal nem raizes: o primeiro objeto abaixo da caixa e o tampo do altar, em y = 0", () => {
    const w = world(false);
    w.scene.updateMatrixWorld(true);
    expect(BOXP[1]).toBe(0);
    const box = w.scene.getObjectByName("DeckBox")!;
    const targets: Object3D[] = [];
    w.scene.traverse((o) => {
      let inBox = false;
      for (let p: Object3D | null = o; p; p = p.parent) if (p === box) inBox = true;
      if (o instanceof Mesh && !inBox && o.visible) targets.push(o);
    });
    const ray = new Raycaster(new Vector3(BOXP[0] + 0.5, 5, BOXP[2] + 0.3), new Vector3(0, -1, 0));
    const hit = ray.intersectObjects(targets, false).find((h) => h.distance > 0);
    expect(hit).toBeDefined();
    expect(hit!.point.y).toBeCloseTo(0, 3);
  });
});

describe("altar em glTF (T-L05, AC-L05b)", () => {
  it("a cena usa o modelo do altar e acende o circulo com a fase altarGlow", () => {
    const w = world(false);
    const altar = w.scene.getObjectByName("Altar");
    expect(altar).toBeDefined();
    const glow: MeshStandardMaterial[] = [];
    altar!.traverse((o) => {
      if (o instanceof Mesh && o.material instanceof MeshStandardMaterial && o.material.emissiveMap)
        glow.push(o.material);
    });
    let raw = 0;
    while (raw < END && boxPhases(raw, 0).altarGlow === 0) raw += 0.002;
    w.update(raw, 0);
    expect(glow[0].emissiveIntensity).toBeCloseTo(boxPhases(raw, 0).altarGlow);
  });
});

describe("portal de pedra em glTF (T-L05, AC-L05b)", () => {
  it("a cena usa o modelo do portal em ARCH_Z, sem a silhueta chapada nem o disco da chave", () => {
    const w = world(false);
    const portal = w.scene.getObjectByName("Portal");
    expect(portal).toBeDefined();
    expect(portal!.position.z).toBe(ARCH_Z);
    expect(w.scene.getObjectByName("portal-de-pedra")).toBeUndefined();
    expect(w.scene.getObjectByName("keystone")).toBeUndefined();
    // o portal tapa o que esta atras dele (a auditoria de continuidade nao conta o que ele esconde)
    const meshes: Mesh[] = [];
    portal!.traverse((o) => {
      if (o instanceof Mesh) meshes.push(o);
    });
    expect(meshes.length).toBeGreaterThan(0);
    for (const m of meshes) expect(w.occluders).toContain(m);
  });
});

describe("troncos do primeiro plano em glTF (T-L05, AC-L05b)", () => {
  it("cada tronco do portal de troncos e uma copia do modelo, na posicao e escala da silhueta", () => {
    const w = world(false);
    const trunks = w.scene.children.filter((o) => o.name === "Trunk");
    const plan = trunkPlacements();
    expect(trunks).toHaveLength(plan.length);
    trunks.forEach((t, i) => {
      expect(t.position.x).toBeCloseTo(plan[i].x);
      // apoiado no terreno (colinas longe do caminho)
      expect(t.position.y).toBeCloseTo(GROUND_Y + terrainHeight(plan[i].x, TRUNKS_Z));
      expect(t.position.z).toBeCloseTo(TRUNKS_Z);
      expect(t.scale.y).toBeCloseTo(plan[i].h / 30);
      expect(t.scale.x).toBeCloseTo(plan[i].tw / 1.6);
    });
    // os troncos tapam o que fica atras deles
    let meshes = 0;
    for (const t of trunks)
      t.traverse((o) => {
        if (o instanceof Mesh) {
          meshes++;
          expect(w.occluders).toContain(o);
        }
      });
    expect(meshes).toBeGreaterThanOrEqual(trunks.length);
  });
});

describe("velas em castical e pedras e tocos do Poly Haven (T-L05, REQ-L14)", () => {
  it("cada vela e um castical do modelo, com a chama logo acima da cera", () => {
    const w = world(false);
    const sticks = w.scene.children.filter((o) => o.name === "Candlestick");
    const candleFlames = all(w.scene, isFlame).filter(
      (m) => (m.material as ShaderMaterial).uniforms.candle.value === 1,
    );
    expect(sticks.length).toBe(candleFlames.length);
    expect(sticks.length).toBeGreaterThanOrEqual(4);
    for (const s of sticks) {
      expect(s.position.y).toBe(0); // sobre o tampo do altar
      const flame = candleFlames.find(
        (f) => Math.hypot(f.position.x - s.position.x, f.position.z - s.position.z) < 0.01,
      );
      expect(flame, `chama do castical em ${s.position.x}`).toBeDefined();
      const waxTop = 0.21 * s.scale.y;
      expect(flame!.position.y).toBeGreaterThan(waxTop);
      expect(flame!.position.y).toBeLessThan(waxTop + 0.25);
    }
  });

  it("as pedras e os tocos ficam nas posicoes de PROPS, no chao, e tapam o que esta atras", () => {
    const w = world(false);
    const placed = w.scene.children.filter((o) => o.userData.prop === true);
    expect(placed).toHaveLength(PROPS.length);
    placed.forEach((o, i) => {
      expect(o.position.x).toBeCloseTo(PROPS[i].x);
      expect(o.position.z).toBeCloseTo(PROPS[i].z);
      // enterradas um pouco no chao (pedra 22 cm, toco 5 cm, na escala), para nao parecer que flutuam
      const sink = (PROPS[i].kind === "rock" ? 0.22 : 0.05) * PROPS[i].scale;
      expect(o.position.y).toBeCloseTo(GROUND_Y - sink);
      expect(o.scale.x).toBeCloseTo(PROPS[i].scale);
      o.traverse((m) => {
        if (m instanceof Mesh) expect(w.occluders).toContain(m);
      });
    });
    const rocks = placed.filter((_o, i) => PROPS[i].kind === "rock");
    rocks.forEach((o, i) => {
      const variant = PROPS.filter((p) => p.kind === "rock")[i].variant;
      expect(o.name).toBe(`Rock${variant}`);
    });
  });
});

describe("tochas em glTF (torches.glb, T-L05)", () => {
  it("as tochas de tripe sao copias do modelo, no chao, com a chama no cesto", () => {
    const w = world(false);
    const stands = w.scene.children.filter((o) => o.name === "StandTorch");
    expect(stands).toHaveLength(STAND_TORCHES.length);
    const torchFlames = all(w.scene, isFlame).filter(
      (m) => (m.material as ShaderMaterial).uniforms.candle.value === 0,
    );
    stands.forEach((s, i) => {
      expect(s.position.x).toBeCloseTo(STAND_TORCHES[i].x);
      expect(s.position.z).toBeCloseTo(STAND_TORCHES[i].z);
      expect(s.position.y).toBeCloseTo(GROUND_Y);
      const flame = torchFlames.find(
        (f) => Math.hypot(f.position.x - s.position.x, f.position.z - s.position.z) < 0.01,
      );
      expect(flame).toBeDefined();
      expect(flame!.position.y).toBeCloseTo(GROUND_Y + 2.35 + 0.46);
    });
  });

  it("as tochas de parede (portal e mural) sao copias do modelo e o pano da cabeca brilha em brasa", () => {
    const w = world(false);
    const walls = w.scene.children.filter((o) => o.name === "WallTorch");
    expect(walls).toHaveLength(PORTAL_TORCHES.length + 2);
    for (const t of PORTAL_TORCHES)
      expect(
        walls.some((o) => o.position.distanceTo(new Vector3(t.at[0], t.at[1], t.at[2])) < 1e-6),
      ).toBe(true);
    let glowing = 0;
    walls[0].traverse((o) => {
      if (
        o instanceof Mesh &&
        o.material instanceof MeshStandardMaterial &&
        o.material.name === "TorchWrap"
      ) {
        expect(o.material.emissiveIntensity).toBeGreaterThan(0);
        glowing++;
      }
    });
    expect(glowing).toBeGreaterThan(0);
  });
});

describe("mural de avisos em glTF (noticeboard.glb, T-L05)", () => {
  it.each([
    [false, "NoticeBoardWide"],
    [true, "NoticeBoardTall"],
  ])(
    "celular %s: a estrutura do mural e a variante %s; placa e avisos continuam no codigo",
    (mobile, variant) => {
      const w = world(mobile);
      const mural = w.scene.getObjectByName("mural-de-avisos")!;
      expect(mural).toBeDefined();
      const frame = mural.children.find((c) => c.name === variant);
      expect(frame, variant).toBeDefined();
      expect(frame!.position.length()).toBe(0); // origem do modelo = centro do quadro, como o grupo do mural
      expect(mural.getObjectByName("aviso-free")).toBeDefined();
      expect(mural.getObjectByName("aviso-paid")).toBeDefined();
      expect(mural.getObjectByName("placa-mural")).toBeDefined();
      // a estrutura antiga em BoxGeometry saiu: o que sobra fora do modelo sao a placa, os avisos e as tachas
      const loose = mural.children.filter((c) => c !== frame);
      for (const c of loose)
        expect(["placa-mural", "aviso-free", "aviso-paid", "tacha"]).toContain(c.name);
    },
  );
});

describe("tokens e cartas em glTF (pieces.glb, T-L05)", () => {
  const faceOf = (root: Object3D, name: string): MeshStandardMaterial | undefined => {
    let found: MeshStandardMaterial | undefined;
    root.traverse((o) => {
      if (
        o instanceof Mesh &&
        o.material instanceof MeshStandardMaterial &&
        o.material.name === name
      )
        found = o.material;
    });
    return found;
  };

  it("cada token e um medalhao ou uma ficha do modelo, com o logo ou o simbolo de mana na face", () => {
    const w = world(false);
    expect(w.tokenMeshes).toHaveLength(w.tokens.length);
    const maps = new Set<unknown>();
    w.tokens.forEach((k, i) => {
      const obj = w.tokenMeshes[i];
      expect(obj.name).toBe(k.kind === "medal" ? "Medallion" : "ManaChip");
      const face = faceOf(obj, k.kind === "medal" ? "MedalFace" : "ChipFace");
      expect(face?.map, `token ${i}`).toBeTruthy();
      maps.add(face!.map);
    });
    // cada token tem a propria textura (o material da face nao e compartilhado entre copias)
    expect(maps.size).toBe(w.tokens.length);
  });

  it("as texturas de canvas nas faces do glTF nao sao viradas (UV do glTF: flipY = false)", () => {
    const w = world(false);
    for (const c of w.cards.slice(0, 5)) {
      expect(faceOf(c, "CardFace")!.map!.flipY).toBe(false);
      expect(faceOf(c, "CardBack")!.map!.flipY).toBe(false);
    }
    w.tokens.forEach((k, i) => {
      const face = faceOf(w.tokenMeshes[i], k.kind === "medal" ? "MedalFace" : "ChipFace")!;
      expect(face.map!.flipY).toBe(false);
      if (k.kind === "medal") expect(faceOf(w.tokenMeshes[i], "MedalBack")!.map!.flipY).toBe(false);
    });
  });

  it("as cartas sao o modelo de cantos arredondados: ganchos com frente propria, o resto de costas", () => {
    const w = world(false);
    expect(w.cards.every((c) => c.name === "Card")).toBe(true);
    const fronts = w.cards.map((c) => faceOf(c, "CardFace")!.map);
    const backs = w.cards.map((c) => faceOf(c, "CardBack")!.map);
    expect(new Set(fronts.slice(0, 4)).size).toBe(4);
    for (const f of fronts.slice(4)) expect(f).toBe(backs[0]); // a mesma textura do verso
    for (const b of backs) expect(b).toBe(backs[0]);
  });

  it("foil: a frente do gancho ganha iridescencia junto com o brilho dourado da revelacao", () => {
    const w = world(false);
    let raw = 0.38;
    let pose = cardPose(raw, 0, { t: 0, mobile: false, phases: boxPhases(raw, 0) });
    while (raw < 0.6 && !(pose.sheen && pose.sheen.opacity > 0.3)) {
      raw += 0.001;
      pose = cardPose(raw, 0, { t: 0, mobile: false, phases: boxPhases(raw, 0) });
    }
    expect(pose.sheen?.opacity).toBeGreaterThan(0.3);
    w.update(raw, 0);
    const face = faceOf(w.cards[0], "CardFace") as MeshPhysicalMaterial;
    expect(face).toBeInstanceOf(MeshPhysicalMaterial);
    expect(face.iridescence).toBeCloseTo(pose.sheen!.opacity, 3);
  });
});

describe("fundo em 3D: terreno, arvores, torres, cristas e ruinas (T-L05)", () => {
  const plan = sceneryPlan();
  const instancesOf = (w: ReturnType<typeof world>, name: string) => {
    const meshes: InstancedMesh[] = [];
    w.scene.traverse((o) => {
      if (o instanceof InstancedMesh && o.userData.scenery === name) meshes.push(o);
    });
    return meshes;
  };

  it("as silhuetas chapadas da floresta sairam (sobram so as samambaias)", () => {
    const w = world(false);
    const flats: Mesh[] = [];
    w.scene.traverse((o) => {
      if (o instanceof Mesh && o.geometry instanceof ShapeGeometry) flats.push(o);
    });
    expect(flats.every((m) => m.name === "samambaia")).toBe(true);
  });

  it("cada pinheiro, gigante, copa e torre do plano vira uma instancia", () => {
    const w = world(false);
    // uma InstancedMesh por camada e por material do no: conta pelas malhas de um material so
    const count = (name: string, material: string) => {
      const ms = instancesOf(w, name).filter(
        (m) => (m.material as MeshStandardMaterial).name === material,
      );
      expect(ms.length, name).toBeGreaterThan(0);
      return ms.reduce((n, m) => n + m.count, 0);
    };
    expect(count("pine", "Leaves")).toBe(plan.pines.length);
    expect(count("giant", "Bark")).toBe(plan.giants.length);
    // copas separadas so nos troncos do portal de troncos (as gigantes ja tem copa no modelo)
    expect(count("crown", "Leaves")).toBe(
      plan.crowns.filter((c) => c.layer === "portal-de-troncos").length,
    );
    expect(count("spire", "BldRoof")).toBe(plan.towers.length);
    expect(count("bush", "Leaves")).toBe(plan.bushes.length);
    const bodies = ["TowerBody20", "TowerBody40", "TowerBody70"].reduce(
      (n, k) =>
        n +
        instancesOf(w, k)
          .filter((m) => (m.material as MeshStandardMaterial).name === "BldStone")
          .reduce((a, m) => a + m.count, 0),
      0,
    );
    expect(bodies).toBe(plan.towers.length);
    // tudo o que e solido tapa o que esta atras (auditoria de continuidade)
    for (const m of instancesOf(w, "pine")) expect(w.occluders).toContain(m);
  });

  it("o terreno segue terrainHeight e as cristas, o muro de ruinas e as colunas do caminho estao na cena", () => {
    const w = world(false);
    const ground = w.scene.getObjectByName("terreno") as Mesh;
    expect(ground).toBeDefined();
    const pos = ground.geometry.getAttribute("position");
    for (let i = 0; i < pos.count; i += 997) {
      const x = pos.getX(i) + ground.position.x;
      const z = pos.getZ(i) + ground.position.z;
      expect(pos.getY(i) + ground.position.y).toBeCloseTo(GROUND_Y + terrainHeight(x, z), 4);
    }
    expect(w.scene.getObjectByName("cristas")).toBeDefined();
    // o chao nao e de uma cor so: musgo, terra escura nas baixadas e folhas, por vertice
    expect(ground.geometry.getAttribute("color")).toBeDefined();
    expect((ground.material as MeshStandardMaterial).vertexColors).toBe(true);
    expect(w.scene.getObjectByName("RuinWall")?.position.z).toBe(-24);
    expect(w.scene.getObjectByName("RuinPath")).toBeDefined();
  });

  it("a cena inteira (instancias contadas) fica em ate 250 mil triangulos", () => {
    const w = world(false);
    let tris = 0;
    w.scene.traverse((o) => {
      if (!(o instanceof Mesh) || !o.visible) return;
      const g = o.geometry;
      const n = g.index ? g.index.count / 3 : g.getAttribute("position").count / 3;
      tris += n * (o instanceof InstancedMesh ? o.count : 1);
    });
    expect(tris).toBeLessThanOrEqual(250_000);
  });
});

describe("ceu noturno: estrelas e lua (2026-11-15)", () => {
  const skyMat = (w: ReturnType<typeof world>) => {
    let found: ShaderMaterial | undefined;
    w.scene.traverse((o) => {
      if (o instanceof Mesh && o.name === "ceu") found = o.material as ShaderMaterial;
    });
    return found!;
  };

  it("o ceu desenha estrelas e a lua na direcao MOON_DIR, fora da nevoa", () => {
    const w = world(false);
    const sky = skyMat(w);
    expect(sky).toBeDefined();
    expect(sky.fragmentShader).toMatch(/stars\(/);
    expect(sky.fragmentShader).toMatch(/moonDisc\(/);
    const m = sky.uniforms.moon.value as Vector3;
    expect(m.x).toBeCloseTo(MOON_DIR[0], 6);
    expect(m.y).toBeCloseTo(MOON_DIR[1], 6);
    expect(sky.fog).toBe(false);
  });

  it("as estrelas cintilam: o relogio do ceu anda com o tempo", () => {
    const w = world(false);
    w.update(0.1, 1);
    const a = skyMat(w).uniforms.t.value;
    w.update(0.1, 2.5);
    expect(skyMat(w).uniforms.t.value).not.toBe(a);
  });

  it("a luz principal (com sombra) e o luar frio, vindo da lua", () => {
    const w = world(false);
    const key = all(
      w.scene,
      (o): o is DirectionalLight => (o as DirectionalLight).isDirectionalLight === true,
    ).find((l) => l.castShadow)!;
    const d = key.position.clone().sub(key.target.position).normalize();
    expect(d.dot(new Vector3(...MOON_DIR))).toBeGreaterThan(0.999);
    expect(key.color.b).toBeGreaterThan(key.color.r);
  });
});

describe("caminho de terra, cerca viva, arvores escuras e bancos de nevoa (2026-11-15)", () => {
  it("o caminho e de terra, reto e plano, da entrada ate o altar", () => {
    const w = world(false);
    const path = w.scene.getObjectByName("caminho-de-terra") as Mesh;
    expect(path).toBeDefined();
    path.updateMatrixWorld(true);
    path.geometry.computeBoundingBox();
    const bb = path.geometry.boundingBox!.clone().applyMatrix4(path.matrixWorld);
    expect(bb.min.z).toBeLessThanOrEqual(5.5);
    expect(bb.max.z).toBeGreaterThanOrEqual(69);
    expect(bb.max.y - bb.min.y).toBeLessThan(0.02);
    expect(Math.abs(bb.min.x + bb.max.x)).toBeLessThan(0.01);
  });

  it("a cerca viva vira instancias de arbusto, em tons escuros", () => {
    const w = world(false);
    let n = 0;
    w.scene.traverse((o) => {
      if (o instanceof InstancedMesh && o.userData.scenery === "hedge") n += o.count;
    });
    expect(n).toBe(sceneryPlan().hedge.length);
  });

  it("as folhagens ficam escuras (verdes profundos, luminancia baixa)", () => {
    const w = world(false);
    const lums: number[] = [];
    w.scene.traverse((o) => {
      if (
        o instanceof InstancedMesh &&
        o.instanceColor &&
        ["pine", "giant", "crown"].includes(o.userData.scenery)
      ) {
        const c = new Color();
        for (let i = 0; i < o.count; i++) {
          o.getColorAt(i, c);
          lums.push(0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b);
          expect(c.g).toBeGreaterThanOrEqual(c.r);
        }
      }
    });
    expect(lums.length).toBeGreaterThan(0);
    expect(Math.max(...lums)).toBeLessThan(0.55);
  });

  it("bancos de nevoa baixos ocupam o caminho e envolvem o altar", () => {
    const w = world(false);
    const banks = w.scene.children.filter((o) => o.name === "banco-de-nevoa");
    expect(banks.length).toBeGreaterThanOrEqual(70);
  });
});

describe("logos dos medalhoes sempre de pe (2026-11-15)", () => {
  it.each([0, 0.9, 2.4, -1.7, Math.PI])(
    "com a ficha girada %s rad, o topo do logo aponta para o fundo da cena",
    (yaw) => {
      const tex = new Texture();
      tex.center.set(0.5, 0.5);
      const q = new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), yaw);
      uprightLogo(tex, q);
      tex.updateMatrix();
      // um ponto no mundo logo "acima" do centro (para o fundo, -z) deve amostrar o topo da imagem (v = 0, u = 0,5)
      const world = new Vector3(0, 0, -0.4);
      const local = world.clone().applyQuaternion(q.clone().invert());
      const uv = new Vector2(0.5 + local.x / 1, 0.5 + local.z / 1).applyMatrix3(tex.matrix);
      expect(uv.x).toBeCloseTo(0.5, 4);
      expect(uv.y).toBeCloseTo(0.1, 4);
    },
  );
});

describe("desfoque no celular (2026-11-15)", () => {
  it("no celular a profundidade de campo e mais suave (a carta do gancho nao fica borrada atras do cartao HTML)", () => {
    const raw = 0.42;
    const desk = world(false).update(raw, 0).aperture;
    const mob = world(true).update(raw, 0).aperture;
    expect(mob).toBeLessThanOrEqual(desk * 0.5);
  });
});
