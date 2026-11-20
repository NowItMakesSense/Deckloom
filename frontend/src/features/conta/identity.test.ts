// T-L01C / REQ-L08 v1, REQ-L03 v5: as telas e dialogos da conta-jogador usam a identidade "Grimorio arcano" v1.0
// (docs/design-system.md): so tokens (sem cor ou fonte literal), vidro com solido de reserva, textura no fundo
// e botoes de acao com icone. Os testes de comportamento das telas nao mudam.
import { describe, expect, it } from "vitest";

import {
  findCssViolations,
  findLiteralViolations,
  findTsxLiteralViolations,
  formatViolations,
} from "@/shared/testing/cssChecks";
import { normalize, parseCss, type CssRule } from "@/shared/testing/cssScan";

const accountCss = import.meta.glob(
  ["./*.css", "../../design-system/*.css", "!../../design-system/{tokens,fonts}.css"],
  { query: "?raw", import: "default", eager: true },
) as Record<string, string>;

// Cada grupo de fontes e um glob separado: a varredura confere que todos casam pelo menos um arquivo, para a
// cobertura nunca ficar nula em silencio (ex.: uma pasta so com .ts num glob de .tsx).
const SOURCE_GROUPS = {
  "features/conta/*.tsx": import.meta.glob(["./*.tsx", "!./*.test.tsx"], {
    query: "?raw",
    import: "default",
    eager: true,
  }),
  "design-system/*.tsx": import.meta.glob(
    ["../../design-system/*.tsx", "!../../design-system/*.test.tsx"],
    { query: "?raw", import: "default", eager: true },
  ),
  "app/*.tsx": import.meta.glob(["../../app/*.tsx", "!../../app/*.test.tsx"], {
    query: "?raw",
    import: "default",
    eager: true,
  }),
  "design-system/icons/**/*.{ts,tsx}": import.meta.glob(
    ["../../design-system/icons/**/*.{ts,tsx}", "!../../design-system/icons/**/*.test.{ts,tsx}"],
    { query: "?raw", import: "default", eager: true },
  ),
  "design-system/motion/**/*.{ts,tsx}": import.meta.glob(
    ["../../design-system/motion/**/*.{ts,tsx}", "!../../design-system/motion/**/*.test.{ts,tsx}"],
    { query: "?raw", import: "default", eager: true },
  ),
  "app/theme/**/*.{ts,tsx}": import.meta.glob(
    ["../../app/theme/**/*.{ts,tsx}", "!../../app/theme/**/*.test.{ts,tsx}"],
    { query: "?raw", import: "default", eager: true },
  ),
} as Record<string, Record<string, string>>;

const accountSources: Record<string, string> = Object.assign({}, ...Object.values(SOURCE_GROUPS));

/** Unica excecao justificada: o logotipo do Google mantem as cores de marca exigidas pelo Google. */
const LITERAL_EXCEPTIONS = ["GoogleButton.tsx"];

function sourceLiteralViolations(sources: Record<string, string>) {
  return Object.entries(sources)
    .filter(([path]) => !LITERAL_EXCEPTIONS.some((name) => path.endsWith(`/${name}`)))
    .flatMap(([path, source]) => findTsxLiteralViolations(source, path));
}

const tokensCss = (
  import.meta.glob("../../design-system/tokens.css", {
    query: "?raw",
    import: "default",
    eager: true,
  }) as Record<string, string>
)["../../design-system/tokens.css"];

const GLASS_FILTER = "blur(var(--glass-blur)) saturate(var(--glass-saturate))";

function declarations(css: string, selector: string, inSupportsNot = false): Map<string, string> {
  const result = new Map<string, string>();
  const rules: CssRule[] = parseCss(css).rules.filter((rule) => {
    const fallback = rule.media.some((m) => /@supports\s+not/.test(m));
    return (
      !rule.keyframes && fallback === inSupportsNot && (inSupportsNot || rule.media.length === 0)
    );
  });
  for (const rule of rules) {
    if (!rule.selector.split(",").some((s) => s.trim() === selector)) continue;
    for (const { property, value } of rule.declarations) result.set(property, normalize(value));
  }
  return result;
}

