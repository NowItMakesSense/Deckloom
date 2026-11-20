// Texturas do mural de avisos (REQ-L13): tabuas, vigas, telhado, placa entalhada e os dois avisos dos planos,
// desenhados em canvas 2D (sem imagens externas). O que vai escrito nos avisos e onde vem de board.ts (puro).
import { type CanvasTexture, RepeatWrapping } from "three";

import { noticeLayout, TITLE_FAMILY, type NoticeInk, type NoticeKind } from "../board";
import { rng } from "../math";
import { BOARD } from "../palette";
import { ctxOf, mkCanvas, roundRect, symbol, tex } from "./textures";

type Ctx = CanvasRenderingContext2D;
const TAU = Math.PI * 2;

/** Veios da madeira (riscos finos, verticais ou horizontais). */
function grain(
  g: Ctx,
  x: number,
  y: number,
  w: number,
  h: number,
  r: () => number,
  n: number,
  vertical: boolean,
): void {
  for (let k = 0; k < n; k++) {
    g.strokeStyle = `rgba(${18 + r() * 30},${10 + r() * 16},${5 + r() * 8},${0.15 + r() * 0.25})`;
    g.lineWidth = 1 + r() * 1.6;
    g.beginPath();
    if (vertical) {
      const gx = x + r() * w;
      const y0 = y + r() * h;
      g.moveTo(gx, y0);
      g.lineTo(gx + (r() - 0.5) * 5, y0 + 40 + r() * h * 0.6);
    } else {
      const gy = y + r() * h;
      const x0 = x + r() * w;
      g.moveTo(x0, gy);
      g.lineTo(x0 + 40 + r() * w * 0.6, gy + (r() - 0.5) * 5);
    }
    g.stroke();
  }
}

/** Viga entalhada (as vigas do quadro e, girada, os postes). */
export function beamTexture(vertical: boolean): CanvasTexture {
  const c = mkCanvas(512, 128);
  const g = ctxOf(c);
  const r = rng(vertical ? 73 : 79);
  g.fillStyle = BOARD.beamBase;
  g.fillRect(0, 0, 512, 128);
  grain(g, 0, 0, 512, 128, r, 220, false);
  g.strokeStyle = "rgba(10,5,2,.6)";
  g.lineWidth = 3;
  g.beginPath();
  for (let x = 0; x <= 512; x += 32) {
    g.moveTo(x, 64);
    g.quadraticCurveTo(x + 8, 40, x + 16, 64);
    g.quadraticCurveTo(x + 24, 88, x + 32, 64);
  }
  g.stroke();
  g.strokeStyle = "rgba(150,105,60,.25)";
  g.lineWidth = 1.5;
  g.strokeRect(6, 18, 500, 92);
  const t = tex(c);
  t.wrapS = t.wrapT = RepeatWrapping;
  if (vertical) {
    t.center.set(0.5, 0.5);
    t.rotation = Math.PI / 2;
  }
  return t;
}

/** Placa entalhada com o texto em dourado e o simbolo do Deckloom nas pontas; o texto e medido para caber. */
export function signTexture(text: string, w: number, h: number): CanvasTexture {
  const W = 1024;
  const H = Math.round((1024 * h) / w);
  const c = mkCanvas(W, H);
  const g = ctxOf(c);
  const r = rng(89);
  g.fillStyle = BOARD.signBase;
  g.fillRect(0, 0, W, H);
  grain(g, 0, 0, W, H, r, 260, false);
  g.strokeStyle = "rgba(8,4,2,.8)";
  g.lineWidth = 8;
  roundRect(g, 10, 10, W - 20, H - 20, 14);
  g.stroke();
  g.strokeStyle = "rgba(200,150,80,.35)";
  g.lineWidth = 2;
  roundRect(g, 22, 22, W - 44, H - 44, 10);
  g.stroke();
  let fs = Math.min(H * 0.5, 1700 / Math.max(10, text.length));
  g.font = `700 ${Math.round(fs)}px ${TITLE_FAMILY}`;
  const tw = g.measureText(text).width;
  if (tw > W * 0.7) {
    fs *= (W * 0.7) / tw;
    g.font = `700 ${Math.round(fs)}px ${TITLE_FAMILY}`;
  }
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.fillStyle = "rgba(0,0,0,.75)";
  g.fillText(text, W / 2 + 3, H / 2 + 5);
  g.fillStyle = BOARD.signInk;
  g.fillText(text, W / 2, H / 2 + 2);
  for (const x of [W * 0.075, W * 0.925]) {
    symbol(g, x, H / 2, Math.min(H * 0.55, W * 0.1), BOARD.signInk, BOARD.signInk);
  }
  return tex(c);
}

