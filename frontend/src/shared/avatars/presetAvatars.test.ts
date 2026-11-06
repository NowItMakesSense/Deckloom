// T-129 (REQ-018, REQ-022): os SVG dos avatares preset correspondem a carga do banco.
// Fonte da lista de ids: AvatarPresetEntityConfiguration.cs (InitialPresets), lido como texto.
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const repoRoot = resolve(__dirname, "../../../..");
const backendConfig = resolve(
  repoRoot,
  "backend/src/Modules/Accounts/Deckloom.Accounts.Infrastructure/AvatarPresetEntityConfiguration.cs",
);
const presetsDir = resolve(repoRoot, "frontend/public/avatars/presets");
const MAX_BYTES = 4 * 1024;

function seededIds(): string[] {
  const source = readFileSync(backendConfig, "utf8");
  return [...source.matchAll(/Preset\("([a-z0-9-]+)",/g)].map((m) => m[1]);
}

function defaultId(): string {
  const source = readFileSync(backendConfig, "utf8");
  const match = source.match(/Preset\("([a-z0-9-]+)",[^)]*isDefault: true/);
  if (!match) throw new Error("Nenhum preset padrao na carga do backend");
  return match[1];
}

function svgPath(id: string): string {
  return resolve(presetsDir, `${id}.svg`);
}

function readSvg(id: string): string {
  const path = svgPath(id);
  if (!existsSync(path)) {
    throw new Error(`Arquivo SVG ausente para o preset "${id}": esperado em ${path}`);
  }
  return readFileSync(path, "utf8");
}

const ids = seededIds();

describe("PresetAssetsTests - SVG dos avatares preset (REQ-018, REQ-022)", () => {
  it("a carga do backend tem entre 8 e 12 presets", () => {
    expect(ids.length).toBeGreaterThanOrEqual(8);
    expect(ids.length).toBeLessThanOrEqual(12);
  });

  it("o diretorio de presets existe e nao tem arquivo sem id na carga", () => {
    if (!existsSync(presetsDir)) {
      throw new Error(`Diretorio de SVG ausente: ${presetsDir}`);
    }
    const files = readdirSync(presetsDir).sort();
    expect(files).toEqual(ids.map((id) => `${id}.svg`).sort());
  });

  it("o preset padrao (circulo-azul) existe como SVG", () => {
    expect(defaultId()).toBe("circulo-azul");
    expect(existsSync(svgPath(defaultId())), `SVG ausente: ${svgPath(defaultId())}`).toBe(true);
  });

  describe.each(ids)("preset %s", (id) => {
    it("existe o arquivo SVG correspondente", () => {
      expect(existsSync(svgPath(id)), `SVG ausente: ${svgPath(id)}`).toBe(true);
    });

    it("e um SVG valido com viewBox quadrado", () => {
      const svg = readSvg(id);
      expect(svg.trimStart().startsWith("<svg")).toBe(true);
      const box = svg.match(/viewBox="\s*[\d.-]+[\s,]+[\d.-]+[\s,]+([\d.]+)[\s,]+([\d.]+)\s*"/);
      expect(box, "viewBox ausente").not.toBeNull();
      expect(box![1]).toBe(box![2]);
    });

    it("e neutro: sem texto, sem image, sem recurso externo", () => {
      const svg = readSvg(id);
      expect(svg).not.toMatch(/<text[\s>]/i);
      expect(svg).not.toMatch(/<image[\s>]/i);
      expect(svg).not.toMatch(/https?:\/\/(?!www\.w3\.org\/2000\/svg)/i);
      for (const m of svg.matchAll(/(?:xlink:)?href\s*=\s*"([^"]*)"/gi)) {
        expect(m[1].startsWith("#"), `href externo: ${m[1]}`).toBe(true);
      }
    });

    it("nao tem script nem handlers on*", () => {
      const svg = readSvg(id);
      expect(svg).not.toMatch(/<script[\s>]/i);
      expect(svg).not.toMatch(/\son[a-z]+\s*=/i);
      expect(svg).not.toMatch(/javascript:/i);
    });

    it("e pequeno (< 4 KB) para o cache imutavel", () => {
      readSvg(id);
      expect(statSync(svgPath(id)).size).toBeLessThan(MAX_BYTES);
    });
  });
});
