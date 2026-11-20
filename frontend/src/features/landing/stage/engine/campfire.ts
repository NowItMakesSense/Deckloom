// O fogo da clareira na cena (REQ-L10): velas em volta do circulo de runas, tochas em suportes de ferro (clareira,
// portal e mural) e chamas distantes no muro de ruinas, com halos, luzes pontuais que tremulam e brasas. Onde ficam e
// como se mexem vem de fire.ts (puro); aqui so a geometria e a aplicacao por quadro.
import {
  AdditiveBlending,
  BufferAttribute,
  BufferGeometry,
  Color,
  Mesh,
  MeshBasicMaterial,
  MeshStandardMaterial,
  PlaneGeometry,
  PointLight,
  Points,
  PointsMaterial,
  Vector3,
  type Camera,
  type Object3D,
  type Scene,
  type ShaderMaterial,
  type Texture,
} from "three";

import { type BoardLayout, boardToWorld } from "../board";
import {
  CANDLE_WAX_TOP,
  candleScale,
  type Flame,
  type FireLight,
  PORTAL_TORCHES,
  RUIN_TORCHES,
  STAND_TORCHES,
  emberPoint,
  fireShadows,
  flameFrame,
  lightFrame,
  placeCandles,
} from "../fire";
import { type Vec3, rng } from "../math";
import { FIRE } from "../palette";
import type { Token } from "../tokens";
import { FLAME_PLANE_WIDTH, flameMaterial } from "./flame";

export interface Campfire {
  update(raw: number, t: number, burst: number, camera: Camera): void;
}

interface LiveFlame {
  readonly def: Flame;
  readonly mesh: Mesh;
  readonly mat: ShaderMaterial;
  readonly halo: Mesh | null;
  readonly o0: number;
}

interface LiveLight {
  readonly def: FireLight;
  readonly light: PointLight;
}

export interface CampfireOptions {
  readonly mobile: boolean;
  /** Tokens ja simulados: as velas ficam longe da trajetoria deles. */
  readonly tokens: readonly Token[];
  readonly dotTex: Texture;
  readonly board: BoardLayout;
  /** Castical do Poly Haven (candlestick.glb): cada vela e uma copia, na escala da altura sorteada. */
  readonly candlestick: Object3D;
  /** Tochas proprias (torches.glb): raiz com os nos StandTorch e WallTorch. */
  readonly torches: Object3D;
}

