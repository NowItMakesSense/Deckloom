// O mural de avisos dos planos (REQ-L10, REQ-L13) como funcoes puras: onde ele fica, como a camera o enquadra no fim
// do pan e o layout do texto desenhado nos avisos (medido para caber). Sem three, sem DOM: a cena so le os numeros.
import { type Vec3, lerp, smooth } from "./math";

export type NoticeKind = "free" | "paid";

export interface NoteLayout {
  readonly kind: NoticeKind;
  /** Largura e altura do aviso (papel) no quadro. */
  readonly w: number;
  readonly h: number;
  /** Centro do aviso no plano do quadro (x para a direita, y para cima) e giro no plano (rad). */
  readonly x: number;
  readonly y: number;
  readonly rz: number;
}

export interface BoardTorch {
  /** Ponto no poste, no espaco local do mural. */
  readonly local: Vec3;
  /** Giro da tocha (yaw, rad) no mundo. */
  readonly yaw: number;
  readonly seed: number;
}

export interface BoardLayout {
  /** Largura, altura e centro vertical do quadro de madeira. */
  readonly w: number;
  readonly h: number;
  readonly cy: number;
  readonly top: number;
  readonly bottom: number;
  /** Giro do mural em torno de Y (rad) e a normal da frente dele. */
  readonly yaw: number;
  readonly normal: Vec3;
  /** Centro do quadro no mundo (y = cy). */
  readonly center: Vec3;
  /** Largura da placa "Mural de avisos". */
  readonly signW: number;
  readonly signH: number;
  readonly notes: readonly NoteLayout[];
  readonly torches: readonly BoardTorch[];
}

const YAW = 0.9;
const CENTER_XZ: readonly [number, number] = [-14, 46];

/** O mural fica a esquerda do portal, virado para a clareira; no celular, mais estreito e alto (tela em pe). */
export function boardLayout(mobile: boolean): BoardLayout {
  const { w, h, cy } = mobile ? { w: 2.75, h: 3.9, cy: 2.25 } : { w: 4.6, h: 3.1, cy: 2.05 };
  const top = cy + h / 2;
  const bottom = cy - h / 2;
  const notes: NoteLayout[] = mobile
    ? [
        { kind: "free", w: 2.35, h: 1.2, x: -0.02, y: top - 0.55 - 0.6, rz: -0.022 },
        { kind: "paid", w: 2.45, h: 1.75, x: 0.03, y: top - 0.55 - 1.2 - 0.1 - 0.875, rz: 0.016 },
      ]
    : [
        { kind: "free", w: 1.7, h: 1.95, x: -1.18, y: cy - 0.22, rz: -0.034 },
        { kind: "paid", w: 1.95, h: 2.35, x: 1.05, y: cy - 0.22, rz: 0.021 },
      ];
  return {
    w,
    h,
    cy,
    top,
    bottom,
    yaw: YAW,
    normal: [Math.sin(YAW), 0, Math.cos(YAW)],
    center: [CENTER_XZ[0], cy, CENTER_XZ[1]],
    signW: Math.min(w * 0.72, 3.0),
    signH: 0.36,
    notes,
    torches: [-1, 1].map((sd) => ({
      local: [sd * (w / 2 + 0.16), cy + 0.35, 0.12] as Vec3,
      yaw: YAW + sd * 0.55,
      seed: 11 + sd,
    })),
  };
}

/** Do espaco local do mural (x para a direita, y para cima, z para a frente) para o mundo. */
export function boardToWorld(b: BoardLayout, p: Vec3): Vec3 {
  const c = Math.cos(b.yaw);
  const s = Math.sin(b.yaw);
  return [b.center[0] + p[0] * c + p[2] * s, p[1], b.center[2] - p[0] * s + p[2] * c];
}

/** Os cantos do quadro e o topo do telhado, no mundo: o que o enquadramento final precisa mostrar. */
export function boardCorners(b: BoardLayout): Vec3[] {
  const hw = b.w / 2;
  const local: Vec3[] = [
    [-hw, b.bottom, 0],
    [hw, b.bottom, 0],
    [-hw, b.top + ROOF_ABOVE_TOP, 0],
    [hw, b.top + ROOF_ABOVE_TOP, 0],
  ];
  return local.map((p) => boardToWorld(b, p));
}

export interface PosTgt {
  readonly pos: Vec3;
  readonly tgt: Vec3;
}

/** Altura do telhado do mural (noticeboard.glb) acima do topo do quadro: o enquadramento final mostra ate ali. */
export const ROOF_ABOVE_TOP = 1.05;

/** A ultima pose do epilogo: atras do portal de pedra, com a clareira emoldurada pelo arco. */
export const EPILOGUE_END: PosTgt = { pos: [0.2, 2.6, 57], tgt: [0, 3.4, 0] };

