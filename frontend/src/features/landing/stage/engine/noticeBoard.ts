// O mural de avisos (REQ-L13) na cena: a estrutura do noticeboard.glb (postes, tabuas, vigas, telhado), a placa
// "Mural de avisos" e os dois avisos dos planos presos com tachas. Fica ao lado do portal, revelado pelo pan final.
// Medidas em board.ts (puro).
import {
  BoxGeometry,
  Color,
  CylinderGeometry,
  DoubleSide,
  Group,
  Mesh,
  MeshStandardMaterial,
  PlaneGeometry,
  Vector3,
  type Object3D,
  type Scene,
} from "three";

import type { BoardLayout } from "../board";
import { BOARD, FIRE } from "../palette";
import { beamTexture, noticeTexture, signTexture } from "./boardTextures";
import { NOTICEBOARD_NODES } from "./deckBoxModel";

/**
 * O mural: a estrutura (postes, tabuas, vigas, cantoneiras e telhado) vem do noticeboard.glb, na variante do formato
 * do quadro; a placa e os avisos com texto sao desenhados aqui.
 */
export function buildNoticeBoard(scene: Scene, b: BoardLayout, model: Object3D): Group {
  const board = new Group();
  board.name = "mural-de-avisos";
  board.position.set(b.center[0], 0, b.center[2]);
  board.rotation.y = b.yaw;
  scene.add(board);

  const variant = b.w >= b.h ? NOTICEBOARD_NODES.wide : NOTICEBOARD_NODES.tall;
  const src = model.getObjectByName(variant);
  if (!src) throw new Error(`modelo sem a variante ${variant}`);
  const frame = src.clone();
  frame.position.set(0, 0, 0);
  board.add(frame);

  // placa entalhada com o titulo, presa no topo do quadro
  const beam = new MeshStandardMaterial({
    map: beamTexture(false),
    color: BOARD.beam,
    roughness: 0.85,
  });
  const sign = new Mesh(new BoxGeometry(b.signW, b.signH, 0.05), [
    beam,
    beam,
    beam,
    beam,
    new MeshStandardMaterial({
      map: signTexture("MURAL DE AVISOS", b.signW, b.signH),
      roughness: 0.8,
    }),
    beam,
  ]);
  sign.name = "placa-mural";
  sign.position.set(0, b.top - 0.26, 0.06);
  board.add(sign);

  const brass = new MeshStandardMaterial({ color: FIRE.brass, metalness: 0.9, roughness: 0.3 });
  b.notes.forEach((nt, ni) => {
    // papel levemente curvado, com a ponta de baixo enrolando
    const geo = new PlaneGeometry(nt.w, nt.h, 12, 12);
    const pa = geo.attributes.position;
    for (let v = 0; v < pa.count; v++) {
      const vx = pa.getX(v) / (nt.w / 2);
      const vy = pa.getY(v) / (nt.h / 2);
      pa.setZ(
        v,
        0.012 * (1 - vx * vx) +
          0.07 * Math.pow(Math.max(0, -vy - 0.35), 2) * Math.abs(vx) +
          0.03 * Math.pow(Math.max(0, vy - 0.6), 2) * (vx > 0 ? 1 : 0),
      );
    }
    geo.computeVertexNormals();
    const map = noticeTexture(nt.kind, nt.w, nt.h);
    const note = new Mesh(
      geo,
      new MeshStandardMaterial({
        map,
        transparent: true,
        alphaTest: 0.5,
        roughness: 0.92,
        emissive: new Color(BOARD.noticeGlow),
        emissiveMap: map,
        emissiveIntensity: 0.26,
        side: DoubleSide,
      }),
    );
    note.name = `aviso-${nt.kind}`;
    note.position.set(nt.x, nt.y, 0.08 + ni * 0.01);
    note.rotation.z = nt.rz;
    board.add(note);
    const pins =
      nt.kind === "paid"
        ? [
            [-1, 1],
            [1, 1],
            [-1, -1],
            [1, -1],
          ]
        : [
            [-1, 1],
            [1, 1],
          ];
    const axis = new Vector3(0, 0, 1);
    for (const [px, py] of pins) {
      const pin = new Mesh(new CylinderGeometry(0.035, 0.03, 0.025, 12), brass);
      pin.name = "tacha";
      pin.rotation.x = Math.PI / 2;
      pin.position.set(px * (nt.w / 2 - 0.13), py * (nt.h / 2 - 0.12), 0.03);
      pin.position.applyAxisAngle(axis, nt.rz);
      pin.position.add(note.position);
      pin.position.z += 0.03;
      board.add(pin);
    }
  });
  return board;
}
