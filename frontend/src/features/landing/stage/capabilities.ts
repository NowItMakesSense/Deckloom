// Decisoes puras do palco (REQ-L10, docs/design-system.md 1.12). Este modulo fica no bundle inicial e nao
// importa three: so decide se o palco WebGL vale a pena.

export interface StageCapabilities {
  readonly webgl: boolean;
  readonly reducedMotion: boolean;
}

export interface CapabilityEnv {
  readonly createCanvas?: () => HTMLCanvasElement;
  /** `undefined` explicito significa "navegador sem matchMedia". */
  readonly matchMedia?: ((query: string) => MediaQueryList) | undefined;
}

export const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)";
/** Largura abaixo da qual a landing usa a composicao de celular. */
export const MOBILE_MAX_WIDTH = 760;
/** Em janela em pe (tablet), a composicao de celular vale ate esta largura. */
export const PORTRAIT_MAX_WIDTH = 1100;

/** O palco so entra com WebGL e sem prefers-reduced-motion. */
export function shouldRenderStage(capabilities: StageCapabilities): boolean {
  return capabilities.webgl && !capabilities.reducedMotion;
}

function defaultCreateCanvas(): HTMLCanvasElement {
  return document.createElement("canvas");
}

/** Cria um contexto de teste e o devolve ao navegador: o teste nao deixa contexto aberto. */
export function hasWebGL(createCanvas: () => HTMLCanvasElement = defaultCreateCanvas): boolean {
  try {
    const canvas = createCanvas();
    const context = (canvas.getContext("webgl2") ??
      canvas.getContext("webgl")) as WebGLRenderingContext | null;
    if (!context) return false;
    if (typeof context.getExtension === "function") {
      context.getExtension("WEBGL_lose_context")?.loseContext();
    }
    return true;
  } catch {
    return false;
  }
}

function browserMatchMedia(): ((query: string) => MediaQueryList) | undefined {
  return typeof globalThis.matchMedia === "function"
    ? globalThis.matchMedia.bind(globalThis)
    : undefined;
}

export function detectCapabilities(env: CapabilityEnv = {}): StageCapabilities {
  const matchMedia = "matchMedia" in env ? env.matchMedia : browserMatchMedia();
  return {
    webgl: hasWebGL(env.createCanvas),
    // Sem matchMedia nao da para saber: o conteudo fica parado e visivel (mesma regra de motion/index.ts).
    reducedMotion: matchMedia ? matchMedia(REDUCED_MOTION_QUERY).matches : true,
  };
}

/** Celular, ou tablet em pe: a legenda vai para baixo e a camera usa a pose de celular. */
export function isNarrowViewport(width: number, height: number): boolean {
  return width < MOBILE_MAX_WIDTH || (height > width && width < PORTRAIT_MAX_WIDTH);
}
