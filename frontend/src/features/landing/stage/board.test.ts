// T-L02 / AC-L02j, AC-L02m (REQ-L10, REQ-L13): o mural de avisos dos planos e o pan final ate ele, como funcoes
// puras: posicao do mural ao lado do portal, enquadramento da camera, curva do pan e o layout dos avisos.
import { describe, expect, it } from "vitest";

import {
  EPILOGUE_END,
  NOTICE_COPY,
  boardCorners,
  boardLayout,
  boardView,
  fitFontSize,
  noticeLayout,
  panCam,
  type NoticeOp,
} from "./board";
import type { Vec3 } from "./math";

const VIEWS = [
  { name: "desktop 1366x768", mobile: false, aspect: 1366 / 768, fov: 42 },
  { name: "desktop 1440x900", mobile: false, aspect: 1440 / 900, fov: 42 },
  { name: "celular 375x812", mobile: true, aspect: 375 / 812, fov: 62 },
  { name: "celular 390x844", mobile: true, aspect: 390 / 844, fov: 62 },
  { name: "tablet 768x1024", mobile: true, aspect: 768 / 1024, fov: 62 },
] as const;

/** Projeta um ponto do mundo na tela (NDC -1..1) de uma camera com up = +Y. */
function project(p: Vec3, pos: Vec3, tgt: Vec3, fov: number, aspect: number): [number, number] {
  const f = [tgt[0] - pos[0], tgt[1] - pos[1], tgt[2] - pos[2]];
  const fl = Math.hypot(f[0], f[1], f[2]);
  const fw = [f[0] / fl, f[1] / fl, f[2] / fl];
  const r = [-fw[2], 0, fw[0]];
  const rl = Math.hypot(r[0], r[2]);
  const rt = [r[0] / rl, 0, r[2] / rl];
  const up = [
    rt[1] * fw[2] - rt[2] * fw[1],
    rt[2] * fw[0] - rt[0] * fw[2],
    rt[0] * fw[1] - rt[1] * fw[0],
  ];
  const d = [p[0] - pos[0], p[1] - pos[1], p[2] - pos[2]];
  const z = d[0] * fw[0] + d[1] * fw[1] + d[2] * fw[2];
  const x = d[0] * rt[0] + d[2] * rt[2];
  const y = d[0] * up[0] + d[1] * up[1] + d[2] * up[2];
  const tv = Math.tan((fov * Math.PI) / 360);
  return [x / z / (tv * aspect), y / z / tv];
}

describe("boardLayout: o mural de avisos ao lado do portal", () => {
  it.each([false, true])(
    "fica a esquerda do caminho, perto do portal, virado para a clareira (celular: %s)",
    (mobile) => {
      const b = boardLayout(mobile);
      expect(b.center[0]).toBeLessThan(-8);
      expect(b.center[2]).toBeGreaterThan(40);
      expect(b.center[2]).toBeLessThan(50);
      // a frente do mural olha para a direita e para a camera do epilogo (x e z positivos)
      expect(b.normal[0]).toBeGreaterThan(0);
      expect(b.normal[2]).toBeGreaterThan(0);
      expect(Math.hypot(...b.normal)).toBeCloseTo(1);
    },
  );

  it("no celular o mural e mais estreito e mais alto (tela em pe); no desktop, deitado", () => {
    expect(boardLayout(true).w).toBeLessThan(boardLayout(true).h);
    expect(boardLayout(false).w).toBeGreaterThan(boardLayout(false).h);
  });

  it.each([false, true])(
    "os dois avisos (Gratis e Arcano) cabem no quadro e nao se sobrepoem (celular: %s)",
    (mobile) => {
      const b = boardLayout(mobile);
      expect(b.notes.map((n) => n.kind)).toEqual(["free", "paid"]);
      for (const n of b.notes) {
        expect(Math.abs(n.x) + n.w / 2).toBeLessThanOrEqual(b.w / 2);
        expect(n.y + n.h / 2).toBeLessThanOrEqual(b.top);
        expect(n.y - n.h / 2).toBeGreaterThanOrEqual(b.bottom);
      }
      const [free, paid] = b.notes;
      const apartX = Math.abs(free.x - paid.x) >= (free.w + paid.w) / 2;
      const apartY = Math.abs(free.y - paid.y) >= (free.h + paid.h) / 2;
      expect(apartX || apartY).toBe(true);
      if (mobile)
        expect(paid.y).toBeLessThan(free.y); // empilhados: o Arcano embaixo
      else expect(paid.x).toBeGreaterThan(free.x); // lado a lado: o Arcano a direita
    },
  );

  it("a placa 'Mural de avisos' cabe na largura do quadro", () => {
    for (const mobile of [false, true]) {
      const b = boardLayout(mobile);
      expect(b.signW).toBeLessThanOrEqual(b.w * 0.72 + 1e-9);
    }
  });

  it("duas tochas nos postes, uma de cada lado", () => {
    const b = boardLayout(false);
    expect(b.torches).toHaveLength(2);
    expect(Math.sign(b.torches[0].local[0])).toBe(-Math.sign(b.torches[1].local[0]));
  });
});

