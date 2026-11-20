// T-L01 - tokens de design (docs/design-system.md v1.0 "Grimorio arcano"): dois temas, vidro, tipografia,
// contraste AA dos pares declarados (inclusive texto sobre vidro) e documento base.
import { describe, expect, it } from "vitest";

import indexHtml from "../index.html?raw";
import mainSource from "./main.tsx?raw";
import { normalize, parseCss, readAllCss, type CssRule } from "./shared/testing/cssScan";

const TOKENS_PATH = "/src/design-system/tokens.css";
const FONTS_PATH = "/src/design-system/fonts.css";

type Theme = "light" | "dark";
type TokenMap = Map<string, string>;

const DARK_ATTRIBUTE_SELECTOR = ':root[data-theme="dark"]';
const DARK_SYSTEM_SELECTOR = ':root:not([data-theme="light"])';

function loadRules(): CssRule[] {
  const css = readAllCss()[TOKENS_PATH];
  if (css === undefined) {
    throw new Error(`${TOKENS_PATH} nao existe`);
  }
  return parseCss(css).rules;
}

function collect(rules: CssRule[]): TokenMap {
  const tokens: TokenMap = new Map();
  for (const rule of rules) {
    for (const { property, value } of rule.declarations) {
      if (property.startsWith("--")) tokens.set(property, value);
    }
  }
  return tokens;
}

function lightOverrides(): TokenMap {
  return collect(loadRules().filter((r) => r.media.length === 0 && r.selector.trim() === ":root"));
}

/** Tokens do tema escuro escolhido pelo usuario: `:root[data-theme="dark"]`. */
function darkByAttribute(): TokenMap {
  return collect(
    loadRules().filter(
      (r) => r.media.length === 0 && r.selector.trim() === DARK_ATTRIBUTE_SELECTOR,
    ),
  );
}

/** Tokens do tema escuro quando o sistema esta escuro e o usuario nao escolheu claro. */
function darkBySystem(): TokenMap {
  return collect(
    loadRules().filter(
      (r) =>
        r.media.some((m) => /prefers-color-scheme:\s*dark/.test(m)) &&
        r.selector.trim() === DARK_SYSTEM_SELECTOR,
    ),
  );
}

function loadTokens(theme: Theme = "light"): TokenMap {
  const tokens = lightOverrides();
  if (theme === "dark") {
    for (const [name, value] of darkByAttribute()) tokens.set(name, value);
  }
  return tokens;
}

function resolve(tokens: TokenMap, name: string): string {
  let value = tokens.get(name);
  for (let depth = 0; value !== undefined && depth < 5; depth += 1) {
    const alias = /^var\((--[\w-]+)\)$/.exec(value);
    if (!alias) return value;
    value = tokens.get(alias[1]);
  }
  throw new Error(`token ${name} ausente ou com alias nao resolvido`);
}

type Rgb = [number, number, number];

function hexToRgb(hex: string): Rgb {
  const match = /^#([0-9a-f]{6})$/i.exec(hex.trim());
  if (!match) throw new Error(`cor hexadecimal esperada, recebido '${hex}'`);
  return [0, 2, 4].map((i) => parseInt(match[1].slice(i, i + 2), 16)) as Rgb;
}

function luminance([r, g, b]: Rgb): number {
  const channels = [r, g, b].map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}

function ratio(a: Rgb, b: Rgb): number {
  const [x, y] = [luminance(a), luminance(b)];
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
}

