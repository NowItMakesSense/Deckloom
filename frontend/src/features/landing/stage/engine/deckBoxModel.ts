// Deck box em glTF (REQ-L14, AC-L05a): modelo proprio do Deckloom feito no Blender (design/3d/deckbox.py),
// carregado sob demanda de public/models. A landing anima tampa, fecho e travas pelos nos do modelo.
import { Mesh, MeshStandardMaterial, type Material, type Object3D } from "three";

import type { BoxPhases } from "../phases";

export const DECKBOX_URL = "/models/deckbox.glb";
export const ALTAR_URL = "/models/altar.glb";
export const ALTAR_ROOT = "Altar";
export const PORTAL_URL = "/models/portal.glb";
export const PORTAL_ROOT = "Portal";
export const TRUNK_URL = "/models/trunk.glb";
export const TRUNK_ROOT = "Trunk";
// modelos prontos do Poly Haven (CC0), reduzidos em design/3d/import_ph.py
export const CANDLESTICK_URL = "/models/candlestick.glb";
export const CANDLESTICK_ROOT = "Candlestick";
export const ROCKS_URL = "/models/rocks.glb";
export const ROCKS_ROOT = "Rocks";
export const STUMP_URL = "/models/stump.glb";
export const STUMP_ROOT = "Stump";
export const TORCHES_URL = "/models/torches.glb";
/** Tochas proprias (design/3d/torches.py): de tripe (pe no chao) e de parede (origem no ponto de fixacao). */
export const TORCHES_NODES = { root: "Torches", stand: "StandTorch", wall: "WallTorch" } as const;
export const PIECES_URL = "/models/pieces.glb";
/** Pecas de jogo (design/3d/pieces.py): faces em materiais proprios, que a landing preenche com as texturas. */
export const PIECES_NODES = {
  root: "Pieces",
  medal: "Medallion",
  chip: "ManaChip",
  card: "Card",
} as const;
export const NATURE_URL = "/models/nature.glb";
/** Vegetacao e materiais do fundo (design/3d/nature.py). */
export const NATURE_NODES = {
  root: "Nature",
  /** Tres pinheiros (altura e largura 1) e tres folhosas (altura nominal 30 m), com folhas em cartoes recortados. */
  pines: ["Pine0", "Pine1", "Pine2"],
  broadleaves: ["Broadleaf0", "Broadleaf1", "Broadleaf2"],
  /** So a copa (raio 1), para os troncos do portal de troncos. */
  leafCrown: "LeafCrown",
  /** Arbusto baixo (raio 1, base no chao). */
  bush: "Bush",
  ground: "MatGround",
  cliff: "MatCliff",
  leaves: "MatLeaves",
  /** Mapa de cor da lua (NASA CGI Moon Kit, LROC, dominio publico) para o ceu. */
  moon: "MatMoon",
} as const;
export const BUILDINGS_URL = "/models/buildings.glb";
/** Construcoes do fundo (design/3d/buildings.py): torres de tres alturas, agulha, muro e colunas de ruinas. */
export const BUILDINGS_NODES = {
  root: "Buildings",
  bodies: [20, 40, 70] as const,
  spire: "TowerSpire",
  ruinWall: "RuinWall",
  ruinPath: "RuinPath",
} as const;
export const NOTICEBOARD_URL = "/models/noticeboard.glb";
/** Estrutura do mural de avisos (design/3d/noticeboard.py): quadro largo (desktop) e alto (celular). */
export const NOTICEBOARD_NODES = {
  root: "NoticeBoards",
  wide: "NoticeBoardWide",
  tall: "NoticeBoardTall",
} as const;

/** Nomes dos nos no glTF (pivos: dobradica da tampa, fecho na frente, uma trava de cada lado). */
export const DECKBOX_NODES = {
  root: "DeckBox",
  lid: "Lid",
  clasp: "Clasp",
  latchL: "LatchL",
  latchR: "LatchR",
} as const;

export interface StageModels {
  readonly deckBox: Object3D;
  /** Altar de pedra com o circulo entalhado (tampo plano em y = 0, onde os tokens caem). */
  readonly altar: Object3D;
  /** Portal de pedra do caminho (chao em y = -0,4; a face da frente em z = +0,1 da raiz). */
  readonly portal: Object3D;
  /** Tronco gigante de referencia (h = 30, tw = 1,6; pe na origem): a landing o copia nas posicoes da silhueta. */
  readonly trunk: Object3D;
  /** Castical de madeira com vela (0,22 m; topo da cera em 0,21 m; pe na origem). */
  readonly candlestick: Object3D;
  /** Quatro pedras com musgo como filhos Rock0 a Rock3 (cada uma com o pe na origem do proprio no). */
  readonly rocks: Object3D;
  /** Toco de arvore (pe na origem). */
  readonly stump: Object3D;
  /** Raiz Torches com os nos StandTorch e WallTorch. */
  readonly torches: Object3D;
  /** Raiz NoticeBoards com as variantes NoticeBoardWide e NoticeBoardTall. */
  readonly noticeBoard: Object3D;
  /** Raiz Pieces com Medallion, ManaChip e Card. */
  readonly pieces: Object3D;
  /** Raiz Nature: Pine, Crown, TrunkLow e os portadores de material MatGround, MatCliff, MatLeaves. */
  readonly nature: Object3D;
  /** Raiz Buildings: TowerBody20/40/70, TowerSpire, RuinWall e RuinPath. */
  readonly buildings: Object3D;
}