/** Contorno irregular do pergaminho. */
function parchShape(g: Ctx, W: number, H: number, seed: number, pad: number): void {
  const r = rng(seed);
  const j = (k: number) => (r() - 0.5) * k;
  g.beginPath();
  g.moveTo(pad, pad);
  for (let x = pad; x <= W - pad; x += 20) g.lineTo(x, pad + j(12));
  for (let y = pad; y <= H - pad; y += 20) g.lineTo(W - pad + j(12), y);
  for (let x = W - pad; x >= pad; x -= 20) g.lineTo(x, H - pad + j(12));
  for (let y = H - pad; y >= pad; y -= 20) g.lineTo(pad + j(12), y);
  g.closePath();
}

const INK: Readonly<Record<NoticeInk, string>> = {
  ink: BOARD.ink,
  red: BOARD.red,
  gold: BOARD.gold,
};

function diamond(g: Ctx, x: number, y: number): void {
  g.fillStyle = BOARD.red;
  g.save();
  g.translate(x, y);
  g.rotate(Math.PI / 4);
  g.fillRect(-8, -8, 16, 16);
  g.restore();
}

function seal(g: Ctx, x: number, y: number, rad: number): void {
  g.fillStyle = BOARD.seal;
  g.beginPath();
  for (let a = 0; a <= 24; a++) {
    const an = (a / 24) * TAU;
    const rr = rad * (1 + (a % 2 ? 0.06 : -0.04));
    g.lineTo(x + Math.cos(an) * rr, y + Math.sin(an) * rr);
  }
  g.fill();
  const sg = g.createRadialGradient(x - rad * 0.3, y - rad * 0.3, 2, x, y, rad);
  sg.addColorStop(0, BOARD.sealLight);
  sg.addColorStop(1, BOARD.sealDark);
  g.fillStyle = sg;
  g.beginPath();
  g.arc(x, y, rad * 0.8, 0, TAU);
  g.fill();
  symbol(g, x, y, rad * 1.25, BOARD.signInk, BOARD.signInk);
}

/**
 * Um aviso de plano (Gratis ou Arcano) em pergaminho: papel com manchas e o texto do layout puro (noticeLayout).
 * Deitado (w/h > 1,2, celular), titulo e preco dividem a linha.
 */
export function noticeTexture(kind: NoticeKind, w: number, h: number): CanvasTexture {
  const free = kind === "free";
  const seed = free ? 67 : 61;
  const W = 1024;
  const H = Math.round((1024 * h) / w);
  const c = mkCanvas(W, H);
  const g = ctxOf(c);
  const r = rng(seed + 1);
  g.save();
  parchShape(g, W, H, seed, 12);
  g.clip();
  const base = g.createRadialGradient(
    W / 2,
    H / 2,
    Math.min(W, H) * 0.25,
    W / 2,
    H / 2,
    Math.max(W, H) * 0.78,
  );
  base.addColorStop(0, BOARD.parchment[0]);
  base.addColorStop(0.7, BOARD.parchment[1]);
  base.addColorStop(1, BOARD.parchment[2]);
  g.fillStyle = base;
  g.fillRect(0, 0, W, H);
  for (let k = 0; k < 2600; k++) {
    g.fillStyle = `rgba(120,80,30,${r() * 0.06})`;
    g.fillRect(r() * W, r() * H, 1 + r() * 3, 1 + r() * 3);
  }
  for (let k = 0; k < 5; k++) {
    const sx = r() * W;
    const sy = r() * H;
    const sg = g.createRadialGradient(sx, sy, 0, sx, sy, 40 + r() * 110);
    sg.addColorStop(0, "rgba(140,95,40,.14)");
    sg.addColorStop(1, "rgba(140,95,40,0)");
    g.fillStyle = sg;
    g.fillRect(0, 0, W, H);
  }
  g.restore();
  parchShape(g, W, H, seed, 12);
  g.strokeStyle = "rgba(90,60,25,.6)";
  g.lineWidth = 3;
  g.stroke();

  g.textBaseline = "alphabetic";
  const measure = (font: string, text: string) => {
    g.font = font;
    return g.measureText(text).width;
  };
  for (const op of noticeLayout(kind, W, H, w / h > 1.2, measure)) {
    if (op.op === "rule") {
      g.strokeStyle = "rgba(36,22,11,.5)";
      g.lineWidth = 3;
      g.beginPath();
      g.moveTo(op.x0, op.y);
      g.lineTo(op.x1, op.y);
      g.stroke();
      diamond(g, (op.x0 + op.x1) / 2, op.y);
    } else if (op.op === "seal") {
      seal(g, op.x, op.y, op.r);
    } else if (op.op === "bullet") {
      diamond(g, op.x, op.y - op.size * 0.32);
      g.font = op.font;
      g.textAlign = "left";
      g.fillStyle = INK[op.color];
      g.fillText(op.text, op.x + 30, op.y);
    } else {
      g.font = op.font;
      g.textAlign = op.align;
      g.fillStyle = INK[op.color];
      g.fillText(op.text, op.x, op.y);
    }
  }
  return tex(c);
}
