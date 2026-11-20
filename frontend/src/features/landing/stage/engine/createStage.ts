// O palco WebGL unico (REQ-L10): renderer, laco de quadros, progresso do scroll, profundidade de campo e
// liberacao de recursos. Carregado sob demanda (import dinamico em Stage.tsx), fora do bundle inicial.
import {
  Raycaster,
  NoToneMapping,
  PCFShadowMap,
  Vector3,
  WebGLRenderer,
  type Object3D,
  type WebGLRenderTarget,
} from "three";

import { fovFor } from "../camera";
import { clamp, lerp } from "../math";
import { END } from "../phases";
import { createDepthOfField } from "./dof";
import { disposeObject3D } from "./dispose";
import { buildWorld, type World, type WorldOptions } from "./world";

/** O que o palco usa do renderer: o WebGLRenderer real ou um falso nos testes. */
export interface RendererLike {
  readonly domElement: HTMLCanvasElement;
  readonly shadowMap: { enabled: boolean; type: number };
  toneMapping: number;
  setPixelRatio(ratio: number): void;
  getPixelRatio(): number;
  setSize(width: number, height: number, updateStyle?: boolean): void;
  setRenderTarget(target: WebGLRenderTarget | null): void;
  render(scene: Object3D, camera: Object3D): void;
  dispose(): void;
}

export interface StageOptions extends WorldOptions {
  /** Elemento preso (sticky) que recebe o canvas. */
  readonly container: HTMLElement;
  /** Secao alta cujo scroll guia a camera de 0 a END (o fim e o mural de avisos). */
  readonly scroller: HTMLElement;
  /** A cada quadro desenhado: atualiza a camada HTML (legendas, dica, barras). */
  readonly onFrame?: (raw: number, t: number) => void;
  readonly createRenderer?: () => RendererLike;
}

export interface AuditResult {
  readonly maxDp: number;
  readonly atDp: number;
  readonly maxAng: number;
  readonly atAng: number;
  readonly pops: string[];
}

export interface StageHandle {
  /** Desenha o quadro do progresso `raw` agora (testes e auditoria). */
  drawAt(raw: number, t?: number): void;
  audit(steps: number): AuditResult;
  dispose(): void;
}

/** Teto do devicePixelRatio do palco: acima disso o custo cresce e o ganho visual some. */
export const STAGE_PIXEL_RATIO = 1.5;
/** Taxa do amortecimento do scroll (1/s): o mesmo seguimento a 30, 60 ou 120 quadros por segundo (12% a 60 fps). */
export const FOLLOW_RATE = -Math.log(1 - 0.12) * 60;

/** Fracao do caminho ate o alvo percorrida em `dt` segundos (amortecimento exponencial, independente da taxa). */
export function followStep(dt: number): number {
  return 1 - Math.exp(-FOLLOW_RATE * Math.min(0.1, Math.max(0, dt)));
}

function defaultRenderer(): RendererLike {
  return new WebGLRenderer({
    antialias: false,
    powerPreference: "high-performance",
  }) as unknown as RendererLike;
}

