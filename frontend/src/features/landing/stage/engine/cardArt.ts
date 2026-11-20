// Arte propria das cartas do Deckloom (T-L05, AC-L05c): uma ilustracao por gancho na janela de arte da frente e o
// verso com o selo de runas. Linha de ouro sobre o violeta da marca, em coordenadas absolutas (sem transformacoes),
// para o desenho caber sempre na janela.
import { CARD } from "../palette";

type Ctx = CanvasRenderingContext2D;

/** Janela de arte da frente da carta (canvas de 512 x 716). */
export const ART_WINDOW = { x: 30, y: 116, w: 452, h: 300 } as const;
const CX = ART_WINDOW.x + ART_WINDOW.w / 2;
const CY = ART_WINDOW.y + ART_WINDOW.h / 2;
const TAU = Math.PI * 2;

/** Uma arte por gancho, na ordem de HOOKS: colecao, meta dos torneios, lojas e IA. */
export const HOOK_ARTS = ["colecao", "torneio", "lojas", "oraculo"] as const;

function line(g: Ctx, pts: ReadonlyArray<readonly [number, number]>, close = false): void {
  g.beginPath();
  g.moveTo(pts[0][0], pts[0][1]);
  for (const p of pts.slice(1)) g.lineTo(p[0], p[1]);
  if (close) g.closePath();
}

function circle(g: Ctx, x: number, y: number, r: number): void {
  g.beginPath();
  g.arc(x, y, r, 0, TAU);
}

/** Retangulo girado em torno do proprio centro (a carta do leque). */
function rotRect(g: Ctx, cx: number, cy: number, w: number, h: number, a: number): void {
  const c = Math.cos(a);
  const s = Math.sin(a);
  const corners: Array<[number, number]> = [
    [-w / 2, -h / 2],
    [w / 2, -h / 2],
    [w / 2, h / 2],
    [-w / 2, h / 2],
  ].map(([x, y]) => [cx + x * c - y * s, cy + x * s + y * c]);
  line(g, corners, true);
}

/** O simbolo do Deckloom (anel e D) sem transformacoes. */
function deckloomMark(g: Ctx, cx: number, cy: number, r: number, ring: string, fill: string): void {
  g.strokeStyle = ring;
  g.lineWidth = r * 0.1;
  circle(g, cx, cy, r);
  g.stroke();
  const k = r / 34;
  g.fillStyle = fill;
  g.beginPath();
  g.moveTo(cx - 11 * k, cy - 17 * k);
  g.lineTo(cx, cy - 17 * k);
  g.arc(cx, cy, 17 * k, -Math.PI / 2, Math.PI / 2);
  g.lineTo(cx - 11 * k, cy + 17 * k);
  g.closePath();
  g.fill();
}

/** Quanto do deck voce ja tem: tres cartas em leque e o anel de progresso (47 de 60). */
function colecao(g: Ctx): void {
  g.lineWidth = 3;
  g.strokeStyle = CARD.gold;
  circle(g, CX, CY, 122);
  g.stroke();
  g.lineWidth = 12;
  g.strokeStyle = CARD.brand;
  g.beginPath();
  g.arc(CX, CY, 122, -Math.PI / 2, -Math.PI / 2 + TAU * (47 / 60));
  g.stroke();
  g.lineWidth = 2;
  g.strokeStyle = CARD.gold;
  for (let k = 0; k < 60; k += 5) {
    const a = -Math.PI / 2 + (k / 60) * TAU;
    line(g, [
      [CX + Math.cos(a) * 134, CY + Math.sin(a) * 134],
      [CX + Math.cos(a) * 142, CY + Math.sin(a) * 142],
    ]);
    g.stroke();
  }
  for (const [dx, a] of [
    [-34, -0.28],
    [0, 0],
    [34, 0.28],
  ] as const) {
    g.fillStyle = CARD.face;
    rotRect(g, CX + dx, CY + 6, 78, 108, a);
    g.fill();
    g.lineWidth = 3;
    g.strokeStyle = CARD.gold;
    g.stroke();
  }
  deckloomMark(g, CX + 34 * 0, CY + 6, 22, CARD.gold, CARD.brand);
}

