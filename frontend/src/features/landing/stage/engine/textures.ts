// Texturas procedurais do palco, desenhadas em canvas 2D (sem imagens externas). So o chunk do palco as importa.
import { CanvasTexture, RepeatWrapping, SRGBColorSpace, Texture } from "three";

import { rng } from "../math";
import { MIST_SPRITE_PEAK } from "../mist";
import { CARD, WOOD } from "../palette";
import { RUNES, type Rune } from "../runes";
import type { ManaChip } from "../tokens";
import { drawCardBack, drawHookArt } from "./cardArt";

type Ctx = CanvasRenderingContext2D;

export function mkCanvas(w: number, h: number): HTMLCanvasElement {
  const c = document.createElement("canvas");
  c.width = w;
  c.height = h;
  return c;
}

export function ctxOf(c: HTMLCanvasElement): Ctx {
  return c.getContext("2d") as Ctx;
}

export function tex(c: HTMLCanvasElement): CanvasTexture {
  const t = new CanvasTexture(c);
  t.colorSpace = SRGBColorSpace;
  t.anisotropy = 8;
  return t;
}

export function roundRect(g: Ctx, x: number, y: number, w: number, h: number, r: number): void {
  g.beginPath();
  g.moveTo(x + r, y);
  g.arcTo(x + w, y, x + w, y + h, r);
  g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r);
  g.arcTo(x, y, x + w, y, r);
  g.closePath();
}

/** O simbolo do Deckloom (anel e D), igual ao do logo. */
export function symbol(
  g: Ctx,
  cx: number,
  cy: number,
  size: number,
  ring: string,
  fill: string,
): void {
  g.save();
  g.translate(cx, cy);
  g.scale(size / 80, size / 80);
  g.lineWidth = 4;
  g.strokeStyle = ring;
  g.beginPath();
  g.arc(0, 0, 34, 0, Math.PI * 2);
  g.stroke();
  g.fillStyle = fill;
  g.fill(new Path2D("M-11 -17h11c14 0 22 8 22 17s-8 17-22 17h-11Z"));
  g.restore();
}

function wrapText(g: Ctx, text: string, x: number, y: number, maxW: number, lh: number): void {
  let line = "";
  let yy = y;
  for (const word of text.split(" ")) {
    const test = line ? `${line} ${word}` : word;
    if (g.measureText(test).width > maxW && line) {
      g.fillText(line, x, yy);
      yy += lh;
      line = word;
    } else {
      line = test;
    }
  }
  g.fillText(line, x, yy);
}

function drawRune(g: Ctx, x: number, y: number, size: number, rune: Rune): void {
  g.beginPath();
  for (const s of rune) {
    g.moveTo(x + s[0] * size, y + s[1] * size);
    g.lineTo(x + s[2] * size, y + s[3] * size);
  }
  g.stroke();
}

export type SoftKind = "dot" | "leaf" | "shaft" | "mist";

/** Sprites suaves (poeira, folha, raio de luz, nevoa). */
export function softTexture(kind: SoftKind): CanvasTexture {
  const c = mkCanvas(128, 128);
  const g = ctxOf(c);
  if (kind === "dot") {
    const gr = g.createRadialGradient(64, 64, 0, 64, 64, 64);
    gr.addColorStop(0, "rgba(255,255,255,1)");
    gr.addColorStop(0.35, "rgba(255,255,255,.5)");
    gr.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = gr;
    g.fillRect(0, 0, 128, 128);
  } else if (kind === "leaf") {
    g.fillStyle = "#fff";
    g.beginPath();
    g.moveTo(64, 10);
    g.quadraticCurveTo(112, 60, 64, 118);
    g.quadraticCurveTo(16, 60, 64, 10);
    g.fill();
  } else if (kind === "shaft") {
    let gr = g.createLinearGradient(0, 0, 128, 0);
    gr.addColorStop(0, "rgba(255,255,255,0)");
    gr.addColorStop(0.5, "rgba(255,255,255,1)");
    gr.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = gr;
    g.fillRect(0, 0, 128, 128);
    g.globalCompositeOperation = "destination-in";
    gr = g.createLinearGradient(0, 0, 0, 128);
    gr.addColorStop(0, "rgba(0,0,0,0)");
    gr.addColorStop(0.25, "rgba(0,0,0,1)");
    gr.addColorStop(1, "rgba(0,0,0,0)");
    g.fillStyle = gr;
    g.fillRect(0, 0, 128, 128);
  } else {
    const gr = g.createRadialGradient(64, 64, 0, 64, 64, 64);
    gr.addColorStop(0, `rgba(255,255,255,${MIST_SPRITE_PEAK})`);
    gr.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = gr;
    g.fillRect(0, 0, 128, 128);
  }
  return new CanvasTexture(c);
}