export interface DeckBoxRig {
  readonly root: Object3D;
  readonly lid: Object3D;
  readonly clasp: Object3D;
  readonly latchL: Object3D;
  readonly latchR: Object3D;
  /** Materiais com runas no mapa emissivo: acendem com as fases do scroll. */
  readonly runeMaterials: MeshStandardMaterial[];
}

function required(root: Object3D, name: string): Object3D {
  const node = root.getObjectByName(name);
  if (!node) throw new Error(`modelo sem o no ${name}`);
  return node;
}

/** Malhas com sombra e materiais com mapa emissivo (apagados ate a fase acender). */
function prepare(root: Object3D): MeshStandardMaterial[] {
  const glow = new Set<MeshStandardMaterial>();
  root.traverse((o) => {
    if (!(o instanceof Mesh)) return;
    o.castShadow = true;
    o.receiveShadow = true;
    const materials: unknown[] = Array.isArray(o.material) ? o.material : [o.material];
    for (const m of materials)
      if (m instanceof MeshStandardMaterial && m.emissiveMap) {
        m.emissiveIntensity = 0;
        glow.add(m);
      }
  });
  return [...glow];
}

export interface AltarRig {
  readonly root: Object3D;
  readonly glowMaterials: MeshStandardMaterial[];
}

export function rigAltar(root: Object3D): AltarRig {
  return { root, glowMaterials: prepare(root) };
}

export function applyAltarGlow(rig: AltarRig, glow: number): void {
  for (const m of rig.glowMaterials) m.emissiveIntensity = glow;
}

export function rigDeckBox(root: Object3D): DeckBoxRig {
  const lid = required(root, DECKBOX_NODES.lid);
  const clasp = required(root, DECKBOX_NODES.clasp);
  const latchL = required(root, DECKBOX_NODES.latchL);
  const latchR = required(root, DECKBOX_NODES.latchR);
  return { root, lid, clasp, latchL, latchR, runeMaterials: prepare(root) };
}

export function applyDeckBoxPose(rig: DeckBoxRig, ph: BoxPhases): void {
  for (const m of rig.runeMaterials) m.emissiveIntensity = ph.runeGlow;
  // para fora e para baixo, na frente da caixa (o giro positivo levava o fecho para dentro, sobre as cartas)
  rig.clasp.rotation.x = -ph.clasp;
  rig.latchL.rotation.z = ph.latch;
  rig.latchR.rotation.z = -ph.latch;
  rig.lid.rotation.x = ph.lid;
}

/** Baixa e decodifica um modelo (GLTFLoader e o decodificador meshopt so entram quando o palco carrega). */
async function loadModel(url: string, rootName: string): Promise<Object3D> {
  const [{ GLTFLoader }, { MeshoptDecoder }] = await Promise.all([
    import("three/examples/jsm/loaders/GLTFLoader.js"),
    import("three/examples/jsm/libs/meshopt_decoder.module.js"),
  ]);
  const loader = new GLTFLoader();
  loader.setMeshoptDecoder(MeshoptDecoder);
  const gltf = await loader.loadAsync(url);
  return required(gltf.scene, rootName);
}

export async function loadStageModels(): Promise<StageModels> {
  const [
    deckBox,
    altar,
    portal,
    trunk,
    candlestick,
    rocks,
    stump,
    torches,
    noticeBoard,
    pieces,
    nature,
    buildings,
  ] = await Promise.all([
    loadModel(DECKBOX_URL, DECKBOX_NODES.root),
    loadModel(ALTAR_URL, ALTAR_ROOT),
    loadModel(PORTAL_URL, PORTAL_ROOT),
    loadModel(TRUNK_URL, TRUNK_ROOT),
    loadModel(CANDLESTICK_URL, CANDLESTICK_ROOT),
    loadModel(ROCKS_URL, ROCKS_ROOT),
    loadModel(STUMP_URL, STUMP_ROOT),
    loadModel(TORCHES_URL, TORCHES_NODES.root),
    loadModel(NOTICEBOARD_URL, NOTICEBOARD_NODES.root),
    loadModel(PIECES_URL, PIECES_NODES.root),
    loadModel(NATURE_URL, NATURE_NODES.root),
    loadModel(BUILDINGS_URL, BUILDINGS_NODES.root),
  ]);
  return {
    deckBox,
    altar,
    portal,
    trunk,
    candlestick,
    rocks,
    stump,
    torches,
    noticeBoard,
    pieces,
    nature,
    buildings,
  };
}

/** Copia um no do modelo com materiais proprios (cada copia pode receber a sua textura sem afetar as outras). */
export function cloneWithOwnMaterials(src: Object3D): Object3D {
  const copy = src.clone();
  copy.traverse((o) => {
    if (!(o instanceof Mesh)) return;
    o.material = Array.isArray(o.material)
      ? o.material.map((m: { clone(): unknown }) => m.clone())
      : (o.material as { clone(): unknown }).clone();
  });
  return copy;
}

/** O material de nome `name` dentro de um no (as faces das pecas: MedalFace, ChipFace, CardFace, ...). */
export function materialNamed(root: Object3D, name: string): MeshStandardMaterial {
  let found: MeshStandardMaterial | undefined;
  root.traverse((o) => {
    if (o instanceof Mesh && o.material instanceof MeshStandardMaterial && o.material.name === name)
      found = o.material;
  });
  if (!found) throw new Error(`modelo sem o material ${name}`);
  return found;
}

/** Troca um material (pelo nome do material do modelo) em todas as malhas de um no. */
export function replaceMaterial(root: Object3D, name: string, material: Material): void {
  root.traverse((o) => {
    if (o instanceof Mesh && !Array.isArray(o.material) && o.material.name === name)
      o.material = material;
  });
}