/** Cor opaca de um token (hex) ou o resultado de um rgba() composto sobre `backdrop`. */
function paint(tokens: TokenMap, name: string, backdrop?: Rgb): Rgb {
  const value = normalize(resolve(tokens, name));
  const rgba = /^rgba\(\s*(\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\s*\)$/.exec(value);
  if (!rgba) return hexToRgb(value);
  if (!backdrop) throw new Error(`${name} e translucido: informe o fundo atras`);
  const alpha = Number(rgba[4]);
  const top: Rgb = [Number(rgba[1]), Number(rgba[2]), Number(rgba[3])];
  return top.map((c, i) => Math.round(alpha * c + (1 - alpha) * backdrop[i])) as Rgb;
}

function contrast(tokens: TokenMap, foreground: string, background: string, backdrop?: Rgb) {
  const bg = paint(tokens, background, backdrop);
  const fg = paint(tokens, foreground, bg);
  return ratio(fg, bg);
}

const LIGHT_VALUES: Array<[string, string]> = [
  ["--color-ground", "#f3effa"],
  ["--color-surface", "#fbf9ff"],
  ["--color-text", "#1b1230"],
  ["--color-text-muted", "#4a3f6b"],
  ["--color-border-decor", "#d9d0ee"],
  ["--color-border-control", "#6f6395"],
  ["--color-skeleton", "#e6def5"],
  ["--color-primary", "#5b2fc4"],
  ["--color-primary-hover", "#4a22a8"],
  ["--color-on-primary", "#ffffff"],
  ["--color-primary-subtle", "#ece6fb"],
  ["--color-on-primary-subtle", "#3d1d96"],
  ["--color-danger", "#b91c1c"],
  ["--color-danger-subtle", "#fdecef"],
  ["--color-danger-hover", "#991b1b"],
  ["--color-success", "#15803d"],
  ["--color-success-subtle", "#e9f7ee"],
  ["--color-warning", "#92400e"],
  ["--color-warning-subtle", "#fdf3dc"],
  ["--color-gold", "#b88a2e"],
  ["--color-gold-subtle", "#f3e6c4"],
  ["--color-on-gold", "#1b1230"],
  ["--color-on-gold-subtle", "#6b4a0a"],
  ["--glass-bg", "rgba(255, 255, 255, 0.66)"],
  ["--glass-bg-strong", "rgba(255, 255, 255, 0.84)"],
  ["--glass-edge", "rgba(91, 47, 196, 0.3)"],
  ["--glass-solid", "#f7f4fd"],
  ["--glass-solid-strong", "#fbf9ff"],
  ["--glass-worst", "#5b2fc4"],
  ["--aura-violet", "#8d66ff"],
  ["--aura-gold", "#e0b866"],
];

const DARK_VALUES: Array<[string, string]> = [
  ["--color-ground", "#120d1e"],
  ["--color-surface", "#1c1530"],
  ["--color-text", "#ece7fa"],
  ["--color-text-muted", "#b9aedb"],
  ["--color-border-decor", "#3a2f5c"],
  ["--color-border-control", "#8f82b8"],
  ["--color-skeleton", "#2a2145"],
  ["--color-primary", "#9a77ff"],
  ["--color-primary-hover", "#b39bff"],
  ["--color-on-primary", "#120d1e"],
  ["--color-primary-subtle", "#2a1f4d"],
  ["--color-on-primary-subtle", "#d6c8ff"],
  ["--color-danger", "#ff8f8f"],
  ["--color-danger-subtle", "#3a1219"],
  ["--color-danger-hover", "#ffb0b0"],
  ["--color-success", "#6fd49a"],
  ["--color-success-subtle", "#0f2c1c"],
  ["--color-warning", "#f0b35a"],
  ["--color-warning-subtle", "#33230a"],
  ["--color-gold", "#d9ae55"],
  ["--color-gold-subtle", "#3a2d12"],
  ["--color-on-gold", "#120d1e"],
  ["--color-on-gold-subtle", "#e8c46f"],
  ["--glass-bg", "rgba(34, 26, 60, 0.66)"],
  ["--glass-bg-strong", "rgba(30, 23, 52, 0.86)"],
  ["--glass-edge", "rgba(190, 165, 255, 0.3)"],
  ["--glass-solid", "#1f1836"],
  ["--glass-solid-strong", "#241c3f"],
  ["--glass-worst", "#9a77ff"],
  ["--aura-violet", "#5b2fc4"],
  ["--aura-gold", "#b88a2e"],
];

describe("tokens.css - tema claro em :root (docs/design-system.md v1.0)", () => {
  it("existe em src/design-system/tokens.css", () => {
    expect(Object.keys(readAllCss())).toContain(TOKENS_PATH);
  });

  it.each(LIGHT_VALUES)("%s = %s", (name, expected) => {
    expect(normalize(lightOverrides().get(name) ?? "")).toBe(expected);
  });

  it("--color-info aponta para --color-primary (um matiz so)", () => {
    expect(normalize(lightOverrides().get("--color-info") ?? "")).toBe("var(--color-primary)");
  });

  it("polos fixos dos dois temas: noite e lua (faixas invertidas, rodape)", () => {
    const t = lightOverrides();
    expect(normalize(t.get("--color-night") ?? "")).toBe("#120d1e");
    expect(normalize(t.get("--color-moon") ?? "")).toBe("#efe9ff");
    expect(contrast(t, "--color-moon", "--color-night")).toBeGreaterThanOrEqual(7);
  });

  it("nenhum cinza puro nos neutros (vies violeta: canal azul acima do vermelho)", () => {
    for (const theme of ["light", "dark"] as const) {
      const t = loadTokens(theme);
      for (const name of [
        "--color-ground",
        "--color-surface",
        "--color-text",
        "--color-text-muted",
        "--color-border-decor",
        "--color-border-control",
        "--color-skeleton",
      ]) {
        const [r, , b] = paint(t, name);
        expect(b, `${theme} ${name}`).toBeGreaterThan(r);
      }
    }
  });

  it("A11Y-39: o scrim e escuro e quase opaco (alfa >= 0.6) nos dois temas", () => {
    for (const theme of ["light", "dark"] as const) {
      const scrim = normalize(loadTokens(theme).get("--color-scrim") ?? "");
      const match = /^rgba\(\s*(\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\s*\)$/.exec(scrim);

      expect(match, `${theme} --color-scrim '${scrim}'`).not.toBeNull();
      expect(Number(match?.[4])).toBeGreaterThanOrEqual(0.6);
      expect(Math.max(Number(match?.[1]), Number(match?.[2]), Number(match?.[3]))).toBeLessThan(64);
    }
  });

  it("raios: 4 / 6 / 8 / pleno e o interno e menor que o externo (ART-03)", () => {
    const t = lightOverrides();

    expect(
      ["--radius-sm", "--radius-md", "--radius-lg", "--radius-full"].map((n) => t.get(n)),
    ).toEqual(["4px", "6px", "8px", "9999px"]);
  });

  it("sombras so de AccountMenu e Dialog, nos dois temas (ART-01)", () => {
    const compact = (value: string | undefined) => (value ?? "").replace(/\s+/g, "");
    const light = lightOverrides();
    const dark = darkByAttribute();

    expect(compact(light.get("--shadow-dialog"))).toBe(
      "01px2pxrgba(27,18,48,0.1),012px32pxrgba(27,18,48,0.22)",
    );
    expect(compact(light.get("--shadow-menu"))).toBe(
      "01px2pxrgba(27,18,48,0.1),08px24pxrgba(27,18,48,0.18)",
    );
    expect(compact(dark.get("--shadow-dialog"))).toBe(
      "01px2pxrgba(0,0,0,0.4),012px32pxrgba(0,0,0,0.55)",
    );
    expect(compact(dark.get("--shadow-menu"))).toBe(
      "01px2pxrgba(0,0,0,0.4),08px24pxrgba(0,0,0,0.5)",
    );
    for (const t of [light, dark]) {
      expect([...t.keys()].filter((n) => n.startsWith("--shadow-")).sort()).toEqual([
        "--shadow-dialog",
        "--shadow-menu",
      ]);
    }
  });

  it("espacamento em base 4px", () => {
    const t = lightOverrides();

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
    const t = lightOverrides();

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
});

describe("tokens.css - tema escuro: [data-theme=dark] e prefers-color-scheme", () => {
  it.each(DARK_VALUES)("%s = %s", (name, expected) => {
    expect(normalize(darkByAttribute().get(name) ?? "")).toBe(expected);
  });

  it("o tema escuro segue o sistema (media) e a escolha do usuario (atributo) com os mesmos valores", () => {
    const byAttribute = Object.fromEntries(darkByAttribute());
    const bySystem = Object.fromEntries(darkBySystem());

    expect(Object.keys(bySystem).length).toBeGreaterThan(20);
    expect(bySystem).toEqual(byAttribute);
  });

  it("o tema escuro sobrescreve so tokens que existem no claro (nenhum nome novo escondido)", () => {
    const light = lightOverrides();
    const missing = [...darkByAttribute().keys()].filter((n) => !light.has(n));

    expect(missing).toEqual([]);
  });

  it("color-scheme: claro em :root, escuro nos blocos escuros (controles nativos e barras de rolagem)", () => {
    const colorScheme = (rules: CssRule[], selector: string) =>
      rules
        .filter((r) => r.selector.trim() === selector)
        .flatMap((r) => r.declarations)
        .find((d) => d.property === "color-scheme")?.value;
    const rules = loadRules();

    expect(
      colorScheme(
        rules.filter((r) => r.media.length === 0),
        ":root",
      ),
    ).toBe("light");
    expect(
      colorScheme(
        rules.filter((r) => r.media.length === 0),
        DARK_ATTRIBUTE_SELECTOR,
      ),
    ).toBe("dark");
    expect(
      colorScheme(
        rules.filter((r) => r.media.some((m) => /prefers-color-scheme:\s*dark/.test(m))),
        DARK_SYSTEM_SELECTOR,
      ),
    ).toBe("dark");
  });
});

describe("tokens.css - tipografia Cinzel Decorative / Albert Sans e escala da skill", () => {
  it("pilhas de fonte: Cinzel Decorative nos titulos, Albert Sans no texto, com reserva do mesmo genero", () => {
    const t = lightOverrides();

    expect(normalize(t.get("--font-display") ?? "")).toBe(
      '"cinzel decorative", "iowan old style", "palatino linotype", palatino, georgia, serif',
    );
    expect(normalize(t.get("--font-sans") ?? "")).toBe(
      '"albert sans", system-ui, -apple-system, "segoe ui", roboto, "helvetica neue", arial, sans-serif',
    );
  });

  it("escala fluida da skill (cinco passos no maximo) e ajustes do display", () => {
    const t = lightOverrides();
    const clamp = (name: string) => normalize(t.get(name) ?? "");

    expect(clamp("--step-small")).toBe("clamp(0.8125rem, 0.79rem + 0.1vw, 0.875rem)");
    expect(clamp("--step-body")).toBe("clamp(1.0625rem, 1rem + 0.25vw, 1.1875rem)");
    expect(clamp("--step-lead")).toBe("clamp(1.25rem, 1.1rem + 0.7vw, 1.625rem)");
    expect(clamp("--step-head")).toBe("clamp(2rem, 1.3rem + 3.2vw, 4rem)");
    expect(t.get("--display-tracking")).toBe("0");
    expect(t.get("--display-leading")).toBe("1.05");
    expect(t.get("--display-weight")).toBe("700");
    expect(t.get("--display-weight-strong")).toBe("900");
    expect(t.get("--text-leading")).toBe("1.55");
  });

  it("escala do app mantida (nada abaixo de 14px nos componentes)", () => {
    const t = lightOverrides();

    expect(t.get("--text-sm")).toBe("0.875rem");
    expect(t.get("--text-base")).toBe("1rem");
    expect(t.get("--text-2xl")).toBe("1.5rem");
  });
});

describe("tokens.css - contraste AA dos pares declarados, nos dois temas (A11Y-13, A11Y-14)", () => {
  const textPairs: Array<[string, string]> = [
    ["--color-text", "--color-surface"],
    ["--color-text", "--color-ground"],
    ["--color-text-muted", "--color-surface"],
    ["--color-text-muted", "--color-ground"],
    ["--color-text", "--color-primary-subtle"],
    ["--color-text-muted", "--color-primary-subtle"],
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
    ["--color-on-gold-subtle", "--color-gold-subtle"],
    ["--color-on-gold", "--color-gold"],
    ["--color-text", "--color-gold-subtle"],
  ];

  describe.each(["light", "dark"] as const)("tema %s", (theme) => {
    it.each(textPairs)("texto %s sobre %s tem >= 4,5:1", (foreground, background) => {
      expect(contrast(loadTokens(theme), foreground, background)).toBeGreaterThanOrEqual(4.5);
    });

    const nonTextPairs: Array<[string, string]> = [
      ["--color-border-control", "--color-surface"],
      ["--color-border-control", "--color-ground"],
      ["--color-primary", "--color-surface"],
      ["--color-primary", "--color-ground"],
      ["--color-danger", "--color-surface"],
    ];

    it.each(nonTextPairs)("borda/icone/foco %s sobre %s tem >= 3:1", (foreground, background) => {
      expect(contrast(loadTokens(theme), foreground, background)).toBeGreaterThanOrEqual(3);
    });

    it("o dourado e metal (filete, selo): nunca texto sobre o fundo; texto dourado so em --color-on-gold-subtle", () => {
      const t = loadTokens(theme);
      // Metal (preenchimento) tem texto proprio: --color-on-gold; sobre o fundo o dourado so serve de enfeite.
      expect(contrast(t, "--color-on-gold", "--color-gold")).toBeGreaterThanOrEqual(4.5);
      expect(contrast(t, "--color-on-gold-subtle", "--color-gold-subtle")).toBeGreaterThanOrEqual(
        4.5,
      );
    });

    it("a borda decorativa nunca serve de borda de controle (< 3:1 por definicao)", () => {
      expect(contrast(loadTokens(theme), "--color-border-decor", "--color-surface")).toBeLessThan(
        3,
      );
    });
  });

  it("noite e lua (polos fixos) formam o par das faixas invertidas", () => {
    expect(contrast(lightOverrides(), "--color-moon", "--color-night")).toBeGreaterThanOrEqual(7);
  });
});

describe("tokens.css - vidro (glass): contraste no pior fundo e no fallback sem backdrop-filter (AC-L01c)", () => {
  const GLASS = ["--glass-bg", "--glass-bg-strong"] as const;
  const SOLID = ["--glass-solid", "--glass-solid-strong"] as const;

  it("tokens de vidro existem nos dois temas: translucido, borda, desfoque, saturacao e solido de reserva", () => {
    for (const theme of ["light", "dark"] as const) {
      const t = loadTokens(theme);
      for (const name of [
        "--glass-bg",
        "--glass-bg-strong",
        "--glass-edge",
        "--glass-solid",
        "--glass-solid-strong",
        "--glass-worst",
      ]) {
        expect(t.has(name), `${theme} ${name}`).toBe(true);
      }
      expect(t.get("--glass-blur")).toBe("18px");
      expect(t.get("--glass-saturate")).toBe("1.5");
    }
  });

  it("o vidro e translucido (alfa < 1) e o solido de reserva e opaco", () => {
    for (const theme of ["light", "dark"] as const) {
      const t = loadTokens(theme);
      for (const name of [...GLASS, "--glass-edge"]) {
        const alpha = Number(/,\s*([\d.]+)\s*\)$/.exec(normalize(t.get(name) ?? ""))?.[1]);
        expect(alpha, `${theme} ${name}`).toBeGreaterThan(0);
        expect(alpha, `${theme} ${name}`).toBeLessThan(1);
      }
      for (const name of SOLID) {
        expect(normalize(t.get(name) ?? ""), `${theme} ${name}`).toMatch(/^#[0-9a-f]{6}$/);
      }
    }
  });

  describe.each(["light", "dark"] as const)("tema %s", (theme) => {
    const foregrounds = ["--color-text", "--color-text-muted"];

    it.each(GLASS.flatMap((glass) => foregrounds.map((fg) => [fg, glass] as [string, string])))(
      "texto %s sobre %s no PIOR fundo previsto (--glass-worst) tem >= 4,5:1",
      (foreground, glass) => {
        const t = loadTokens(theme);
        const worst = paint(t, "--glass-worst");
        expect(contrast(t, foreground, glass, worst)).toBeGreaterThanOrEqual(4.5);
      },
    );

    it.each(
      GLASS.flatMap((glass) =>
        ["--color-text", "--color-text-muted", "--color-primary"].flatMap((fg) =>
          ["--color-ground", "--color-surface"].map(
            (bg) => [fg, glass, bg] as [string, string, string],
          ),
        ),
      ),
    )("texto %s sobre %s em fundo comum (%s) tem >= 4,5:1", (foreground, glass, backdropToken) => {
      const t = loadTokens(theme);
      const backdrop = paint(t, backdropToken);
      expect(contrast(t, foreground, glass, backdrop)).toBeGreaterThanOrEqual(4.5);
    });

    it.each(
      SOLID.flatMap((solid) =>
        ["--color-text", "--color-text-muted", "--color-primary"].map(
          (fg) => [fg, solid] as [string, string],
        ),
      ),
    )("fallback sem backdrop-filter: texto %s sobre %s tem >= 4,5:1", (foreground, solid) => {
      expect(contrast(loadTokens(theme), foreground, solid)).toBeGreaterThanOrEqual(4.5);
    });
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
    expect(lightOverrides().get("--target-min")).toBe("44px");
  });
});

describe("tokens.css - preferencias do usuario (A11Y-38)", () => {
  it("prefers-contrast: more usa --color-text no texto de apoio, em qualquer tema", () => {
    const rule = loadRules().find((r) => r.media.some((m) => /prefers-contrast:\s*more/.test(m)));

    expect(rule?.declarations).toContainEqual({
      property: "--color-text-muted",
      value: "var(--color-text)",
    });
    // O bloco escuro tem especificidade maior que :root: o ajuste precisa cobrir os seletores escuros.
    const selectors = (rule?.selector ?? "").split(",").map((s) => s.trim());
    expect(selectors).toEqual(
      expect.arrayContaining([":root", ":root[data-theme]", DARK_SYSTEM_SELECTOR]),
    );
  });

  it("prefers-reduced-transparency: o vidro vira o solido de reserva", () => {
    const rule = loadRules().find((r) =>
      r.media.some((m) => /prefers-reduced-transparency:\s*reduce/.test(m)),
    );
    const declarations = Object.fromEntries(
      (rule?.declarations ?? []).map((d) => [d.property, d.value]),
    );

    expect(declarations["--glass-bg"]).toBe("var(--glass-solid)");
    expect(declarations["--glass-bg-strong"]).toBe("var(--glass-solid-strong)");
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

  it("declara os dois temas ao navegador e nao usa script inline (CSP script-src 'self')", () => {
    expect(indexHtml).toMatch(/<meta[^>]*name="color-scheme"[^>]*content="light dark"/);
    expect(indexHtml).toMatch(/<meta[^>]*name="theme-color"[^>]*prefers-color-scheme:\s*dark/);
    expect(indexHtml).not.toMatch(/<script(?![^>]*\ssrc=)[^>]*>/i);
  });

  it("a entrada importa tokens e fontes e aplica o tema salvo antes de montar o React", () => {
    expect(mainSource).toMatch(/import\s+["'][^"']*design-system\/tokens\.css["']/);
    expect(mainSource).toMatch(/import\s+["'][^"']*design-system\/fonts\.css["']/);
    const apply = mainSource.indexOf("applyStoredTheme(");
    const render = mainSource.indexOf("createRoot(");
    expect(apply, "applyStoredTheme() ausente em main.tsx").toBeGreaterThan(-1);
    expect(apply).toBeLessThan(render);
  });

  it("estilos globais: fundo e texto por papel, fonte Albert Sans, titulos em Cinzel Decorative, corpo em rem", () => {
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
    expect(value("body", "line-height")).toBe("var(--text-leading)");
    expect(value("h1", "font-family")).toBe("var(--font-display)");
    expect(value("h1", "font-weight")).toBe("var(--display-weight)");
    expect(value("h1", "text-transform")).toBeUndefined();
  });
});

describe("fontes - arquivo fonts.css separado dos tokens", () => {
  it("existe em src/design-system/fonts.css", () => {
    expect(Object.keys(readAllCss())).toContain(FONTS_PATH);
  });

  it("declara so Cinzel Decorative (700 e 900) e Albert Sans (a Imbue e a direcao 'ficha de colecao' foram descartadas)", () => {
    const css = readAllCss()[FONTS_PATH] ?? "";
    const families = [...css.matchAll(/font-family:\s*["']([^"']+)["']/g)].map((m) => m[1]);

    expect(families.sort()).toEqual(["Albert Sans", "Cinzel Decorative", "Cinzel Decorative"]);
    expect(css).not.toMatch(/Imbue|Big Shoulders|IBM Plex/i);
  });

  it("Cinzel Decorative tem os pesos 700 e 900 e Albert Sans e variavel (cobre 400 e 500)", () => {
    const css = readAllCss()[FONTS_PATH] ?? "";
    const weights = [...css.matchAll(/font-weight:\s*([\d ]+);/g)].map((m) => m[1].trim());

    expect(weights.sort()).toEqual(["100 900", "700", "900"]);
  });

  it("o CSS antigo da landing (landing-tokens.css) nao existe mais", () => {
    expect(Object.keys(readAllCss())).not.toContain("/src/design-system/landing-tokens.css");
  });
});