export function buildCampfire(scene: Scene, o: CampfireOptions): Campfire {
  const flames: LiveFlame[] = [];
  const lights: LiveLight[] = [];
  const emberSources: Vec3[] = [];

  function addFlame(
    at: Vec3,
    w: number,
    h: number,
    seed: number,
    candle: boolean,
    haloSize: number,
    ember: boolean,
  ): void {
    // o plano e mais largo que a lingua: espaco para as labaredas do shader
    const geo = new PlaneGeometry(
      w * (candle ? FLAME_PLANE_WIDTH.candle : FLAME_PLANE_WIDTH.torch),
      h,
      1,
      8,
    );
    geo.translate(0, h / 2, 0);
    const mat = flameMaterial(seed, candle, w);
    const mesh = new Mesh(geo, mat);
    mesh.position.set(at[0], at[1], at[2]);
    mesh.renderOrder = 3;
    scene.add(mesh);
    let halo: Mesh | null = null;
    if (haloSize) {
      halo = new Mesh(
        new PlaneGeometry(haloSize, haloSize),
        new MeshBasicMaterial({
          map: o.dotTex,
          color: candle ? FIRE.haloCandle : FIRE.haloTorch,
          transparent: true,
          opacity: candle ? 0.22 : 0.34,
          blending: AdditiveBlending,
          depthWrite: false,
          fog: false,
        }),
      );
      halo.position.set(at[0], at[1] + h * 0.35, at[2]);
      halo.renderOrder = 2;
      scene.add(halo);
    }
    flames.push({
      def: { x: at[0], z: at[2], w, h, seed, candle },
      mesh,
      mat,
      halo,
      o0: halo ? (halo.material as MeshBasicMaterial).opacity : 0,
    });
    if (ember) emberSources.push([at[0], at[1] + h * 0.5, at[2]]);
  }

  function addLight(
    p: Vec3,
    spec: { readonly i0: number; readonly dist: number },
    color: string,
    seed: number,
    kind: "candle" | "torch",
    shadow: boolean,
  ): void {
    const light = new PointLight(color, 0, spec.dist, 2);
    light.position.set(p[0], p[1], p[2]);
    light.userData.fire = true;
    if (shadow) {
      light.castShadow = true;
      light.shadow.mapSize.set(512, 512);
      light.shadow.bias = -0.004;
      light.shadow.camera.near = 0.2;
      light.shadow.camera.far = spec.dist;
    }
    scene.add(light);
    lights.push({ def: { p, i0: spec.i0, seed, kind }, light });
  }

  // velas: castical de madeira do Poly Haven (candlestick.glb) em grupos irregulares logo depois do anel de runas;
  // a altura sorteada da vela vira a escala do castical (topo da cera em 0,21 m na escala 1)
  const candles = placeCandles(o.tokens);
  candles.forEach((cd, ci) => {
    const stick = o.candlestick.clone();
    const k = candleScale(cd);
    stick.position.set(cd.x, 0, cd.z);
    stick.scale.setScalar(k);
    stick.rotation.y = ci * 2.4;
    stick.traverse((m) => {
      if (m instanceof Mesh) m.castShadow = true;
    });
    scene.add(stick);
    const fh = 0.11 + cd.r * 1.3;
    addFlame(
      [cd.x, CANDLE_WAX_TOP * k + 0.025, cd.z],
      fh * 0.42,
      fh,
      ci * 1.7 + 0.3,
      true,
      0.5,
      false,
    );
  });
  for (const sd of [-1, 1]) {
    const side = candles.filter((c) => c.x * sd > 0);
    if (!side.length) continue;
    const mx = side.reduce((s, c) => s + c.x, 0) / side.length;
    const mz = side.reduce((s, c) => s + c.z, 0) / side.length;
    addLight([mx, 0.6, mz], FIRE.candle, FIRE.lightCandle, sd * 3.1, "candle", false);
  }

  // tochas do torches.glb: o pano da cabeca brilha em brasa (o material e o mesmo em todas as copias)
  const torchNode = (name: string): Object3D => {
    const node = o.torches.getObjectByName(name);
    if (!node) throw new Error(`modelo sem a tocha ${name}`);
    return node;
  };
  o.torches.traverse((m) => {
    if (
      m instanceof Mesh &&
      m.material instanceof MeshStandardMaterial &&
      m.material.name === "TorchWrap"
    ) {
      // as rachaduras do mapa emissivo do modelo acendem na cor da brasa
      m.material.emissive = new Color(FIRE.wrapGlow);
      m.material.emissiveIntensity = 1.6;
    }
  });
  const placeTorch = (name: string, at: Vec3, yaw: number, shadow: boolean): Object3D => {
    const t = torchNode(name).clone();
    t.position.set(at[0], at[1], at[2]);
    t.rotation.y = yaw;
    t.traverse((m) => {
      if (!(m instanceof Mesh)) return;
      m.castShadow = shadow;
      m.receiveShadow = shadow;
    });
    scene.add(t);
    return t;
  };

  // tochas de tripe (haste torcida e cesto de ferro), afastadas da laje; chama no cesto, em H + 0,46
  for (const st of STAND_TORCHES) {
    const H = 2.35;
    placeTorch("StandTorch", [st.x, -0.4, st.z], st.seed, true);
    const fy = -0.4 + H + 0.46;
    addFlame([st.x, fy, st.z], 0.55, 1.1, st.seed * 7.1, false, 2.6, true);
    addLight(
      [st.x, fy + 0.45, st.z],
      FIRE.torch,
      FIRE.lightTorch,
      st.seed * 5.3,
      "torch",
      fireShadows(st.shadow, o.mobile),
    );
  }

  // tochas de parede: placa, braco em voluta, cabo inclinado e cabeca envolta em pano; chama acima da cabeca
  function wallTorch(at: Vec3, yaw: number, seed: number): Vec3 {
    const g = placeTorch("WallTorch", at, yaw, true);
    g.updateMatrixWorld(true);
    const top: Vec3 = [0, 0.1 + Math.cos(0.32) * 0.27, 0.32 + Math.sin(0.32) * 0.27];
    const wp = g.localToWorld(new Vector3(top[0], top[1] + 0.07, top[2] + 0.02));
    addFlame([wp.x, wp.y, wp.z], 0.4, 0.85, seed * 7.7, false, 2.2, true);
    return [wp.x, wp.y, wp.z];
  }
  const portal = PORTAL_TORCHES.map((pt) => wallTorch(pt.at, 0, pt.seed));
  addLight([0, portal[0][1] + 0.3, 43.2], FIRE.portal, FIRE.lightTorch, 8.8, "torch", false);
  // tochas nos postes do mural de avisos e a luz quente sobre os avisos
  for (const bt of o.board.torches) wallTorch(boardToWorld(o.board, bt.local), bt.yaw, bt.seed);
  addLight(
    boardToWorld(o.board, [0, o.board.top + 0.2, 1.1]),
    FIRE.board,
    FIRE.lightBoard,
    12.4,
    "torch",
    false,
  );
  // tochas distantes no muro de ruinas: so chama e brilho na pedra
  for (const rt of RUIN_TORCHES) addFlame([rt.x, rt.y, rt.z], 0.5, 1.2, rt.seed, false, 5.5, false);

  // brasas das tochas
  const NE = o.mobile ? 70 : 130;
  const emberGeo = new BufferGeometry();
  emberGeo.setAttribute("position", new BufferAttribute(new Float32Array(NE * 3), 3));
  emberGeo.setAttribute("color", new BufferAttribute(new Float32Array(NE * 3), 3));
  const embers = new Points(
    emberGeo,
    new PointsMaterial({
      size: 0.07,
      map: o.dotTex,
      vertexColors: true,
      transparent: true,
      depthWrite: false,
      blending: AdditiveBlending,
    }),
  );
  embers.frustumCulled = false;
  scene.add(embers);
  const er = rng(150);
  const emberSeeds = Array.from({ length: NE }, () => [er(), er(), er()] as const);
  const ePos = emberGeo.attributes.position.array as Float32Array;
  const eCol = emberGeo.attributes.color.array as Float32Array;

  return {
    update(raw, t, burst, camera) {
      const cam: [number, number] = [camera.position.x, camera.position.z];
      for (const f of flames) {
        const fr = flameFrame(f.def, cam, raw, t, burst);
        f.mesh.rotation.y = fr.yaw;
        f.mesh.scale.set(1, fr.scaleY, 1);
        f.mat.uniforms.t.value = t + f.def.seed;
        f.mat.uniforms.lean.value = fr.lean;
        f.mat.uniforms.hot.value = fr.hot;
        if (f.halo) {
          f.halo.quaternion.copy(camera.quaternion);
          (f.halo.material as MeshBasicMaterial).opacity = f.o0 * fr.halo;
        }
      }
      for (const L of lights) {
        const lf = lightFrame(L.def, raw, t);
        L.light.intensity = lf.intensity;
        L.light.position.set(lf.pos[0], lf.pos[1], lf.pos[2]);
      }
      for (let i = 0; i < NE; i++) {
        const e = emberPoint(emberSeeds[i], emberSources[i % emberSources.length], t);
        ePos.set(e.pos, i * 3);
        eCol[i * 3] = e.fade;
        eCol[i * 3 + 1] = 0.5 * e.fade;
        eCol[i * 3 + 2] = 0.16 * e.fade;
      }
      emberGeo.attributes.position.needsUpdate = true;
      emberGeo.attributes.color.needsUpdate = true;
    },
  };
}
