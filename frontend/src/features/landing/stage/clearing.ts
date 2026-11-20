// Imagem estatica da clareira (fallback sem WebGL ou com prefers-reduced-motion): projecao pura das camadas do
// hero 2,5D vistas da ultima pose da camera. O SVG so desenha o que estas funcoes devolvem.
import type { HeroLayer, Pt, Shapes } from "./silhouettes";
import { CLEARING_Z } from "./silhouettes";

export interface View {
  readonly w: number;
  readonly h: number;
}

/** Quadro 16:9 do SVG (preserveAspectRatio slice cobre qualquer janela). */
export const STATIC_VIEW: View = { w: 1600, h: 900 };

const FOV_HALF = (25 * Math.PI) / 180;

/** Distancia focal em pixels: fov vertical de 50 graus. */
export function focalPx(view: View): number {
  return (view.h * 0.5) / Math.tan(FOV_HALF);
}

interface Cam {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

export interface LayerPlacement {
  readonly tx: number;
  readonly ty: number;
  readonly scale: number;
}

/** Posicao e escala de uma camada (billboard) na tela, ou null se estiver atras da camera ou fora do quadro. */
export function projectLayer(
  layer: Pick<HeroLayer, "z" | "W" | "H">,
  cam: Cam,
  view: View,
): LayerPlacement | null {
  const d = layer.z - cam.z;
  if (d < 0.35) return null;
  const p = focalPx(view) / d;
  const tx = view.w / 2 + (-layer.W - cam.x) * p;
  const ty = view.h * 0.5 + (cam.y - layer.H) * p;
  const ww = 2 * layer.W * p;
  const hh = layer.H * p;
  if (tx > view.w || ty > view.h || tx + ww < 0 || ty + hh < 0) return null;
  return { tx, ty, scale: p };
}

function project(
  cam: Cam,
  view: View,
  x: number,
  y: number,
  z: number,
): [number, number, number] | null {
  const d = z - cam.z;
  if (d < 0.3) return null;
  const p = focalPx(view) / d;
  return [view.w / 2 + (x - cam.x) * p, view.h * 0.5 + (cam.y - y) * p, p];
}

export interface ClearingGround {
  readonly horizonY: number;
  /** Trapezio do caminho de pedra (4 vertices). */
  readonly path: Pt[];
  /** Contorno da clareira. */
  readonly clearing: Pt[];
  /** Juntas das pedras do caminho. */
  readonly stoneLines: Array<readonly [Pt, Pt]>;
}

export function clearingGround(view: View, cam: Cam = { x: 0, y: 1.75, z: 214 }): ClearingGround {
  const clearing: Pt[] = [];
  for (let k = 0; k <= 24; k++) {
    const a = (k / 24) * Math.PI * 2;
    const q = project(cam, view, Math.cos(a) * 9, 0, CLEARING_Z + Math.sin(a) * 7);
    if (q) clearing.push([q[0], q[1]]);
  }
  const z0 = Math.max(cam.z + 0.4, 130);
  const z1 = CLEARING_Z - 4;
  const corners = [
    project(cam, view, -1.6, 0, z0),
    project(cam, view, 1.6, 0, z0),
    project(cam, view, 1.6, 0, z1),
    project(cam, view, -1.6, 0, z1),
  ];
  const path: Pt[] = corners.flatMap((c) => (c ? [[c[0], c[1]] as Pt] : []));
  const stoneLines: Array<readonly [Pt, Pt]> = [];
  for (let z = Math.ceil(z0 / 1.4) * 1.4; z < z1; z += 1.4) {
    const a = project(cam, view, -1.6, 0, z);
    const b = project(cam, view, 1.6, 0, z);
    if (a && b)
      stoneLines.push([
        [a[0], a[1]],
        [b[0], b[1]],
      ]);
  }
  return { horizonY: view.h / 2, path, clearing, stoneLines };
}

const fixed = (n: number) => n.toFixed(2);

/** Caminho SVG (y para baixo) de poligonos e circulos em coordenadas do mundo (y para cima). */
export function toSvgPath(o: Shapes): string {
  const parts: string[] = [];
  for (const poly of o.polys) {
    parts.push(`M${poly.map(([x, y]) => `${fixed(x)} ${fixed(-y)}`).join("L")}Z`);
  }
  for (const [x, y, r] of o.circles) {
    parts.push(
      `M${fixed(x - r)} ${fixed(-y)}A${fixed(r)} ${fixed(r)} 0 1 0 ${fixed(x + r)} ${fixed(-y)}A${fixed(r)} ${fixed(r)} 0 1 0 ${fixed(x - r)} ${fixed(-y)}Z`,
    );
  }
  return parts.join("");
}
