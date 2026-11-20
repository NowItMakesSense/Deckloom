// Modelos glTF da landing (T-L05, REQ-L14): validos, dentro do orcamento de celular e com fonte e licenca.
// AC-L05d: gltf-validator sem erros, soma comprimida e triangulos dentro do orcamento. O usuario subiu o orcamento
// para 5 MB e 250 mil triangulos ao pedir o fundo em 3D (2026-11-14).
// AC-L05e: SOURCES.md lista cada asset com origem, autor e licenca (CC0, MIT ou arte propria do Deckloom).
import { readFileSync, readdirSync, statSync } from "node:fs";
import { resolve } from "node:path";
import { validateBytes } from "gltf-validator";
import { describe, expect, it } from "vitest";

const MODELS = resolve(__dirname, "../../../../public/models");
const GLBS = readdirSync(MODELS).filter((f) => f.endsWith(".glb"));
const MAX_BYTES = 5 * 1024 * 1024;
const MAX_TRIANGLES = 250_000;
const LICENSES = ["CC0", "MIT", "Própria (Deckloom)"];

interface GltfJson {
  textures?: { source?: number; extensions?: { EXT_texture_webp?: { source?: number } } }[];
  images?: unknown[];
  nodes?: { mesh?: number }[];
  meshes?: { primitives: { indices?: number; mode?: number }[] }[];
  accessors?: { count: number }[];
}

function glbJson(path: string): GltfJson {
  const buf = readFileSync(path);
  const len = buf.readUInt32LE(12);
  return JSON.parse(buf.toString("utf8", 20, 20 + len)) as GltfJson;
}

function triangles(json: GltfJson): number {
  const perMesh = (json.meshes ?? []).map((m) =>
    m.primitives
      .filter((p) => (p.mode ?? 4) === 4 && p.indices !== undefined)
      .reduce((sum, p) => sum + (json.accessors?.[p.indices as number].count ?? 0) / 3, 0),
  );
  return (json.nodes ?? []).reduce(
    (sum, n) => sum + (n.mesh === undefined ? 0 : perMesh[n.mesh]),
    0,
  );
}

describe("modelos glTF em public/models", () => {
  it("existe ao menos a deck box", () => {
    expect(GLBS).toContain("deckbox.glb");
  });

  it.each(GLBS)("%s passa no gltf-validator sem erros", async (file) => {
    const report = await validateBytes(new Uint8Array(readFileSync(resolve(MODELS, file))), {
      uri: file,
    });
    const errors = report.issues.messages.filter((m: { severity: number }) => m.severity === 0);
    expect(errors, JSON.stringify(errors.slice(0, 5))).toEqual([]);
  });

  // o gltf-validator aceita textura sem imagem (fallback ausente), mas o GLTFLoader do three quebra e o palco cai
  it.each(GLBS)("%s: toda textura aponta para uma imagem do arquivo", (file) => {
    const json = glbJson(resolve(MODELS, file));
    for (const [i, t] of (json.textures ?? []).entries()) {
      const src = t.extensions?.EXT_texture_webp?.source ?? t.source;
      expect(src, `${file} textura ${i}`).toBeTypeOf("number");
      expect(src as number).toBeLessThan(json.images?.length ?? 0);
    }
  });

  it("a soma dos arquivos comprimidos cabe em 5 MB", () => {
    const total = GLBS.reduce((sum, f) => sum + statSync(resolve(MODELS, f)).size, 0);
    expect(total).toBeLessThanOrEqual(MAX_BYTES);
  });

  it("os modelos somam no maximo 250 mil triangulos", () => {
    const total = GLBS.reduce((sum, f) => sum + triangles(glbJson(resolve(MODELS, f))), 0);
    expect(total).toBeGreaterThan(0);
    expect(total).toBeLessThanOrEqual(MAX_TRIANGLES);
  });
});

describe("SOURCES.md e avisos de terceiros", () => {
  const sources = readFileSync(resolve(MODELS, "SOURCES.md"), "utf8");
  const rows = sources
    .split("\n")
    .filter((l) => l.startsWith("|") && !/^\|\s*-/.test(l))
    .slice(1)
    .map((l) =>
      l
        .split("|")
        .slice(1, -1)
        .map((c) => c.trim()),
    );

  it.each(GLBS)("%s tem linha com origem, autor e licenca permitida", (file) => {
    const row = rows.find((r) => r[0] === file);
    expect(row, file).toBeDefined();
    const [, origin, author, license] = row as string[];
    expect(origin).not.toBe("");
    expect(author).not.toBe("");
    expect(LICENSES).toContain(license);
  });

  it("THIRD_PARTY_NOTICES.md cita os modelos 3D da landing", () => {
    const notices = readFileSync(resolve(__dirname, "../../../../THIRD_PARTY_NOTICES.md"), "utf8");
    expect(notices).toMatch(/public\/models\/SOURCES\.md/);
  });
});