describe("verificador de tokens (cor e fonte literais)", () => {
  it.each([
    [".a { color: #5b2fc4; }"],
    [".a { background: rgb(1, 2, 3); }"],
    [".a { border: 1px solid black; }"],
    [".a { font-family: Georgia, serif; }"],
    [".a { font: 16px Arial; }"],
  ])("reprova %s", (css) => {
    expect(findLiteralViolations(css).length).toBeGreaterThan(0);
  });

  it("aceita tokens, transparent, currentcolor e fonte herdada", () => {
    const css =
      ".a { color: var(--color-text); border: 1px solid transparent; outline-color: currentcolor; font: inherit; font-family: var(--font-display); background: var(--glass-bg); }";
    expect(formatViolations(findLiteralViolations(css))).toBe("");
  });
});

describe("verificador de tokens no TSX (cor nomeada, hexadecimal e fonte em style inline)", () => {
  it.each([
    ['<path fill="#5b2fc4" />'],
    ['<path stroke="red" />'],
    ['<div style={{ color: "blue" }} />'],
    ['<div style={{ background: "rgb(1, 2, 3)" }} />'],
    ['<div style={{ border: "1px solid black" }} />'],
    ['<div style={{ fontFamily: "Georgia, serif" }} />'],
    ["<div style={{ fontFamily: 'Arial' }} />"],
  ])("reprova %s", (tsx) => {
    expect(findTsxLiteralViolations(tsx).length).toBeGreaterThan(0);
  });

  it.each([
    ['<path stroke="currentColor" fill="none" />'],
    ['<div style={{ color: "var(--color-text)", fontFamily: "var(--font-display)" }} />'],
    ['<div style={{ transformOrigin: "19px 11px", fontFamily: "inherit" }} />'],
    ["<p>Gold e red sao so texto, e a cor vem do CSS</p>"],
    ["<div style={style} />"],
  ])("aceita %s", (tsx) => {
    expect(formatViolations(findTsxLiteralViolations(tsx))).toBe("");
  });
});

describe("verificador v1.0: vidro e textura", () => {
  it("aceita o vidro pelos tokens com o solido de reserva", () => {
    const css = `.p { background-color: var(--glass-bg-strong); backdrop-filter: ${GLASS_FILTER}; }
      @supports not (backdrop-filter: blur(1px)) { .p { background-color: var(--glass-solid-strong); } }`;
    expect(formatViolations(findCssViolations(css))).toBe("");
  });

  it("reprova o vidro sem o solido de reserva e o desfoque literal", () => {
    expect(
      findCssViolations(`.p { backdrop-filter: ${GLASS_FILTER}; }`).map((v) => v.message),
    ).toEqual([expect.stringContaining("sem fundo solido de reserva")]);
    expect(findCssViolations(".p { backdrop-filter: blur(8px); }").length).toBeGreaterThan(0);
  });

  it("aceita gradiente radial so no fundo da pagina", () => {
    const radial = "radial-gradient(circle at 20% 10%, var(--aura-violet), transparent 60%)";
    expect(formatViolations(findCssViolations(`body { background-image: ${radial}; }`))).toBe("");
    expect(findCssViolations(`.button { background-image: ${radial}; }`).length).toBeGreaterThan(0);
    expect(
      findCssViolations("body { background-image: linear-gradient(red, blue); }").length,
    ).toBeGreaterThan(0);
  });
});

describe("telas e dialogos da conta: so tokens (AC-L01Ca)", () => {
  it("o CSS da conta e do design system nao tem cor nem fonte literal fora dos tokens", () => {
    expect(Object.keys(accountCss).length).toBeGreaterThan(20);
    const violations = Object.entries(accountCss).flatMap(([path, css]) =>
      findLiteralViolations(css, path),
    );

    expect(formatViolations(violations)).toBe("");
  });

  it.each(Object.entries(SOURCE_GROUPS))(
    "a varredura de %s encontra pelo menos um arquivo",
    (_glob, files) => {
      expect(Object.keys(files).length).toBeGreaterThan(0);
    },
  );

  it("o TSX e o TS varridos nao tem cor nem fonte literal (so o logotipo do Google, cores de marca)", () => {
    expect(formatViolations(sourceLiteralViolations(accountSources))).toBe("");
  });

  it.each([
    ["Icone.tsx", '<path fill="red" />'],
    ["Titulo.tsx", '<h1 style={{ fontFamily: "Arial" }}>Deckloom</h1>'],
    ["Aviso.tsx", '<p style={{ color: "gold" }}>Aviso</p>'],
    ["motion/cor.ts", 'export const glow = "#d4a017";'],
  ])("a varredura reprova uma regressao em %s (%s)", (name, source) => {
    const regressed = { ...accountSources, [`../../design-system/${name}`]: source };

    expect(sourceLiteralViolations(regressed).map((v) => v.where)).toContain(
      `../../design-system/${name}`,
    );
  });

  it("a excecao do GoogleButton.tsx e real: ele ainda tem as cores de marca do logotipo", () => {
    const google = Object.entries(accountSources).find(([path]) =>
      path.endsWith("/GoogleButton.tsx"),
    );

    expect(google, "GoogleButton.tsx na varredura").toBeDefined();
    expect(findTsxLiteralViolations(google![1]).length).toBeGreaterThan(0);
  });
});

