// Deck box em glTF (T-L05, AC-L05a): a landing anima tampa, fecho e travas pelos nos do modelo carregado.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { Group, Mesh, MeshStandardMaterial, Texture } from "three";
import { describe, expect, it } from "vitest";

import { boxPhases } from "../phases";
import {
  ALTAR_ROOT,
  DECKBOX_NODES,
  PORTAL_ROOT,
  TRUNK_ROOT,
  CANDLESTICK_ROOT,
  ROCKS_ROOT,
  STUMP_ROOT,
  TORCHES_NODES,
  NOTICEBOARD_NODES,
  PIECES_NODES,
  applyAltarGlow,
  applyDeckBoxPose,
  rigAltar,
  rigDeckBox,
} from "./deckBoxModel";
import { fakeAltar, fakeDeckBox } from "./testModels";

const GLB = resolve(__dirname, "../../../../../public/models/deckbox.glb");

interface GltfJson {
  nodes: { name?: string; children?: number[]; mesh?: number }[];
  scenes: { nodes: number[] }[];
  extensionsUsed?: string[];
  materials: { emissiveTexture?: unknown; emissiveFactor?: number[] }[];
}

/** Le o chunk JSON de um .glb (cabecalho de 12 bytes, depois chunk de tamanho + tipo + dados). */
function glbJson(path: string): GltfJson {
  const buf = readFileSync(path);
  expect(buf.toString("ascii", 0, 4)).toBe("glTF");
  const len = buf.readUInt32LE(12);
  expect(buf.toString("ascii", 16, 20)).toBe("JSON");
  return JSON.parse(buf.toString("utf8", 20, 20 + len)) as GltfJson;
}

describe("deckbox.glb (hierarquia exportada do Blender)", () => {
  const json = glbJson(GLB);
  const byName = new Map(json.nodes.map((n, i) => [n.name, i]));

  it("tem a raiz DeckBox na cena e os nos articulaveis como filhos diretos dela", () => {
    const root = byName.get(DECKBOX_NODES.root);
    expect(root).toBeDefined();
    expect(json.scenes[0].nodes).toContain(root);
    for (const name of [
      DECKBOX_NODES.lid,
      DECKBOX_NODES.clasp,
      DECKBOX_NODES.latchL,
      DECKBOX_NODES.latchR,
    ]) {
      const i = byName.get(name);
      expect(i, name).toBeDefined();
      expect(json.nodes[root as number].children).toContain(i);
      // cada no articulavel carrega a propria malha num filho (pivo separado da geometria)
      const kids = json.nodes[i as number].children ?? [];
      expect(
        kids.some((k) => json.nodes[k].mesh !== undefined),
        name,
      ).toBe(true);
    }
  });

  it("as runas ficam num mapa emissivo proprio e a geometria vem comprimida com meshopt", () => {
    expect(json.materials.filter((m) => m.emissiveTexture).length).toBeGreaterThanOrEqual(3);
    expect(json.extensionsUsed).toContain("EXT_meshopt_compression");
  });
});

describe("rigDeckBox", () => {
  it("acha tampa, fecho e travas pelo nome e os materiais com runas", () => {
    const rig = rigDeckBox(fakeDeckBox());
    expect(rig.lid.name).toBe("Lid");
    expect(rig.clasp.name).toBe("Clasp");
    expect(rig.latchL.name).toBe("LatchL");
    expect(rig.latchR.name).toBe("LatchR");
    expect(rig.runeMaterials).toHaveLength(1);
  });

  it("comeca com as runas apagadas e as malhas fazendo e recebendo sombra", () => {
    const rig = rigDeckBox(fakeDeckBox());
    expect(rig.runeMaterials[0].emissiveIntensity).toBe(0);
    rig.root.traverse((o) => {
      if (o instanceof Mesh) {
        expect(o.castShadow).toBe(true);
        expect(o.receiveShadow).toBe(true);
      }
    });
  });

  it("falha com mensagem clara se faltar um no articulavel", () => {
    const root = new Group();
    root.name = "DeckBox";
    expect(() => rigDeckBox(root)).toThrow(/Lid/);
  });
});