export function cardBackTexture(): CanvasTexture {
  const c = mkCanvas(512, 716);
  drawCardBack(ctxOf(c));
  return tex(c);
}

export interface CardFace {
  readonly title: string;
  readonly type: string;
  readonly text: string;
}

/** A face de um gancho: faixa de titulo, janela com a arte propria do gancho, linha de tipo e caixa de texto. */
export function cardFrontTexture(face: CardFace, index: number): CanvasTexture {
  const c = mkCanvas(512, 716);
  const g = ctxOf(c);
  g.fillStyle = CARD.face;
  g.fillRect(0, 0, 512, 716);
  g.strokeStyle = CARD.gold;
  g.lineWidth = 10;
  roundRect(g, 5, 5, 502, 706, 26);
  g.stroke();
  g.fillStyle = CARD.band;
  roundRect(g, 30, 30, 452, 70, 14);
  g.fill();
  g.strokeStyle = CARD.line;
  g.lineWidth = 2;
  g.stroke();
  g.fillStyle = CARD.ink;
  g.font = '700 21px "Cinzel Decorative", Georgia, serif';
  g.textBaseline = "middle";
  g.fillText(face.title, 46, 66, 390);
  g.fillStyle = CARD.gold;
  g.font = '700 22px "Albert Sans", system-ui, sans-serif';
  g.fillText(String(index + 1), 454, 66);
  const ag = g.createRadialGradient(256, 260, 20, 256, 260, 240);
  ag.addColorStop(0, CARD.band);
  ag.addColorStop(1, "#c9b8f5");
  g.fillStyle = ag;
  roundRect(g, 30, 116, 452, 300, 14);
  g.fill();
  g.stroke();
  drawHookArt(g, index);
  g.fillStyle = CARD.muted;
  g.font = '600 21px "Albert Sans", system-ui, sans-serif';
  g.fillText(face.type, 44, 448, 424);
  g.strokeStyle = CARD.line;
  g.beginPath();
  g.moveTo(30, 428);
  g.lineTo(482, 428);
  g.moveTo(30, 470);
  g.lineTo(482, 470);
  g.stroke();
  g.fillStyle = "#ffffff";
  roundRect(g, 30, 486, 452, 168, 14);
  g.fill();
  g.stroke();
  g.fillStyle = CARD.ink;
  g.font = '500 22px "Albert Sans", system-ui, sans-serif';
  g.textBaseline = "top";
  wrapText(g, face.text, 50, 504, 412, 30);
  g.fillStyle = CARD.muted;
  g.font = '600 17px "Albert Sans", system-ui, sans-serif';
  g.fillText(`${index + 1}/4`, 40, 672);
  g.fillText("Deckloom", 400, 672);
  return tex(c);
}

/** Brilho dourado que atravessa a carta quando o gancho e revelado. */
export function sheenTexture(): CanvasTexture {
  const c = mkCanvas(512, 64);
  const g = ctxOf(c);
  const gr = g.createLinearGradient(0, 0, 512, 0);
  gr.addColorStop(0, "rgba(255,236,190,0)");
  gr.addColorStop(0.44, "rgba(255,236,190,0)");
  gr.addColorStop(0.5, "rgba(255,240,205,.85)");
  gr.addColorStop(0.56, "rgba(255,236,190,0)");
  gr.addColorStop(1, "rgba(255,236,190,0)");
  g.fillStyle = gr;
  g.fillRect(0, 0, 512, 64);
  return tex(c);
}

export type LogoImage = CanvasImageSource & {
  readonly naturalWidth: number;
  readonly naturalHeight: number;
};