describe("boardView: a camera final enquadra o mural inteiro", () => {
  it.each(VIEWS)("$name: todos os cantos do mural (do pe ao telhado) dentro do quadro", (v) => {
    const b = boardLayout(v.mobile);
    const view = boardView(b, v.fov, v.aspect, v.mobile);
    const xs: number[] = [];
    for (const corner of boardCorners(b)) {
      const [x, y] = project(corner, view.pos, view.tgt, v.fov, v.aspect);
      xs.push(x);
      expect(Math.abs(x), `x ${corner.join(",")}`).toBeLessThanOrEqual(1);
      expect(Math.abs(y), `y ${corner.join(",")}`).toBeLessThanOrEqual(1);
    }
    // e o mural ocupa boa parte da largura (quase metade da tela ou mais: nao fica pequeno no meio)
    expect(Math.max(...xs) - Math.min(...xs)).toBeGreaterThan(0.9);
  });

  it("a camera fica diante do mural, na direcao da normal", () => {
    const b = boardLayout(false);
    const view = boardView(b, 42, 16 / 9, false);
    const d = [view.pos[0] - b.center[0], view.pos[2] - b.center[2]];
    const len = Math.hypot(d[0], d[1]);
    expect((d[0] * b.normal[0] + d[1] * b.normal[2]) / len).toBeGreaterThan(0.99);
  });
});

describe("panCam: do portal ate o mural, sem salto", () => {
  const forward = (p: { pos: Vec3; tgt: Vec3 }) => {
    const d = [p.tgt[0] - p.pos[0], p.tgt[1] - p.pos[1], p.tgt[2] - p.pos[2]];
    const l = Math.hypot(d[0], d[1], d[2]);
    return d.map((x) => x / l);
  };

  it.each(VIEWS)(
    "$name: comeca na pose final do epilogo e termina no enquadramento do mural",
    (v) => {
      const b = boardLayout(v.mobile);
      const start = panCam(0, b, v.fov, v.aspect, v.mobile);
      const end = panCam(1, b, v.fov, v.aspect, v.mobile);
      const view = boardView(b, v.fov, v.aspect, v.mobile);
      for (let j = 0; j < 3; j++) {
        expect(start.pos[j]).toBeCloseTo(EPILOGUE_END.pos[j], 6);
        expect(start.tgt[j]).toBeCloseTo(EPILOGUE_END.tgt[j], 6);
        expect(end.pos[j]).toBeCloseTo(view.pos[j], 6);
        expect(end.tgt[j]).toBeCloseTo(view.tgt[j], 6);
      }
    },
  );

  it.each(VIEWS)(
    "$name: desliza para a esquerda de forma continua (passo pequeno, giro pequeno)",
    (v) => {
      const b = boardLayout(v.mobile);
      let prev = panCam(0, b, v.fov, v.aspect, v.mobile);
      let maxMove = 0;
      let maxTurn = 0;
      for (let i = 1; i <= 250; i++) {
        const next = panCam(i / 250, b, v.fov, v.aspect, v.mobile);
        maxMove = Math.max(maxMove, Math.hypot(...next.pos.map((x, j) => x - prev.pos[j])));
        const [fa, fb] = [forward(prev), forward(next)];
        const dot = fa[0] * fb[0] + fa[1] * fb[1] + fa[2] * fb[2];
        maxTurn = Math.max(maxTurn, (Math.acos(Math.min(1, dot)) * 180) / Math.PI);
        expect(next.pos[0]).toBeLessThanOrEqual(prev.pos[0] + 1e-9); // so para a esquerda
        prev = next;
      }
      expect(maxMove).toBeLessThan(0.6);
      expect(maxTurn).toBeLessThan(4);
    },
  );
});