/**
 * Enquadramento final: do pe do quadro ate o telhado, livre da ilha de navegacao (em cima) e dos botoes em HTML
 * (embaixo). Depende da lente (fov vertical, graus) e do formato da tela.
 */
export function boardView(b: BoardLayout, fov: number, aspect: number, mobile: boolean): PosTgt {
  const hv = Math.tan((fov * Math.PI) / 360);
  const hh = hv * aspect;
  const d = Math.max(
    (b.w + (mobile ? 0.15 : 0.45)) / (mobile ? 0.98 : 0.96) / 2 / hh,
    (b.h + ROOF_ABOVE_TOP + 0.3) / 0.8 / 2 / hv,
  );
  const visH = 2 * d * hv;
  const yc = b.center[1] + 0.3 + (ROOF_ABOVE_TOP - 0.66) / 2 - visH * 0.02;
  return {
    pos: [b.center[0] + b.normal[0] * d, yc + 0.25, b.center[2] + b.normal[2] * d],
    tgt: [b.center[0], yc, b.center[2]],
  };
}

/**
 * O pan final (u de 0 a 1): uma curva continua (Bezier quadratica) que desliza para a esquerda e se aproxima do mural,
 * girando a direcao do olhar junto, sem parada no meio.
 */
export function panCam(
  u: number,
  b: BoardLayout,
  fov: number,
  aspect: number,
  mobile: boolean,
): PosTgt {
  const s = smooth(u);
  const fin = boardView(b, fov, aspect, mobile);
  const EP = EPILOGUE_END.pos;
  const ET = EPILOGUE_END.tgt;
  const P1: Vec3 = [lerp(EP[0], fin.pos[0], 0.6), 2.8, lerp(EP[2], fin.pos[2], 0.3)];
  const a = (1 - s) * (1 - s);
  const m = 2 * s * (1 - s);
  const c = s * s;
  const pos: Vec3 = [0, 1, 2].map((j) => a * EP[j] + m * P1[j] + c * fin.pos[j]) as unknown as Vec3;
  const d0 = [ET[0] - EP[0], ET[1] - EP[1], ET[2] - EP[2]];
  const d1 = [fin.tgt[0] - fin.pos[0], fin.tgt[1] - fin.pos[1], fin.tgt[2] - fin.pos[2]];
  const l0 = Math.hypot(d0[0], d0[1], d0[2]);
  const l1 = Math.hypot(d1[0], d1[1], d1[2]);
  const dir = [0, 1, 2].map((j) => lerp(d0[j] / l0, d1[j] / l1, s));
  const dl = Math.hypot(dir[0], dir[1], dir[2]);
  const L = lerp(l0, l1, s);
  return {
    pos,
    tgt: [pos[0] + (dir[0] / dl) * L, pos[1] + (dir[1] / dl) * L, pos[2] + (dir[2] / dl) * L],
  };
}

// ---------------------------------------------------------------------------------------------------------------
// Texto dos avisos: o que vai escrito e onde, medido para caber (a textura so executa estas operacoes).
// ---------------------------------------------------------------------------------------------------------------

export interface NoticeCopy {
  readonly label: string;
  readonly title: string;
  readonly price: string;
  readonly per: string;
  readonly feats: readonly string[];
  readonly foot: string;
}

/** Os planos do REQ-L13, como aparecem escritos no mural. */
export const NOTICE_COPY: Readonly<Record<NoticeKind, NoticeCopy>> = {
  free: {
    label: "PLANO GRATUITO",
    title: "Grátis",
    price: "R$ 0",
    per: "para sempre",
    feats: ["Até 5 decks salvos", "Monte com a sua coleção"],
    foot: "Crie sua conta grátis",
  },
  paid: {
    label: "PLANO PAGO",
    title: "Arcano",
    price: "R$ 19,90",
    per: "por mês",
    feats: ["Sugestões com IA", "Decks ilimitados", "Alertas de preço", "Sem anúncios"],
    foot: "Comece pelo cadastro grátis",
  },
};

/** Largura do texto numa fonte CSS (no navegador, o measureText do canvas). */
export type Measure = (font: string, text: string) => number;

export type NoticeInk = "ink" | "red" | "gold";

export type NoticeOp =
  | {
      readonly op: "text";
      readonly text: string;
      readonly x: number;
      readonly y: number;
      readonly font: string;
      readonly align: "left" | "center" | "right";
      readonly color: NoticeInk;
    }
  | {
      /** Losango vermelho em (x, y - size * 0,32) e o texto em x + 30. */
      readonly op: "bullet";
      readonly bullet: true;
      readonly text: string;
      readonly x: number;
      readonly y: number;
      readonly size: number;
      readonly font: string;
      readonly align: "left";
      readonly color: NoticeInk;
    }
  | { readonly op: "rule"; readonly y: number; readonly x0: number; readonly x1: number }
  | { readonly op: "seal"; readonly x: number; readonly y: number; readonly r: number };