export function createStage(options: StageOptions): StageHandle {
  const { container, scroller } = options;
  const renderer = (options.createRenderer ?? defaultRenderer)();
  renderer.setPixelRatio(Math.min(globalThis.devicePixelRatio || 1, STAGE_PIXEL_RATIO));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = PCFShadowMap;
  // sem tonemapping: as cores da paleta saem exatas (o ACES lavava as silhuetas)
  renderer.toneMapping = NoToneMapping;
  renderer.domElement.setAttribute("aria-hidden", "true");
  container.prepend(renderer.domElement);

  const world: World = buildWorld(options);
  const dof = createDepthOfField();

  function resize(): void {
    const w = container.clientWidth || 1;
    const h = container.clientHeight || 1;
    renderer.setSize(w, h, false);
    world.camera.aspect = w / h;
    world.camera.fov = fovFor(w / h);
    world.camera.updateProjectionMatrix();
    dof.setSize(w, h, renderer.getPixelRatio());
  }

  function draw(raw: number, t: number): void {
    const frame = world.update(raw, t);
    dof.setFocus(frame.focus, frame.aperture);
    renderer.setRenderTarget(dof.target);
    renderer.render(world.scene, world.camera);
    renderer.setRenderTarget(null);
    renderer.render(dof.scene, dof.camera);
    options.onFrame?.(raw, t);
  }

  function progress(): number {
    const r = scroller.getBoundingClientRect();
    const span = r.height - globalThis.innerHeight;
    return span > 0 ? clamp(-r.top / span, 0, 1) * END : 0;
  }

  const debug = { snap: false, paused: false };
  let shown = -1;
  let lastNow = -1;
  let frameId = 0;
  const frame = (now: number): void => {
    const prevNow = lastNow;
    lastNow = now;
    const r = scroller.getBoundingClientRect();
    if (!debug.paused && r.top < globalThis.innerHeight && r.bottom > 0) {
      const target = progress();
      const dt = prevNow < 0 ? 1 / 60 : (now - prevNow) / 1000;
      let next = shown < 0 || debug.snap ? target : lerp(shown, target, followStep(dt));
      if (Math.abs(next - target) < 0.0002) next = target;
      shown = next;
      draw(shown, now / 1000);
    }
    frameId = requestAnimationFrame(frame);
  };

  /** Auditoria de continuidade: amostra a camera em `steps` passos e acusa saltos e objetos que surgem no quadro. */
  function audit(steps: number): AuditResult {
    const raycaster = new Raycaster();
    const dir = new Vector3();
    const toObj = new Vector3();
    const projected = new Vector3();
    const objects: Array<[string, Object3D]> = [
      ...world.cards.map((m, i) => [`carta${i}`, m] as [string, Object3D]),
      ...world.tokens.map((k, i) => [`token${k.i}`, world.tokenMeshes[i]] as [string, Object3D]),
    ];
    const seen = new Map<string, boolean>();
    const pops: string[] = [];
    let prevPos: Vector3 | null = null;
    let prevDir: Vector3 | null = null;
    let maxDp = 0;
    let maxAng = 0;
    let atDp = 0;
    let atAng = 0;
    for (let s = 0; s <= steps; s++) {
      const raw = (s / steps) * END;
      world.update(raw, 0);
      world.camera.getWorldDirection(dir);
      if (prevPos && prevDir) {
        const dp = world.camera.position.distanceTo(prevPos);
        const ang = (dir.angleTo(prevDir) * 180) / Math.PI;
        if (dp > maxDp) [maxDp, atDp] = [dp, raw];
        if (ang > maxAng) [maxAng, atAng] = [ang, raw];
      }
      prevPos = world.camera.position.clone();
      prevDir = dir.clone();
      for (const [name, obj] of objects) {
        const visible = obj.visible;
        const before = seen.get(name);
        if (before !== undefined && before !== visible) {
          projected.copy(obj.position).project(world.camera);
          toObj.copy(obj.position).sub(world.camera.position);
          const distance = toObj.length();
          const inFront = toObj.dot(dir) > 0;
          raycaster.set(world.camera.position, toObj.normalize());
          const hit = raycaster.intersectObjects(world.occluders, false);
          const hidden = hit.length > 0 && hit[0].distance < distance - 0.5;
          if (inFront && !hidden && Math.abs(projected.x) < 1.05 && Math.abs(projected.y) < 1.05) {
            pops.push(`${name} ${visible ? "aparece" : "some"} em raw ${raw.toFixed(4)}`);
          }
        }
        seen.set(name, visible);
      }
    }
    return { maxDp, atDp, maxAng, atAng, pops };
  }

  globalThis.addEventListener("resize", resize);
  resize();
  // o primeiro quadro sai ja: o cartao so cede o lugar ao canvas com a cena desenhada
  shown = progress();
  draw(shown, 0);
  frameId = requestAnimationFrame(frame);

  // ganchos de depuracao e do E2E (auditoria de continuidade): so fora de producao
  const debugGlobal = globalThis as unknown as { __deckloom?: unknown };
  if (import.meta.env.MODE !== "production") {
    debugGlobal.__deckloom = {
      ready: true,
      audit,
      draw,
      get snap() {
        return debug.snap;
      },
      set snap(v: boolean) {
        debug.snap = v;
      },
      /** Liga ou desliga so as faixas de nevoa (medicao com e sem). */
      mist: (on: boolean) => world.setMist(on),
      /** Pausa o laco: o quadro desenhado por `draw(raw, t)` fica parado para a captura. */
      get paused() {
        return debug.paused;
      },
      set paused(v: boolean) {
        debug.paused = v;
      },
    };
  }

  return {
    drawAt: (raw, t = 0) => draw(raw, t),
    audit,
    dispose() {
      cancelAnimationFrame(frameId);
      globalThis.removeEventListener("resize", resize);
      disposeObject3D(world.scene);
      dof.dispose();
      renderer.dispose();
      renderer.domElement.remove();
      if (debugGlobal.__deckloom) delete debugGlobal.__deckloom;
    },
  };
}