describe("avisos: o texto do mural e medido para caber", () => {
  // medida falsa e deterministica: largura = tamanho da fonte * 0,6 por caractere
  const measure = (font: string, text: string) => {
    const size = Number(/(\d+(?:\.\d+)?)px/.exec(font)?.[1] ?? 0);
    return size * 0.6 * text.length;
  };
  const texts = (ops: NoticeOp[]) => ops.flatMap((o) => ("text" in o ? [o.text] : []));

  it("fitFontSize reduz a fonte so quando o texto passa da largura", () => {
    expect(fitFontSize(measure, "700", 100, "Serif", "abc", 1000)).toBe(100);
    const fitted = fitFontSize(measure, "700", 100, "Serif", "abcdefghij", 300);
    expect(fitted).toBeLessThan(100);
    expect(measure(`700 ${fitted}px Serif`, "abcdefghij")).toBeLessThanOrEqual(300);
  });

  it("os planos do mural sao os do REQ-L13: Gratis ate 5 decks; Arcano R$ 19,90 por mes com IA, ilimitados, alertas e sem anuncios", () => {
    expect(NOTICE_COPY.free.title).toBe("Grátis");
    expect(NOTICE_COPY.free.feats.join(" ")).toMatch(/Até 5 decks/);
    expect(NOTICE_COPY.paid.title).toBe("Arcano");
    expect(NOTICE_COPY.paid.price).toBe("R$ 19,90");
    expect(NOTICE_COPY.paid.per).toBe("por mês");
    expect(NOTICE_COPY.paid.feats).toEqual([
      "Sugestões com IA",
      "Decks ilimitados",
      "Alertas de preço",
      "Sem anúncios",
    ]);
  });

  it.each(["free", "paid"] as const)(
    "aviso %s em pe (desktop): todo texto dentro do papel",
    (kind) => {
      const ops = noticeLayout(kind, 1024, 1300, false, measure);
      expect(texts(ops)).toEqual(
        expect.arrayContaining([NOTICE_COPY[kind].title, NOTICE_COPY[kind].price]),
      );
      for (const op of ops) {
        if (!("text" in op)) continue;
        const w = measure(op.font, op.text) + ("bullet" in op ? 30 : 0);
        const left = op.align === "center" ? op.x - w / 2 : op.align === "right" ? op.x - w : op.x;
        expect(left, op.text).toBeGreaterThanOrEqual(0);
        expect(left + w, op.text).toBeLessThanOrEqual(1024);
        expect(op.y, op.text).toBeLessThanOrEqual(1300);
      }
    },
  );

  it.each(["free", "paid"] as const)(
    "aviso %s deitado (celular): titulo e preco na mesma linha e tudo medido para caber",
    (kind) => {
      const H = kind === "free" ? 523 : 731;
      const ops = noticeLayout(kind, 1024, H, true, measure);
      const title = ops.find((o) => "text" in o && o.text === NOTICE_COPY[kind].title);
      const price = ops.find((o) => "text" in o && o.text === NOTICE_COPY[kind].price);
      expect(title && price).toBeTruthy();
      expect(Math.abs((title as { y: number }).y - (price as { y: number }).y)).toBeLessThan(10);
      for (const op of ops) {
        if (!("text" in op)) continue;
        const w = measure(op.font, op.text) + ("bullet" in op ? 30 : 0);
        const left = op.align === "center" ? op.x - w / 2 : op.align === "right" ? op.x - w : op.x;
        expect(left, op.text).toBeGreaterThanOrEqual(0);
        expect(left + w, op.text).toBeLessThanOrEqual(1024);
        expect(op.y, op.text).toBeLessThanOrEqual(H);
      }
      // o titulo (esquerda) e o preco (direita) nao se encostam
      const tw = measure((title as { font: string }).font, NOTICE_COPY[kind].title);
      const pw = measure((price as { font: string }).font, NOTICE_COPY[kind].price);
      expect(60 + tw).toBeLessThan(1024 - 60 - pw);
    },
  );

  it("so o Arcano leva o selo do Deckloom", () => {
    const has = (kind: "free" | "paid", land: boolean) =>
      noticeLayout(kind, 1024, 1300, land, measure).some((o) => o.op === "seal");
    expect(has("paid", false)).toBe(true);
    expect(has("paid", true)).toBe(true);
    expect(has("free", false)).toBe(false);
  });
});