/** Medalhao dourado com o logo oficial do jogo sobre uma placa clara. */
export function medallionTopTexture(img: LogoImage): CanvasTexture {
  const c = mkCanvas(512, 512);
  const g = ctxOf(c);
  const gr = g.createRadialGradient(256, 256, 180, 256, 256, 256);
  gr.addColorStop(0, "#f3dca6");
  gr.addColorStop(0.55, CARD.gold);
  gr.addColorStop(1, "#6b4a0a");
  g.fillStyle = gr;
  g.beginPath();
  g.arc(256, 256, 256, 0, Math.PI * 2);
  g.fill();
  g.strokeStyle = "rgba(255,240,200,.7)";
  g.lineWidth = 3;
  g.setLineDash([4, 10]);
  g.beginPath();
  g.arc(256, 256, 232, 0, Math.PI * 2);
  g.stroke();
  g.setLineDash([]);
  g.fillStyle = "#f5f1fb";
  g.beginPath();
  g.arc(256, 256, 212, 0, Math.PI * 2);
  g.fill();
  const sc = Math.min(330 / img.naturalWidth, 230 / img.naturalHeight);
  const w = img.naturalWidth * sc;
  const h = img.naturalHeight * sc;
  g.drawImage(img, 256 - w / 2, 256 - h / 2, w, h);
  return tex(c);
}

export function medallionBottomTexture(): CanvasTexture {
  const c = mkCanvas(256, 256);
  const g = ctxOf(c);
  g.fillStyle = "#2a1660";
  g.beginPath();
  g.arc(128, 128, 128, 0, Math.PI * 2);
  g.fill();
  symbol(g, 128, 128, 170, CARD.goldSoft, CARD.goldSoft);
  return tex(c);
}

/** Ficha de mana com o simbolo da fonte Mana (carregada pelo index.html). */
export function manaTopTexture(chip: ManaChip): CanvasTexture {
  const c = mkCanvas(256, 256);
  const g = ctxOf(c);
  g.fillStyle = WOOD.chipSide;
  g.beginPath();
  g.arc(128, 128, 128, 0, Math.PI * 2);
  g.fill();
  g.fillStyle = chip.color;
  g.beginPath();
  g.arc(128, 128, 112, 0, Math.PI * 2);
  g.fill();
  g.fillStyle = "#111";
  g.font = "150px Mana";
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.fillText(chip.glyph, 128, 136);
  return tex(c);
}
/** Pedras do caminho ate a clareira. */
export function pathTexture(): CanvasTexture {
  const c = mkCanvas(256, 1024);
  const g = ctxOf(c);
  const r = rng(31);
  g.fillStyle = "#15201a";
  g.fillRect(0, 0, 256, 1024);
  for (let y = 6; y < 1024; y += 30 + r() * 14) {
    for (let x = 8; x < 248; x += 40 + r() * 22) {
      const w = 34 + r() * 18;
      const h = 24 + r() * 10;
      const v = Math.floor(70 + r() * 30);
      g.fillStyle = `rgb(${v},${v - 2},${v + 12})`;
      roundRect(g, x, y, Math.min(w, 248 - x), h, 8);
      g.fill();
      if (r() < 0.45) {
        g.fillStyle = "rgba(70,120,64,.55)";
        g.beginPath();
        g.arc(x + r() * w, y + r() * h, 5 + r() * 9, 0, Math.PI * 2);
        g.fill();
      }
    }
  }
  const t = tex(c);
  t.wrapT = RepeatWrapping;
  t.repeat.set(1, 4);
  return t;
}

export function glowTexture(): CanvasTexture {
  const c = mkCanvas(256, 256);
  const g = ctxOf(c);
  const gr = g.createRadialGradient(128, 128, 0, 128, 128, 128);
  gr.addColorStop(0, "rgba(255,248,210,1)");
  gr.addColorStop(0.35, "rgba(214,240,150,.55)");
  gr.addColorStop(1, "rgba(214,240,150,0)");
  g.fillStyle = gr;
  g.fillRect(0, 0, 256, 256);
  return new CanvasTexture(c);
}

/** Runa brilhante (sprite aditivo) para o arco do portal e o caminho. */
export function runeGlowTexture(k: number): Texture {
  const c = mkCanvas(128, 128);
  const g = ctxOf(c);
  const gg = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  gg.addColorStop(0, "rgba(255,255,255,.35)");
  gg.addColorStop(1, "rgba(255,255,255,0)");
  g.fillStyle = gg;
  g.fillRect(0, 0, 128, 128);
  g.strokeStyle = "#fff";
  g.lineWidth = 13;
  g.lineCap = "round";
  drawRune(g, 26, 26, 76, RUNES[30 + k]);
  return new CanvasTexture(c);
}
