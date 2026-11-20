// T-L02 / AC-L02d: o palco libera geometrias, materiais e texturas ao desmontar.
import {
  BoxGeometry,
  Group,
  Mesh,
  MeshStandardMaterial,
  Points,
  PointsMaterial,
  Texture,
} from "three";
import { describe, expect, it, vi } from "vitest";

import { disposeObject3D } from "./dispose";

describe("disposeObject3D", () => {
  it("libera geometria, material e as texturas do material de cada malha da arvore", () => {
    const map = new Texture();
    const emissiveMap = new Texture();
    const geometry = new BoxGeometry();
    const material = new MeshStandardMaterial({ map, emissiveMap });
    const root = new Group();
    const child = new Group();
    child.add(new Mesh(geometry, material));
    root.add(child);
    const spies = [geometry, material, map, emissiveMap].map((resource) =>
      vi.spyOn(resource, "dispose"),
    );

    disposeObject3D(root);

    for (const spy of spies) expect(spy).toHaveBeenCalledTimes(1);
  });

  it("libera materiais em lista e pontos (particulas)", () => {
    const first = new MeshStandardMaterial();
    const second = new MeshStandardMaterial();
    const points = new Points(new BoxGeometry(), new PointsMaterial({ map: new Texture() }));
    const root = new Group();
    root.add(new Mesh(new BoxGeometry(), [first, second]), points);
    const spies = [first, second, points.material, points.geometry].map((resource) =>
      vi.spyOn(resource, "dispose"),
    );

    disposeObject3D(root);

    for (const spy of spies) expect(spy).toHaveBeenCalledTimes(1);
  });

  it("um material compartilhado por varias malhas e liberado uma vez so", () => {
    const shared = new MeshStandardMaterial();
    const spy = vi.spyOn(shared, "dispose");
    const root = new Group();
    root.add(new Mesh(new BoxGeometry(), shared), new Mesh(new BoxGeometry(), shared));

    disposeObject3D(root);

    expect(spy).toHaveBeenCalledTimes(1);
  });

  it("aceita uma arvore sem malhas", () => {
    expect(() => disposeObject3D(new Group())).not.toThrow();
  });
});
