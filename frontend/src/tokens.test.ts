// T-095 - tokens de design (docs/design-system.md v0.1), contraste, foco, alvos e documento base.
import { describe, expect, it } from "vitest";

import indexHtml from "../index.html?raw";
import mainSource from "./main.tsx?raw";
import { normalize, parseCss, readAllCss, type CssRule } from "./shared/testing/cssScan";

const TOKENS_PATH = "/src/design-system/tokens.css";

function loadRules(): CssRule[] {
  const css = readAllCss()[TOKENS_PATH];
  if (css === undefined) {
    throw new Error(`${TOKENS_PATH} nao existe (T-095 GREEN: criar o CSS de tokens)`);
  }
  return parseCss(css).rules;
}

function loadTokens(): Map<string, string> {
  const tokens = new Map<string, string>();
  for (const rule of loadRules()) {
    if (rule.media.length === 0 && rule.selector.trim() === ":root") {
      for (const { property, value } of rule.declarations) {
        if (property.startsWith("--")) tokens.set(property, value);
      }
    }
  }
  return tokens;
}

function resolve(tokens: Map<string, string>, name: string): string {
  let value = tokens.get(name);
  for (let depth = 0; value !== undefined && depth < 5; depth += 1) {
    const alias = /^var\((--[\w-]+)\)$/.exec(value);
    if (!alias) return value;
    value = tokens.get(alias[1]);
  }
  throw new Error(`token ${name} ausente ou com alias nao resolvido`);
}