describe("applyDeckBoxPose", () => {
  it("anima tampa, fecho, travas e runas a partir das fases do scroll", () => {
    const rig = rigDeckBox(fakeDeckBox());
    // procura um ponto da sequencia em que a caixa ja esta abrindo
    let raw = 0;
    let ph = boxPhases(raw, 0);
    while (raw < 1 && (ph.lid === 0 || ph.runeGlow === 0)) {
      raw += 0.001;
      ph = boxPhases(raw, 0);
    }
    applyDeckBoxPose(rig, ph);
    expect(rig.lid.rotation.x).toBe(ph.lid);
    // o fecho gira para fora e para baixo, na frente da caixa (girar para dentro o deixava flutuando sobre as cartas)
    expect(rig.clasp.rotation.x).toBe(-ph.clasp);
    expect(rig.latchL.rotation.z).toBe(ph.latch);
    expect(rig.latchR.rotation.z).toBe(-ph.latch);
    expect(rig.runeMaterials[0].emissiveIntensity).toBe(ph.runeGlow);
    expect(ph.lid).not.toBe(0);
  });
});

describe("fakeDeckBox", () => {
  it("usa material com mapa emissivo, como o glTF real", () => {
    let found = false;
    fakeDeckBox().traverse((o) => {
      if (!(o instanceof Mesh)) return;
      const materials: unknown[] = Array.isArray(o.material) ? o.material : [o.material];
      if (
        materials.some((m) => m instanceof MeshStandardMaterial && m.emissiveMap instanceof Texture)
      )
        found = true;
    });
    expect(found).toBe(true);
  });
});

describe("altar.glb (T-L05, AC-L05b)", () => {
  const json = glbJson(resolve(__dirname, "../../../../../public/models/altar.glb"));

  it("tem a raiz Altar na cena, o circulo e os entalhes em mapas emissivos proprios", () => {
    const root = json.nodes.findIndex((n) => n.name === ALTAR_ROOT);
    expect(root).toBeGreaterThanOrEqual(0);
    expect(json.scenes[0].nodes).toContain(root);
    expect(json.materials.filter((m) => m.emissiveTexture).length).toBeGreaterThanOrEqual(3);
  });

  it("o brilho do altar segue a fase altarGlow e comeca apagado", () => {
    const rig = rigAltar(fakeAltar());
    expect(rig.glowMaterials.length).toBeGreaterThan(0);
    for (const m of rig.glowMaterials) expect(m.emissiveIntensity).toBe(0);
    applyAltarGlow(rig, 0.8);
    for (const m of rig.glowMaterials) expect(m.emissiveIntensity).toBe(0.8);
  });
});

describe("portal.glb (T-L05, AC-L05b)", () => {
  const json = glbJson(resolve(__dirname, "../../../../../public/models/portal.glb"));

  it("tem a raiz Portal na cena, com aduelas, torres e muro numa malha so", () => {
    const root = json.nodes.findIndex((n) => n.name === PORTAL_ROOT);
    expect(root).toBeGreaterThanOrEqual(0);
    expect(json.scenes[0].nodes).toContain(root);
    expect(json.nodes[root].children?.some((k) => json.nodes[k].mesh !== undefined)).toBe(true);
  });
});

describe("trunk.glb (T-L05, AC-L05b)", () => {
  const json = glbJson(resolve(__dirname, "../../../../../public/models/trunk.glb"));

  it("tem a raiz Trunk na cena com a malha do tronco, raizes e galhos", () => {
    const root = json.nodes.findIndex((n) => n.name === TRUNK_ROOT);
    expect(root).toBeGreaterThanOrEqual(0);
    expect(json.scenes[0].nodes).toContain(root);
    expect(json.nodes[root].children?.some((k) => json.nodes[k].mesh !== undefined)).toBe(true);
  });
});

