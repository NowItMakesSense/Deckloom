// T-L02 / AC-L02d: a decisao de montar o palco e uma funcao pura (docs/design-system.md 1.12):
// so com WebGL e sem prefers-reduced-motion; nos outros casos, o fallback estatico.
import { describe, expect, it, vi } from "vitest";

import { detectCapabilities, hasWebGL, isNarrowViewport, shouldRenderStage } from "./capabilities";

function canvasWith(getContext: (kind: string) => unknown): () => HTMLCanvasElement {
  return () => ({ getContext }) as unknown as HTMLCanvasElement;
}

describe("shouldRenderStage", () => {
  it("monta o palco so com WebGL e sem reducao de movimento", () => {
    expect(shouldRenderStage({ webgl: true, reducedMotion: false })).toBe(true);
  });

  it.each([
    { webgl: false, reducedMotion: false },
    { webgl: true, reducedMotion: true },
    { webgl: false, reducedMotion: true },
  ])("mostra o fallback estatico em %o", (capabilities) => {
    expect(shouldRenderStage(capabilities)).toBe(false);
  });
});

describe("hasWebGL", () => {
  it("e verdadeiro quando o canvas entrega um contexto webgl2", () => {
    expect(hasWebGL(canvasWith((kind) => (kind === "webgl2" ? {} : null)))).toBe(true);
  });

  it("cai para webgl quando webgl2 nao existe", () => {
    expect(hasWebGL(canvasWith((kind) => (kind === "webgl" ? {} : null)))).toBe(true);
  });

  it("e falso quando nenhum contexto vem", () => {
    expect(hasWebGL(canvasWith(() => null))).toBe(false);
  });

  it("e falso quando criar o contexto lanca (driver bloqueado, modo de economia)", () => {
    expect(
      hasWebGL(
        canvasWith(() => {
          throw new Error("bloqueado");
        }),
      ),
    ).toBe(false);
  });

  it("e falso quando nem o canvas pode ser criado", () => {
    expect(
      hasWebGL(() => {
        throw new Error("sem DOM");
      }),
    ).toBe(false);
  });

  it("devolve o contexto ao navegador depois do teste (nao deixa contexto aberto)", () => {
    const lose = vi.fn();
    const context = {
      getExtension: (name: string) =>
        name === "WEBGL_lose_context" ? { loseContext: lose } : null,
    };

    expect(hasWebGL(canvasWith(() => context))).toBe(true);
    expect(lose).toHaveBeenCalledTimes(1);
  });
});

describe("detectCapabilities", () => {
  it("junta WebGL e reducao de movimento", () => {
    const capabilities = detectCapabilities({
      createCanvas: canvasWith(() => ({})),
      matchMedia: (query: string) => ({ matches: query.includes("reduce") }) as MediaQueryList,
    });

    expect(capabilities).toEqual({ webgl: true, reducedMotion: true });
  });

  it("sem matchMedia assume movimento reduzido (conteudo parado e visivel)", () => {
    const capabilities = detectCapabilities({
      createCanvas: canvasWith(() => ({})),
      matchMedia: undefined,
    });

    expect(capabilities.reducedMotion).toBe(true);
    expect(shouldRenderStage(capabilities)).toBe(false);
  });
});

describe("isNarrowViewport", () => {
  it("celular e a janela com menos de 760 px", () => {
    expect(isNarrowViewport(375, 812)).toBe(true);
    expect(isNarrowViewport(759, 500)).toBe(true);
    expect(isNarrowViewport(760, 500)).toBe(false);
  });

  it("tablet em pe (mais alto que largo, ate 1099 px) usa a composicao de celular; deitado, nao", () => {
    expect(isNarrowViewport(768, 1024)).toBe(true);
    expect(isNarrowViewport(1024, 768)).toBe(false);
    expect(isNarrowViewport(1100, 1400)).toBe(false);
  });
});