function luminance(hex: string): number {
  const match = /^#([0-9a-f]{6})$/i.exec(hex.trim());
  if (!match) throw new Error(`cor hexadecimal esperada, recebido '${hex}'`);
  const channels = [0, 2, 4].map((i) => {
    const c = parseInt(match[1].slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

function contrast(tokens: Map<string, string>, foreground: string, background: string): number {
  const a = luminance(resolve(tokens, foreground));
  const b = luminance(resolve(tokens, background));
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

describe("tokens.css - valores de docs/design-system.md v0.1", () => {
  it("existe em src/design-system/tokens.css", () => {
    expect(Object.keys(readAllCss())).toContain(TOKENS_PATH);
  });

  it.each([
    ["--color-ground", "#f9fafb"],
    ["--color-surface", "#ffffff"],
    ["--color-text", "#111827"],
    ["--color-text-muted", "#4b5563"],
    ["--color-border-decor", "#d1d5db"],
    ["--color-border-control", "#6b7280"],
    ["--color-skeleton", "#e5e7eb"],
    ["--color-primary", "#1d4ed8"],
    ["--color-primary-hover", "#1e40af"],
    ["--color-on-primary", "#ffffff"],
    ["--color-primary-subtle", "#eff6ff"],
    ["--color-on-primary-subtle", "#1e40af"],
    ["--color-danger", "#b91c1c"],
    ["--color-danger-subtle", "#fef2f2"],
    ["--color-success", "#15803d"],
    ["--color-success-subtle", "#f0fdf4"],
    ["--color-warning", "#92400e"],
    ["--color-warning-subtle", "#fffbeb"],
  ])("%s = %s", (name, expected) => {
    expect(normalize(loadTokens().get(name) ?? "")).toBe(expected);
  });

  it("--color-info aponta para --color-primary (um matiz so)", () => {
    expect(normalize(loadTokens().get("--color-info") ?? "")).toBe("var(--color-primary)");
  });

  it("A11Y-39: o scrim e escuro e quase opaco (alfa >= 0.6, sem desfoque)", () => {
    const scrim = normalize(loadTokens().get("--color-scrim") ?? "");
    const match = /^rgba\(\s*(\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\s*\)$/.exec(scrim);

    expect(match, `--color-scrim '${scrim}'`).not.toBeNull();
    expect(Number(match?.[4])).toBeGreaterThanOrEqual(0.6);
    expect(Math.max(Number(match?.[1]), Number(match?.[2]), Number(match?.[3]))).toBeLessThan(64);
  });

  it("raios: 4 / 6 / 8 / pleno e o interno e menor que o externo (ART-03)", () => {
    const t = loadTokens();

    expect(
      ["--radius-sm", "--radius-md", "--radius-lg", "--radius-full"].map((n) => t.get(n)),
    ).toEqual(["4px", "6px", "8px", "9999px"]);
  });

  it("sombras so de AccountMenu e Dialog (ART-01)", () => {
    const t = loadTokens();

    const compact = (value: string | undefined) => (value ?? "").replace(/\s+/g, "");
    expect(compact(t.get("--shadow-dialog"))).toBe(
      "01px2pxrgba(17,24,39,0.08),012px32pxrgba(17,24,39,0.18)",
    );
    expect(compact(t.get("--shadow-menu"))).toBe(
      "01px2pxrgba(17,24,39,0.08),08px24pxrgba(17,24,39,0.14)",
    );
    expect([...t.keys()].filter((n) => n.startsWith("--shadow-")).sort()).toEqual([
      "--shadow-dialog",
      "--shadow-menu",
    ]);
  });

  it("espacamento em base 4px", () => {
    const t = loadTokens();

    const spaces = Object.fromEntries(
      ["0", "1", "2", "3", "4", "6", "8", "12", "16"].map((n) => [n, t.get(`--space-${n}`)]),
    );
    expect(spaces).toEqual({
      "0": "0",
      "1": "4px",
      "2": "8px",
      "3": "12px",
      "4": "16px",
      "6": "24px",
      "8": "32px",
      "12": "48px",
      "16": "64px",
    });
  });

  it("tokens de movimento (MOT-*, design-system 1.7)", () => {
    const t = loadTokens();

    expect(normalize(t.get("--ease-out") ?? "")).toBe("cubic-bezier(0.23, 1, 0.32, 1)");
    expect(normalize(t.get("--ease-in-out") ?? "")).toBe("cubic-bezier(0.77, 0, 0.175, 1)");
    expect(
      Object.fromEntries(
        [
          "--duration-press",
          "--duration-hover",
          "--duration-fade",
          "--duration-menu-in",
          "--duration-menu-out",
          "--duration-dialog-in",
          "--duration-dialog-out",
          "--duration-spin",
        ].map((n) => [n, t.get(n)]),
      ),
    ).toEqual({
      "--duration-press": "120ms",
      "--duration-hover": "150ms",
      "--duration-fade": "160ms",
      "--duration-menu-in": "150ms",
      "--duration-menu-out": "100ms",
      "--duration-dialog-in": "200ms",
      "--duration-dialog-out": "150ms",
      "--duration-spin": "800ms",
    });
  });

  it("tipografia: pilha do sistema e escala (nada abaixo de 14px)", () => {
    const t = loadTokens();

    expect(normalize(t.get("--font-sans") ?? "")).toBe(
      'system-ui, -apple-system, "segoe ui", roboto, "helvetica neue", arial, sans-serif',
    );
    expect(t.get("--text-sm")).toBe("0.875rem");
    expect(t.get("--text-base")).toBe("1rem");
    expect(t.get("--text-2xl")).toBe("1.5rem");
  });
});

describe("tokens.css - contraste (A11Y-13 e A11Y-14)", () => {
  const textPairs: Array<[string, string]> = [
    ["--color-text", "--color-surface"],
    ["--color-text", "--color-ground"],
    ["--color-text-muted", "--color-surface"],
    ["--color-text-muted", "--color-ground"],
    ["--color-primary", "--color-surface"],
    ["--color-primary", "--color-ground"],
    ["--color-primary-hover", "--color-surface"],
    ["--color-on-primary", "--color-primary"],
    ["--color-on-primary", "--color-primary-hover"],
    ["--color-on-primary-subtle", "--color-primary-subtle"],
    ["--color-danger", "--color-surface"],
    ["--color-danger", "--color-danger-subtle"],
    ["--color-success", "--color-surface"],
    ["--color-success", "--color-success-subtle"],
    ["--color-warning", "--color-surface"],
    ["--color-warning", "--color-warning-subtle"],
    ["--color-info", "--color-primary-subtle"],
    ["--color-on-primary", "--color-danger"],
  ];

  it.each(textPairs)("texto %s sobre %s tem >= 4,5:1", (foreground, background) => {
    expect(contrast(loadTokens(), foreground, background)).toBeGreaterThanOrEqual(4.5);
  });

  const nonTextPairs: Array<[string, string]> = [
    ["--color-border-control", "--color-surface"],
    ["--color-border-control", "--color-ground"],
    ["--color-primary", "--color-surface"],
    ["--color-primary", "--color-ground"],
    ["--color-danger", "--color-surface"],
  ];

  it.each(nonTextPairs)("borda/icone/foco %s sobre %s tem >= 3:1", (foreground, background) => {
    expect(contrast(loadTokens(), foreground, background)).toBeGreaterThanOrEqual(3);
  });

  it("a borda decorativa nunca serve de borda de controle (< 3:1 por definicao)", () => {
    expect(contrast(loadTokens(), "--color-border-decor", "--color-surface")).toBeLessThan(3);
  });
});

describe("tokens.css - foco visivel (A11Y-09, MOT-03)", () => {
  it("anel de foco: :focus-visible com outline 3px solid --color-primary e offset 2px, sem transicao", () => {
    const rule = loadRules().find(
      (r) =>
        r.media.length === 0 && r.selector.split(",").some((s) => s.trim() === ":focus-visible"),
    );
    const declarations = Object.fromEntries(
      (rule?.declarations ?? []).map((d) => [d.property, normalize(d.value)]),
    );

    expect(declarations["outline"]).toBe("3px solid var(--color-primary)");
    expect(declarations["outline-offset"]).toBe("2px");
    expect(declarations["transition"]).toBeUndefined();
  });
});

describe("tokens.css - alvos de toque (A11Y-36, ART-08)", () => {
  it("token de alvo minimo --target-min = 44px (44x44)", () => {
    expect(loadTokens().get("--target-min")).toBe("44px");
  });
});

describe("tokens.css - preferencias do usuario (A11Y-38)", () => {
  it("prefers-contrast: more usa --color-text no texto de apoio", () => {
    const rule = loadRules().find((r) => r.media.some((m) => /prefers-contrast:\s*more/.test(m)));

    expect(rule?.declarations).toContainEqual({
      property: "--color-text-muted",
      value: "var(--color-text)",
    });
  });
});

describe("documento base - index.html, entrada e estilos globais", () => {
  it('A11Y-21: <html lang="pt-BR">', () => {
    expect(indexHtml).toMatch(/<html[^>]*\slang="pt-BR"/);
  });

  it("viewport nao bloqueia o zoom (WCAG 1.4.4) e nao carrega recursos de terceiros", () => {
    expect(indexHtml).toMatch(/<meta[^>]*name="viewport"/);
    expect(indexHtml).not.toMatch(/user-scalable\s*=\s*no|maximum-scale/i);
    expect(indexHtml).not.toMatch(/(src|href)="https?:\/\//i);
  });

  it("a entrada da aplicacao importa os tokens (a pagina base usa os tokens)", () => {
    expect(mainSource).toMatch(/import\s+["'][^"']*design-system\/tokens\.css["']/);
  });

  it("estilos globais: tema claro, fundo e texto por papel, fonte do sistema, corpo em rem", () => {
    const rules = loadRules().filter((r) => r.media.length === 0);
    const value = (selector: string, property: string) =>
      rules
        .filter((r) => r.selector.split(",").some((s) => s.trim() === selector))
        .flatMap((r) => r.declarations)
        .find((d) => d.property === property)?.value;

    expect(value(":root", "color-scheme")).toBe("light");
    expect(value("body", "background-color")).toBe("var(--color-ground)");
    expect(value("body", "color")).toBe("var(--color-text)");
    expect(value("body", "font-family")).toBe("var(--font-sans)");
    expect(value("body", "font-size")).toBe("var(--text-base)");
  });
});