describe("modelos do Poly Haven reduzidos (castical, pedras, toco)", () => {
  const read = (f: string) => glbJson(resolve(__dirname, `../../../../../public/models/${f}`));

  it.each([
    ["candlestick.glb", CANDLESTICK_ROOT],
    ["stump.glb", STUMP_ROOT],
  ])("%s tem a raiz %s na cena com malha", (file, rootName) => {
    const json = read(file);
    const root = json.nodes.findIndex((n) => n.name === rootName);
    expect(root).toBeGreaterThanOrEqual(0);
    expect(json.scenes[0].nodes).toContain(root);
    expect(json.nodes[root].children?.some((k) => json.nodes[k].mesh !== undefined)).toBe(true);
  });

  it("rocks.glb tem a raiz Rocks com as quatro pedras como nos separados (Rock0 a Rock3)", () => {
    const json = read("rocks.glb");
    const root = json.nodes.findIndex((n) => n.name === ROCKS_ROOT);
    expect(json.scenes[0].nodes).toContain(root);
    for (let i = 0; i < 4; i++) {
      const k = json.nodes.findIndex((n) => n.name === `Rock${i}`);
      expect(json.nodes[root].children, `Rock${i}`).toContain(k);
      expect(json.nodes[k].children?.some((c) => json.nodes[c].mesh !== undefined)).toBe(true);
    }
  });
});

describe("torches.glb (T-L05)", () => {
  const json = glbJson(resolve(__dirname, "../../../../../public/models/torches.glb"));

  it("tem a raiz Torches com a tocha de tripe e a de parede como nos separados, cada um com malha", () => {
    const root = json.nodes.findIndex((n) => n.name === TORCHES_NODES.root);
    expect(json.scenes[0].nodes).toContain(root);
    for (const name of [TORCHES_NODES.stand, TORCHES_NODES.wall]) {
      const k = json.nodes.findIndex((n) => n.name === name);
      expect(json.nodes[root].children, name).toContain(k);
      expect(json.nodes[k].children?.some((c) => json.nodes[c].mesh !== undefined)).toBe(true);
    }
  });
});

describe("noticeboard.glb (T-L05)", () => {
  const json = glbJson(resolve(__dirname, "../../../../../public/models/noticeboard.glb"));

  it("tem as duas variantes do mural (desktop e celular) como nos da raiz", () => {
    const root = json.nodes.findIndex((n) => n.name === NOTICEBOARD_NODES.root);
    expect(json.scenes[0].nodes).toContain(root);
    for (const name of [NOTICEBOARD_NODES.wide, NOTICEBOARD_NODES.tall]) {
      const k = json.nodes.findIndex((n) => n.name === name);
      expect(json.nodes[root].children, name).toContain(k);
      expect(json.nodes[k].children?.some((c) => json.nodes[c].mesh !== undefined)).toBe(true);
    }
  });
});

describe("pieces.glb (T-L05, AC-L05b e AC-L05c)", () => {
  const json = glbJson(
    resolve(__dirname, "../../../../../public/models/pieces.glb"),
  ) as GltfJson & {
    meshes: { primitives: { material?: number }[] }[];
    materials: { name?: string }[];
  };

  it("tem medalhao, ficha de mana e carta como nos da raiz Pieces, com as faces em materiais proprios", () => {
    const root = json.nodes.findIndex((n) => n.name === PIECES_NODES.root);
    expect(json.scenes[0].nodes).toContain(root);
    const faces: Record<string, string[]> = {
      [PIECES_NODES.medal]: ["MedalFace", "MedalBack"],
      [PIECES_NODES.chip]: ["ChipFace"],
      [PIECES_NODES.card]: ["CardFace", "CardBack", "CardEdge"],
    };
    for (const [name, mats] of Object.entries(faces)) {
      const k = json.nodes.findIndex((n) => n.name === name);
      expect(json.nodes[root].children, name).toContain(k);
      const meshNode = (json.nodes[k].children ?? []).find(
        (c) => json.nodes[c].mesh !== undefined,
      )!;
      const prims = json.meshes[json.nodes[meshNode].mesh as number].primitives;
      const names = prims.map((p) => json.materials[p.material as number].name);
      for (const m of mats) expect(names, name).toContain(m);
    }
  });
});
