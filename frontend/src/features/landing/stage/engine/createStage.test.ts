// T-L02 / AC-L02d, AC-L02h (REQ-L10, REQ-L12): o palco monta a cena com um renderer falso (sem WebGL no jsdom),
// desenha quadros e libera geometrias, materiais, texturas e renderer ao desmontar; sem tonemapping.
import { BufferGeometry, Material, NoToneMapping, Texture } from "three";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { fakeModels } from "./testModels";
import { END, PH } from "../phases";
import { createStage, type RendererLike } from "./createStage";

function fakeContext(): CanvasRenderingContext2D {
  const proxy: unknown = new Proxy(function noop() {}, {
    get: (_target, key) =>
      typeof key === "symbol" ? undefined : key === "measureText" ? () => ({ width: 10 }) : proxy,
    set: () => true,
    apply: () => proxy,
  });
  return proxy as CanvasRenderingContext2D;
}

function fakeRenderer() {
  const calls = { render: 0, dispose: 0, size: 0 };
  const renderer: RendererLike = {
    domElement: document.createElement("canvas"),
    shadowMap: { enabled: false, type: 0 },
    toneMapping: -1,
    setPixelRatio: vi.fn(),
    getPixelRatio: () => 1,
    setSize: () => void calls.size++,
    setRenderTarget: vi.fn(),
    render: () => void calls.render++,
    dispose: () => void calls.dispose++,
  };
  return { renderer, calls };
}

const logo = { naturalWidth: 200, naturalHeight: 80 } as unknown as HTMLImageElement;
const LOGOS = { magic: logo, pokemon: logo, yugioh: logo, onepiece: logo, lorcana: logo };
const HOOKS = [0, 1, 2, 3].map((i) => ({ title: `Gancho ${i}`, type: "Recurso", text: "Texto" }));

function mount(onFrame?: (raw: number) => void) {
  const container = document.createElement("div");
  const scroller = document.createElement("div");
  document.body.append(scroller, container);
  scroller.getBoundingClientRect = () => ({ top: 0, bottom: 100000, height: 100000 }) as DOMRect;
  const { renderer, calls } = fakeRenderer();
  const handle = createStage({
    container,
    scroller,
    hooks: HOOKS,
    logos: LOGOS,
    models: fakeModels(),
    mobile: false,
    onFrame,
    createRenderer: () => renderer,
  });
  return { container, scroller, renderer, calls, handle };
}

beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(
    () => fakeContext() as never,
  );
  vi.stubGlobal("Path2D", class {});
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  document.body.innerHTML = "";
});