/** O meta dos torneios: coroa de louros, estrela e fita. */
function torneio(g: Ctx): void {
  g.strokeStyle = CARD.gold;
  g.fillStyle = CARD.brand;
  for (const side of [-1, 1]) {
    g.lineWidth = 4;
    g.beginPath();
    g.arc(
      CX,
      CY,
      108,
      side < 0 ? Math.PI * 0.6 : Math.PI * 0.4,
      side < 0 ? Math.PI * 1.35 : -Math.PI * 0.35,
      side > 0,
    );
    g.stroke();
    for (let k = 0; k < 7; k++) {
      const a = side < 0 ? Math.PI * (0.62 + k * 0.1) : Math.PI * (0.38 - k * 0.1);
      const x = CX + Math.cos(a) * 108;
      const y = CY + Math.sin(a) * 108;
      g.beginPath();
      g.ellipse(x, y, 20, 8, a + (side < 0 ? 0.9 : -0.9), 0, TAU);
      g.fill();
      g.lineWidth = 2;
      g.stroke();
    }
  }
  const star: Array<[number, number]> = [];
  for (let k = 0; k < 10; k++) {
    const a = -Math.PI / 2 + (k / 10) * TAU;
    const r = k % 2 ? 26 : 62;
    star.push([CX + Math.cos(a) * r, CY - 8 + Math.sin(a) * r]);
  }
  g.fillStyle = CARD.goldSoft;
  line(g, star, true);
  g.fill();
  g.lineWidth = 3;
  g.strokeStyle = CARD.gold;
  g.stroke();
  g.fillStyle = CARD.brand;
  line(
    g,
    [
      [CX - 70, CY + 96],
      [CX + 70, CY + 96],
      [CX + 56, CY + 112],
      [CX + 70, CY + 128],
      [CX - 70, CY + 128],
      [CX - 56, CY + 112],
    ],
    true,
  );
  g.fill();
  g.stroke();
}

/** Compre so o que falta: balanca com moedas num prato e uma carta no outro. */
function lojas(g: Ctx): void {
  g.strokeStyle = CARD.gold;
  g.lineWidth = 6;
  line(g, [
    [CX, CY - 104],
    [CX, CY + 98],
  ]);
  g.stroke();
  g.fillStyle = CARD.brand;
  line(
    g,
    [
      [CX - 54, CY + 112],
      [CX + 54, CY + 112],
      [CX + 34, CY + 96],
      [CX - 34, CY + 96],
    ],
    true,
  );
  g.fill();
  g.stroke();
  circle(g, CX, CY - 110, 10);
  g.fillStyle = CARD.goldSoft;
  g.fill();
  g.lineWidth = 5;
  line(g, [
    [CX - 120, CY - 80],
    [CX + 120, CY - 80],
  ]);
  g.stroke();
  for (const side of [-1, 1]) {
    const px = CX + side * 120;
    g.lineWidth = 2;
    line(g, [
      [px, CY - 80],
      [px - 46, CY + 6],
    ]);
    g.stroke();
    line(g, [
      [px, CY - 80],
      [px + 46, CY + 6],
    ]);
    g.stroke();
    g.lineWidth = 4;
    g.fillStyle = CARD.band;
    g.beginPath();
    g.moveTo(px - 54, CY + 6);
    g.lineTo(px + 54, CY + 6);
    g.arc(px, CY + 6, 54, 0, Math.PI);
    g.closePath();
    g.fill();
    g.stroke();
  }
  // moedas empilhadas no prato da esquerda
  g.fillStyle = CARD.goldSoft;
  for (let k = 0; k < 4; k++) {
    g.beginPath();
    g.ellipse(CX - 120 + (k % 2) * 8 - 4, CY - 2 - k * 9, 24, 7, 0, 0, TAU);
    g.fill();
    g.lineWidth = 2;
    g.stroke();
  }
  // uma carta em pe no prato da direita
  g.fillStyle = CARD.face;
  rotRect(g, CX + 120, CY - 30, 44, 62, 0.12);
  g.fill();
  g.lineWidth = 3;
  g.stroke();
}

/** IA que explica o porque: o olho oracular com raios e quatro runas em volta. */
function oraculo(g: Ctx): void {
  g.strokeStyle = CARD.gold;
  g.lineWidth = 3;
  for (let k = 0; k < 16; k++) {
    const a = (k / 16) * TAU;
    const r0 = k % 2 ? 112 : 104;
    line(g, [
      [CX + Math.cos(a) * r0, CY + Math.sin(a) * r0],
      [CX + Math.cos(a) * 136, CY + Math.sin(a) * 136],
    ]);
    g.stroke();
  }
  g.fillStyle = CARD.face;
  g.lineWidth = 5;
  g.beginPath();
  g.moveTo(CX - 112, CY);
  g.quadraticCurveTo(CX, CY - 96, CX + 112, CY);
  g.quadraticCurveTo(CX, CY + 96, CX - 112, CY);
  g.closePath();
  g.fill();
  g.stroke();
  circle(g, CX, CY, 40);
  g.fillStyle = CARD.brand;
  g.fill();
  g.lineWidth = 3;
  g.stroke();
  circle(g, CX, CY, 15);
  g.fillStyle = CARD.ink;
  g.fill();
  circle(g, CX + 12, CY - 13, 6);
  g.fillStyle = CARD.face;
  g.fill();
  // quatro runas nos cantos da janela (losango com traco)
  g.lineWidth = 3;
  for (const [dx, dy] of [
    [-170, -100],
    [170, -100],
    [-170, 100],
    [170, 100],
  ] as const) {
    const x = CX + dx;
    const y = CY + dy;
    line(
      g,
      [
        [x, y - 18],
        [x + 12, y],
        [x, y + 18],
        [x - 12, y],
      ],
      true,
    );
    g.stroke();
    line(g, [
      [x, y - 18],
      [x, y + 18],
    ]);
    g.stroke();
  }
}

