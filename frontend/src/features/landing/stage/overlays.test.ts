// T-L02 / AC-L02e (REQ-L10, REQ-L11): a camada HTML do palco (dica, legendas dos ganchos, jogos) como funcao
// pura do progresso: cada legenda aparece quando a carta e revelada e uma por vez.
import { describe, expect, it } from "vitest";

import { applyOverlayFrame, overlayFrame, type OverlayElements } from "./overlays";
import { END, PAN1, PH, SEGL } from "./phases";

describe("overlayFrame", () => {
  it("a dica de rolar aparece no topo e some logo", () => {
    expect(overlayFrame(0).hint.opacity).toBe(1);
    expect(overlayFrame(0.02).hint.opacity).toBe(0);
  });

  it("a frase de abertura do baralho aparece depois da subida das cartas e sai antes dos ganchos", () => {
    expect(overlayFrame(PH.open).intro.opacity).toBe(0);
    expect(overlayFrame(PH.rise + 0.03).intro.opacity).toBeGreaterThan(0.9);
    expect(overlayFrame(PH.intro).intro.opacity).toBe(0);
  });

  it("uma legenda por vez, na ordem, quando a carta e revelada", () => {
    for (let k = 0; k < 4; k++) {
      const raw = PH.story0 + SEGL * (k + 0.7);
      const frame = overlayFrame(raw);
      frame.hooks.forEach((hook, i) => {
        if (i === k) expect(hook.opacity, `gancho ${k}`).toBeGreaterThan(0.9);
        else expect(hook.opacity, `gancho ${i} com ${k} ativo`).toBe(0);
      });
    }
  });

  it("as partes de cada legenda entram escalonadas (titulo, depois texto, depois exemplo)", () => {
    const raw = PH.story0 + SEGL * 0.62;
    const items = overlayFrame(raw).hooks[0].items;
    expect(items.length).toBe(4);
    expect(items[0].opacity).toBeGreaterThanOrEqual(items[1].opacity);
    expect(items[1].opacity).toBeGreaterThanOrEqual(items[3].opacity);
  });

  it("a legenda sobe 18 px ao sair e as barras de progresso preenchem ao longo do segmento", () => {
    const k = 1;
    const frame = overlayFrame(PH.story0 + SEGL * (k + 0.5));
    expect(frame.hooks[k].bar).toBeCloseTo(0.5);
    expect(overlayFrame(PH.story0 + SEGL * (k + 0.95)).hooks[k].items[0].dy).toBeLessThan(0);
    expect(frame.progress.opacity).toBe(1);
    expect(overlayFrame(0).progress.opacity).toBe(0);
  });

  it("os jogos entram na mesa: titulo primeiro e a legenda perto do fim", () => {
    expect(overlayFrame(PH.regroup).gamesTitle.opacity).toBe(0);
    expect(overlayFrame(PH.regroup + 0.06).gamesTitle.opacity).toBe(1);
    expect(overlayFrame(PH.mesa).gamesCap.opacity).toBe(0);
    expect(overlayFrame(0.99).gamesCap.opacity).toBe(1);
  });

  it("o titulo e a legenda dos jogos saem quando a camera deixa o portal; os botoes do mural entram no fim do pan", () => {
    expect(overlayFrame(1.02).gamesTitle.opacity).toBe(0);
    expect(overlayFrame(1.02).gamesCap.opacity).toBe(0);
    expect(overlayFrame(1).boardCta.opacity).toBe(0);
    expect(overlayFrame(PAN1 - 0.03).boardCta.opacity).toBe(0);
    expect(overlayFrame(END).boardCta.opacity).toBe(1);
    expect(overlayFrame(END).boardCta.dy).toBe(0);
    expect(overlayFrame(1.06).boardCta.dy).toBeGreaterThan(0);
  });
});

describe("applyOverlayFrame", () => {
  function elements(): OverlayElements {
    const div = () => document.createElement("div");
    return {
      hint: div(),
      intro: div(),
      progress: div(),
      hooks: [0, 1, 2, 3].map(() => ({ panel: div(), items: [div(), div()], bar: div() })),
      gamesTitle: div(),
      gamesCap: div(),
      boardCta: div(),
    };
  }

  it("escreve opacity e translate nos elementos e bloqueia o foco da legenda invisivel (inert)", () => {
    const els = elements();
    applyOverlayFrame(els, overlayFrame(PH.story0 + SEGL * 0.7));

    expect(Number(els.hooks[0].panel.style.opacity)).toBeGreaterThan(0.9);
    expect(els.hooks[0].panel.hasAttribute("inert")).toBe(false);
    expect(els.hooks[1].panel.hasAttribute("inert")).toBe(true);
    expect(els.hooks[0].bar.style.transform).toContain("scaleX");
    expect(els.hooks[0].items[0].style.translate).toContain("px");
    expect(Number(els.hint.style.opacity)).toBe(0);
  });

  it("os botoes do mural ficam fora do foco e invisiveis (visibility) ate o pan terminar", () => {
    const els = elements();
    applyOverlayFrame(els, overlayFrame(1));
    expect(els.boardCta.style.visibility).toBe("hidden");
    expect(els.boardCta.hasAttribute("inert")).toBe(true);

    applyOverlayFrame(els, overlayFrame(END));
    expect(els.boardCta.style.visibility).toBe("visible");
    expect(els.boardCta.hasAttribute("inert")).toBe(false);
    expect(Number(els.boardCta.style.opacity)).toBe(1);
  });
});
