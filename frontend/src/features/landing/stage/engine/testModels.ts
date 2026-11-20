// Modelos falsos para os testes do palco: mesma hierarquia de nos do glTF real, geometria minima.
import { BoxGeometry, Group, Mesh, MeshStandardMaterial, Texture } from "three";

import type { StageModels } from "./deckBoxModel";

function node(name: string, parent: Group, material: MeshStandardMaterial): Group {
  const g = new Group();
  g.name = name;
  const mesh = new Mesh(new BoxGeometry(0.1, 0.1, 0.1), material);
  mesh.name = `${name}Mesh`;
  g.add(mesh);
  parent.add(g);
  return g;
}

export function fakeDeckBox(): Group {
  const root = new Group();
  root.name = "DeckBox";
  const metal = new MeshStandardMaterial();
  const runes = new MeshStandardMaterial({ emissiveMap: new Texture(), emissiveIntensity: 1 });
  const body = new Mesh(new BoxGeometry(1, 1, 1), [metal, runes]);
  body.name = "DeckBoxMesh";
  root.add(body);
  for (const name of ["Lid", "Clasp", "LatchL", "LatchR"]) node(name, root, metal);
  return root;
}

export function fakeAltar(): Group {
  const root = new Group();
  root.name = "Altar";
  const top = new Mesh(
    new BoxGeometry(13, 0.25, 9.2).translate(0, -0.125, 0),
    new MeshStandardMaterial({ emissiveMap: new Texture(), emissiveIntensity: 1 }),
  );
  top.name = "AltarMesh";
  root.add(top);
  return root;
}

export function fakePortal(): Group {
  const root = new Group();
  root.name = "Portal";
  const wall = new Mesh(new BoxGeometry(120, 10, 2.4), new MeshStandardMaterial());
  wall.name = "PortalMesh";
  root.add(wall);
  return root;
}

export function fakeTrunk(): Group {
  const root = new Group();
  root.name = "Trunk";
  const body = new Mesh(new BoxGeometry(2, 24, 2).translate(0, 12, 0), new MeshStandardMaterial());
  body.name = "TrunkMesh";
  root.add(body);
  return root;
}

function single(name: string, w: number, h: number): Group {
  const root = new Group();
  root.name = name;
  const mesh = new Mesh(
    new BoxGeometry(w, h, w).translate(0, h / 2, 0),
    new MeshStandardMaterial(),
  );
  mesh.name = `${name}Mesh`;
  root.add(mesh);
  return root;
}

export function fakeRocks(): Group {
  const root = new Group();
  root.name = "Rocks";
  for (let i = 0; i < 4; i++) root.add(single(`Rock${i}`, 2, 1.2));
  return root;
}

export function fakeTorches(): Group {
  const root = new Group();
  root.name = "Torches";
  root.add(single("StandTorch", 0.4, 2.8));
  const wall = single("WallTorch", 0.1, 0.45);
  const wrap = new MeshStandardMaterial();
  wrap.name = "TorchWrap";
  wall.add(new Mesh(new BoxGeometry(0.1, 0.1, 0.1), wrap));
  root.add(wall);
  return root;
}

export function fakeNoticeBoard(): Group {
  const root = new Group();
  root.name = "NoticeBoards";
  root.add(single("NoticeBoardWide", 4.6, 3.1), single("NoticeBoardTall", 2.75, 3.9));
  return root;
}

function faced(name: string, mats: string[]): Group {
  const root = new Group();
  root.name = name;
  for (const m of mats) {
    const mat = new MeshStandardMaterial();
    mat.name = m;
    root.add(new Mesh(new BoxGeometry(0.1, 0.1, 0.1), mat));
  }
  return root;
}

export function fakePieces(): Group {
  const root = new Group();
  root.name = "Pieces";
  root.add(
    faced("Medallion", ["MedalGold", "MedalFace", "MedalBack"]),
    faced("ManaChip", ["ChipSide", "ChipStripe", "ChipFace"]),
    faced("Card", ["CardEdge", "CardFace", "CardBack"]),
  );
  return root;
}

export function fakeNature(): Group {
  const root = new Group();
  root.name = "Nature";
  root.add(
    faced("Pine0", ["Bark", "Leaves"]),
    faced("Pine1", ["Bark", "Leaves"]),
    faced("Pine2", ["Bark", "Leaves"]),
    faced("Broadleaf0", ["Bark", "Leaves"]),
    faced("Broadleaf1", ["Bark", "Leaves"]),
    faced("Broadleaf2", ["Bark", "Leaves"]),
    faced("LeafCrown", ["Leaves"]),
    faced("Bush", ["Leaves"]),
    faced("MatGround", ["Ground"]),
    faced("MatCliff", ["Cliff"]),
    faced("MatLeaves", ["Leaves"]),
    faced("MatMoon", ["Moon"]),
  );
  return root;
}

export function fakeBuildings(): Group {
  const root = new Group();
  root.name = "Buildings";
  root.add(
    faced("TowerBody20", ["BldStone", "BldTrim", "WindowGlow"]),
    faced("TowerBody40", ["BldStone", "BldTrim", "WindowGlow"]),
    faced("TowerBody70", ["BldStone", "BldTrim", "WindowGlow"]),
    faced("TowerSpire", ["BldRoof", "BldTrim"]),
    faced("RuinWall", ["BldStone", "BldMoss"]),
    faced("RuinPath", ["BldStone", "BldMoss"]),
  );
  return root;
}

export function fakeModels(): StageModels {
  return {
    deckBox: fakeDeckBox(),
    altar: fakeAltar(),
    portal: fakePortal(),
    trunk: fakeTrunk(),
    candlestick: single("Candlestick", 0.1, 0.22),
    rocks: fakeRocks(),
    stump: single("Stump", 1.4, 0.57),
    torches: fakeTorches(),
    noticeBoard: fakeNoticeBoard(),
    pieces: fakePieces(),
    nature: fakeNature(),
    buildings: fakeBuildings(),
  };
}