export const TITLE_FAMILY = '"Cinzel Decorative", Georgia, serif';
export const TEXT_FAMILY = '"Albert Sans", system-ui, sans-serif';

const font = (weight: string, size: number, family: string) => `${weight} ${size}px ${family}`;

/** O maior tamanho (ate `size`) em que o texto cabe em `maxW`. */
export function fitFontSize(
  measure: Measure,
  weight: string,
  size: number,
  family: string,
  text: string,
  maxW: number,
): number {
  const w = measure(font(weight, size, family), text);
  return w > maxW ? Math.floor((size * maxW) / w) : size;
}

/**
 * As operacoes de desenho de um aviso numa textura W x H. Em pe (desktop): label, titulo, preco e beneficios
 * centralizados. Deitado (celular): titulo e preco na mesma linha e os beneficios em uma ou duas colunas.
 */
export function noticeLayout(
  kind: NoticeKind,
  W: number,
  H: number,
  land: boolean,
  measure: Measure,
): NoticeOp[] {
  const free = kind === "free";
  const c = NOTICE_COPY[kind];
  const ops: NoticeOp[] = [];
  const text = (
    t: string,
    x: number,
    y: number,
    f: string,
    align: "left" | "center" | "right",
    color: NoticeInk,
  ) => ops.push({ op: "text", text: t, x, y, font: f, align, color });
  const bullets = (x: number, colW: number, cols: number, y0: number, dy: number, max: number) => {
    let size = max;
    for (const f of c.feats)
      size = Math.min(size, fitFontSize(measure, "600", max, TEXT_FAMILY, f, colW - 40));
    c.feats.forEach((f, i) => {
      const col = i % cols;
      const row = Math.floor(i / cols);
      ops.push({
        op: "bullet",
        bullet: true,
        text: f,
        x: x + (col * (W - 120)) / cols,
        y: y0 + row * dy,
        size,
        font: font("600", size, TEXT_FAMILY),
        align: "left",
        color: "ink",
      });
    });
  };

  if (!land) {
    const hair = String.fromCharCode(8202);
    text(c.label.split("").join(hair), W / 2, 120, font("700", 40, TEXT_FAMILY), "center", "gold");
    const ts = fitFontSize(measure, "700", 116, TITLE_FAMILY, c.title, W - 160);
    text(c.title, W / 2, 250, font("700", ts, TITLE_FAMILY), "center", "ink");
    ops.push({ op: "rule", y: 300, x0: 180, x1: W - 180 });
    const ps = fitFontSize(measure, "700", 150, TITLE_FAMILY, c.price, W - 160);
    text(c.price, W / 2, 460, font("700", ps, TITLE_FAMILY), "center", free ? "ink" : "red");
    text(c.per, W / 2, 530, font("500", 46, TEXT_FAMILY), "center", "ink");
    ops.push({ op: "rule", y: 590, x0: 180, x1: W - 180 });
    bullets(140, W - 120 - 20, 1, 700, 96, 62);
    const fs = fitFontSize(measure, "italic 600", 46, TEXT_FAMILY, c.foot, W - 120);
    text(c.foot, W / 2, H - 80, font("italic 600", fs, TEXT_FAMILY), "center", "gold");
    if (!free) ops.push({ op: "seal", x: W - 150, y: H - 230, r: 78 });
    return ops;
  }

  text(c.label, 60, 78, font("700", 34, TEXT_FAMILY), "left", "gold");
  const ts = fitFontSize(measure, "700", 96, TITLE_FAMILY, c.title, 440);
  text(c.title, 60, 178, font("700", ts, TITLE_FAMILY), "left", "ink");
  const ps = fitFontSize(measure, "700", 112, TITLE_FAMILY, c.price, 460);
  text(c.price, W - 60, 180, font("700", ps, TITLE_FAMILY), "right", free ? "ink" : "red");
  text(c.per, W - 60, 228, font("500", 40, TEXT_FAMILY), "right", "ink");
  ops.push({ op: "rule", y: 266, x0: 60, x1: W - 60 });
  const cols = free ? 1 : 2;
  bullets(72, (W - 120) / cols - (cols > 1 ? 30 : 0), cols, free ? 338 : 350, free ? 68 : 78, 58);
  const fs = fitFontSize(measure, "italic 600", 38, TEXT_FAMILY, c.foot, W - 120);
  text(c.foot, W / 2, H - 40, font("italic 600", fs, TEXT_FAMILY), "center", "gold");
  if (!free) ops.push({ op: "seal", x: W - 110, y: H - 130, r: 50 });
  return ops;
}