const PANELS: Array<[string, string, string]> = [
  ...[
    ".login-page",
    ".cadastro-page",
    ".forgot-password-page",
    ".reset-password-page",
    ".google-link-page",
    ".google-signup-page",
    ".google-return-page",
    ".edit-profile",
  ].map((s): [string, string, string] => ["./pageShell.css", `${s}::before`, "strong"]),
  ["./ProfilePage.css", ".profile::before", "strong"],
  ["../../design-system/Dialog.css", ".dialog__panel", "strong"],
  ["../../design-system/AccountMenu.css", ".account-menu__panel", "strong"],
  // Tentativa 4: o vidro da pilula mora no ::before. backdrop-filter na propria pilula a tornava a raiz do fundo
  // (backdrop root) dos menus aninhados, que ficavam translucidos e sem desfoque sobre a pagina.
  ["../../design-system/AppHeader.css", ".app-header::before", "common"],
];

describe("paineis, formularios, menu, dialogos e cabecalho em vidro (REQ-L03 v5, design-system 1.6)", () => {
  it.each(PANELS)(
    "%s %s: vidro com borda luminosa e solido de reserva",
    (file, selector, level) => {
      const css = accountCss[file];
      const bg = level === "strong" ? "glass-bg-strong" : "glass-bg";
      const solid = level === "strong" ? "glass-solid-strong" : "glass-solid";
      const d = declarations(css, selector);
      const fallback = declarations(css, selector, true);

      expect(d.get("background-color")).toBe(`var(--${bg})`);
      expect(d.get("backdrop-filter")).toBe(GLASS_FILTER);
      expect(d.get("-webkit-backdrop-filter")).toBe(GLASS_FILTER);
      expect(d.get("border")).toBe("1px solid var(--glass-edge)");
      expect(d.has("background")).toBe(false);
      expect(fallback.get("background-color")).toBe(`var(--${solid})`);
    },
  );

  it("os campos de entrada seguem solidos (so vidro em volta, 1.6.2)", () => {
    for (const file of ["TextField", "Textarea", "Select", "PasswordField"]) {
      expect(accountCss[`../../design-system/${file}.css`], file).not.toMatch(/backdrop-filter/);
    }
  });
});

describe("textura de fantasia atras do vidro (design-system 1.6.4)", () => {
  const body = () => declarations(tokensCss, "body");

  it("o fundo da pagina tem brilho radial violeta e dourado pelos tokens --aura-*", () => {
    const image = body().get("background-image") ?? "";

    expect(image).toMatch(/radial-gradient\(/);
    expect(image).toContain("var(--aura-violet)");
    expect(image).toContain("var(--aura-gold)");
    expect(image).not.toMatch(/linear-gradient|conic-gradient/);
  });

  it("o fundo fica fixo e nao rola com o conteudo", () => {
    expect(body().get("background-attachment")).toBe("fixed");
  });
});

describe("camadas: o dialogo cobre o cabecalho flutuante e nao e contido pelo painel", () => {
  const z = (file: string, selector: string) =>
    Number(declarations(accountCss[file], selector).get("z-index"));

  it("o scrim fica acima do cabecalho e do menu da conta", () => {
    const scrim = z("../../design-system/Dialog.css", ".dialog__scrim");

    expect(scrim).toBeGreaterThan(z("../../design-system/AppHeader.css", ".app-header"));
    expect(scrim).toBeGreaterThan(z("../../design-system/AccountMenu.css", ".account-menu__panel"));
  });

  it("o painel da tela nao cria contexto de empilhamento (isolation) que prenderia o scrim abaixo do cabecalho", () => {
    for (const selector of [".login-page", ".edit-profile"]) {
      expect(declarations(accountCss["./pageShell.css"], selector).has("isolation")).toBe(false);
    }
    expect(declarations(accountCss["./ProfilePage.css"], ".profile").has("isolation")).toBe(false);
  });
});