const ARTS: Record<(typeof HOOK_ARTS)[number], (g: Ctx) => void> = {
  colecao,
  torneio,
  lojas,
  oraculo,
};

/** Desenha a arte do gancho `index` (0 a 3) na janela de arte da frente. */
export function drawHookArt(g: Ctx, index: number): void {
  g.save();
  ARTS[HOOK_ARTS[index % HOOK_ARTS.length]](g);
  g.restore();
}

/** O verso: violeta com treliça de losangos, molduras de ouro e o selo de runas com o simbolo no centro. */
export function drawCardBack(g: Ctx): void {
  const W = 512;
  const H = 716;
  const bg = g.createLinearGradient(0, 0, W, H);
  bg.addColorStop(0, CARD.backFrom);
  bg.addColorStop(1, CARD.backTo);
  g.fillStyle = bg;
  g.fillRect(0, 0, W, H);
  // trelica de losangos
  g.strokeStyle = "rgba(224,184,102,.12)";
  g.lineWidth = 1.5;
  for (let y = 60; y <= H - 60; y += 36) {
    for (let x = 60 + ((y / 36) % 2) * 18; x <= W - 60; x += 36) {
      line(
        g,
        [
          [x, y - 9],
          [x + 9, y],
          [x, y + 9],
          [x - 9, y],
        ],
        true,
      );
      g.stroke();
    }
  }
  const cx = W / 2;
  const cy = 358;
  const glow = g.createRadialGradient(cx, cy, 10, cx, cy, 240);
  glow.addColorStop(0, "rgba(224,184,102,.30)");
  glow.addColorStop(1, "rgba(224,184,102,0)");
  g.fillStyle = glow;
  g.fillRect(0, 0, W, H);
  // molduras
  g.strokeStyle = CARD.gold;
  g.lineWidth = 10;
  line(
    g,
    [
      [6, 6],
      [W - 6, 6],
      [W - 6, H - 6],
      [6, H - 6],
    ],
    true,
  );
  g.stroke();
  g.strokeStyle = CARD.goldSoft;
  g.lineWidth = 3;
  line(
    g,
    [
      [36, 36],
      [W - 36, 36],
      [W - 36, H - 36],
      [36, H - 36],
    ],
    true,
  );
  g.stroke();
  // selo: dois aneis, hexagrama, doze selos pequenos e o simbolo
  g.lineWidth = 4;
  circle(g, cx, cy, 172);
  g.stroke();
  g.lineWidth = 2;
  circle(g, cx, cy, 138);
  g.stroke();
  for (let a = 0; a < 2; a++) {
    const tri: Array<[number, number]> = [0, 1, 2].map((k) => {
      const t = -Math.PI / 2 + a * Math.PI + (k / 3) * TAU;
      return [cx + Math.cos(t) * 138, cy + Math.sin(t) * 138];
    });
    line(g, tri, true);
    g.stroke();
  }
  for (let k = 0; k < 12; k++) {
    const t = (k / 12) * TAU;
    const x = cx + Math.cos(t) * 155;
    const y = cy + Math.sin(t) * 155;
    g.fillStyle = CARD.backTo;
    circle(g, x, y, 13);
    g.fill();
    g.lineWidth = 2;
    g.stroke();
    line(g, [
      [x, y - 7],
      [x, y + 7],
    ]);
    g.stroke();
  }
  // cantos: um losango em cada
  for (const [x, y] of [
    [60, 60],
    [W - 60, 60],
    [60, H - 60],
    [W - 60, H - 60],
  ] as const) {
    g.fillStyle = CARD.goldSoft;
    line(
      g,
      [
        [x, y - 12],
        [x + 12, y],
        [x, y + 12],
        [x - 12, y],
      ],
      true,
    );
    g.fill();
  }
  g.fillStyle = CARD.backTo;
  circle(g, cx, cy, 70);
  g.fill();
  deckloomMark(g, cx, cy, 62, CARD.goldSoft, CARD.goldSoft);
}
