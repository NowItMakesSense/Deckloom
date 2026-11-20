// Liberacao de recursos da cena ao desmontar (AC-L02d). So tipos de three: este modulo nao puxa a biblioteca.
import type { BufferGeometry, Material, Mesh, Object3D, Points, Texture } from "three";

interface Disposable {
  dispose(): void;
}

function isTexture(value: unknown): value is Texture {
  return typeof value === "object" && value !== null && (value as Texture).isTexture === true;
}

function disposeMaterial(material: Material, seen: Set<Disposable>): void {
  if (seen.has(material)) return;
  seen.add(material);
  for (const value of Object.values(material)) {
    if (isTexture(value) && !seen.has(value)) {
      seen.add(value);
      value.dispose();
    }
  }
  material.dispose();
}

/** Libera geometria, material e texturas de toda malha ou ponto da arvore, uma vez cada. */
export function disposeObject3D(root: Object3D): void {
  const seen = new Set<Disposable>();
  root.traverse((object) => {
    const drawable = object as Partial<Mesh | Points>;
    const geometry: BufferGeometry | undefined = drawable.geometry;
    if (geometry && !seen.has(geometry)) {
      seen.add(geometry);
      geometry.dispose();
    }
    const material = drawable.material;
    if (!material) return;
    for (const item of Array.isArray(material) ? material : [material]) disposeMaterial(item, seen);
  });
}