describe("createStage", () => {
  it("configura o renderer sem tonemapping e com o canvas decorativo dentro do palco", () => {
    const { container, renderer, handle } = mount();

    expect(renderer.toneMapping).toBe(NoToneMapping);
    expect(renderer.shadowMap.enabled).toBe(true);
    expect(container.firstElementChild).toBe(renderer.domElement);
    expect(renderer.domElement.getAttribute("aria-hidden")).toBe("true");
    handle.dispose();
  });

  it("desenha um quadro em dois passes (cena para o alvo de profundidade e o passe de foco) e avisa a camada HTML", () => {
    const onFrame = vi.fn();
    const { calls, handle } = mount(onFrame);
    expect(calls.render).toBe(2); // o primeiro quadro sai na montagem

    handle.drawAt(PH.open);

    expect(calls.render).toBe(4);
    expect(onFrame).toHaveBeenCalledWith(PH.open, 0);
    handle.dispose();
  });

  it("desenha todas as fases sem lancar (floresta, caixa, ganchos, mesa, epilogo e pan ate o mural)", () => {
    const { handle } = mount();

    for (let i = 0; i <= 44; i++) expect(() => handle.drawAt((i / 44) * END)).not.toThrow();
    handle.dispose();
  });

  it("o scroll do palco vai de 0 a END: no fim da secao a camera chega ao mural de avisos", () => {
    const onFrame = vi.fn();
    const container = document.createElement("div");
    const scroller = document.createElement("div");
    document.body.append(scroller, container);
    const height = 100000;
    scroller.getBoundingClientRect = () =>
      ({
        top: -(height - globalThis.innerHeight),
        bottom: globalThis.innerHeight,
        height,
      }) as DOMRect;
    const { renderer } = fakeRenderer();
    const handle = createStage({
      container,
      scroller,
      hooks: HOOKS,
      logos: LOGOS,
      models: fakeModels(),
      mobile: false,
      onFrame,
      createRenderer: () => renderer,
    });

    expect(onFrame).toHaveBeenCalledWith(END, 0);
    handle.dispose();
  });

  it("a depuracao (fora de producao) liga e desliga a nevoa e pausa o laco para medir quadros fixos", () => {
    vi.useFakeTimers({ toFake: ["requestAnimationFrame", "cancelAnimationFrame"] });
    const { calls, handle } = mount();
    const hook = (
      globalThis as unknown as {
        __deckloom: { mist: (on: boolean) => void; paused: boolean };
      }
    ).__deckloom;

    expect(() => hook.mist(false)).not.toThrow();
    hook.paused = true;
    const drawn = calls.render;
    vi.advanceTimersToNextFrame();
    expect(calls.render).toBe(drawn);
    hook.paused = false;
    hook.mist(true);
    handle.dispose();
    vi.useRealTimers();
  });

  it("a auditoria de continuidade cobre a linha do tempo inteira, ate o mural", () => {
    const { handle } = mount();
    const result = handle.audit(40);
    expect(result.atDp).toBeLessThanOrEqual(END);
    expect(result.maxDp).toBeGreaterThan(0);
    handle.dispose();
  });

  it("expoe a auditoria de continuidade e a remove ao desmontar", () => {
    const { handle } = mount();
    const hook = (globalThis as unknown as { __deckloom?: { ready: boolean } }).__deckloom;

    expect(hook?.ready).toBe(true);
    const result = handle.audit(60);
    expect(result.maxDp).toBeGreaterThanOrEqual(0);
    expect(Array.isArray(result.pops)).toBe(true);
    handle.dispose();
    expect((globalThis as unknown as { __deckloom?: unknown }).__deckloom).toBeUndefined();
  });

  it("ao desmontar libera geometrias, materiais, texturas e o renderer, e tira o canvas", () => {
    const geometry = vi.spyOn(BufferGeometry.prototype, "dispose");
    const material = vi.spyOn(Material.prototype, "dispose");
    const texture = vi.spyOn(Texture.prototype, "dispose");
    const { renderer, calls, handle } = mount();

    handle.dispose();

    expect(geometry.mock.calls.length).toBeGreaterThan(50);
    expect(material.mock.calls.length).toBeGreaterThan(20);
    expect(texture.mock.calls.length).toBeGreaterThan(10);
    expect(calls.dispose).toBe(1);
    expect(renderer.domElement.isConnected).toBe(false);
  });

  it("segue o scroll: o laco de quadros so para depois de desmontar", () => {
    vi.useFakeTimers({ toFake: ["requestAnimationFrame", "cancelAnimationFrame"] });
    const { calls, handle } = mount();

    vi.advanceTimersToNextFrame();
    const drawn = calls.render;
    handle.dispose();
    vi.advanceTimersToNextFrame();

    expect(drawn).toBeGreaterThanOrEqual(2);
    expect(calls.render).toBe(drawn);
    vi.useRealTimers();
  });
});

describe("seguimento do scroll independente da taxa de quadros", () => {
  it("12% do caminho por quadro a 60 fps; dois quadros a 120 fps andam o mesmo que um a 60", async () => {
    const { followStep } = await import("./createStage");
    expect(followStep(1 / 60)).toBeCloseTo(0.12, 6);
    const two = 1 - (1 - followStep(1 / 120)) ** 2;
    expect(two).toBeCloseTo(followStep(1 / 60), 6);
    expect(followStep(1 / 30)).toBeCloseTo(1 - 0.88 ** 2, 6);
  });
});
