// Shell "No sidebar" (design-system.md 1.5) e Panel v0.1: coluna centralizada com painel de superficie.
// jsdom nao calcula layout; a verificacao e sobre o CSS, como em appChrome.test.tsx.
import { describe, expect, it } from "vitest";

import { normalize, parseCss, type CssRule } from "@/shared/testing/cssScan";

const pageStyles = import.meta.glob("./*.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

const chromeStyles = import.meta.glob("../../design-system/AppHeader.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

const baseStyles = import.meta.glob("../../design-system/{tokens,TextField}.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

const rules: CssRule[] = Object.values(pageStyles).flatMap((css) => parseCss(css).rules);

function declarationsFor(className: string, source: CssRule[] = rules): Map<string, string> {
  const result = new Map<string, string>();
  for (const rule of source) {
    if (rule.media.length > 0 || rule.keyframes) continue;
    const selectors = rule.selector.split(",").map((s) => s.trim());
    if (!selectors.includes(`.${className}`)) continue;
    for (const { property, value } of rule.declarations) result.set(property, normalize(value));
  }
  return result;
}

const FORM_PAGES = [
  "login-page",
  "cadastro-page",
  "forgot-password-page",
  "reset-password-page",
  "google-link-page",
  "google-signup-page",
  "google-return-page",
  "edit-profile",
];

describe("Shell das paginas: coluna centralizada em painel", () => {
  it.each(FORM_PAGES)("%s: formulario em painel de ate 28rem, centralizado", (page) => {
    const d = declarationsFor(page);
    expect(d.get("max-width")).toBe("28rem");
    expect(d.get("margin-inline")).toBe("auto");
    expect(d.get("background")).toBe("var(--color-surface)");
    expect(d.get("border-radius")).toBe("var(--radius-lg)");
  });

  it("perfil: tela de leitura em painel de ate 36rem", () => {
    const d = declarationsFor("profile");
    expect(d.get("max-width")).toBe("36rem");
    expect(d.get("margin-inline")).toBe("auto");
    expect(d.get("background")).toBe("var(--color-surface)");
  });

  it("formularios das paginas espacam os campos com --space-4", () => {
    const d = declarationsFor("page-form");
    expect(d.get("display")).toBe("flex");
    expect(d.get("gap")).toBe("var(--space-4)");
  });
});

describe("AppHeader: barra de 56px sobre o fundo, sem borda (v0.1)", () => {
  const header = declarationsFor(
    "app-header",
    Object.values(chromeStyles).flatMap((css) => parseCss(css).rules),
  );

  it("sem borda e sobre --color-ground", () => {
    expect(header.has("border-bottom")).toBe(false);
    expect(header.get("background")).toBe("var(--color-ground)");
  });

  it("altura minima de 56px", () => {
    expect(header.get("min-height")).toBe("56px");
  });
});

// T-174 / U-11: a 320px com texto a 200% e espacamento aumentado o titulo e o email somente leitura (S4, S5)
// passavam da largura da tela. O layout real so e medido no E2E (accessibility.spec.ts).
function declarationsForSelector(selector: string, source: CssRule[] = rules): Map<string, string> {
  const result = new Map<string, string>();
  for (const rule of source) {
    if (rule.media.length > 0 || rule.keyframes) continue;
    if (!rule.selector.split(",").some((s) => s.trim() === selector)) continue;
    for (const { property, value } of rule.declarations) result.set(property, normalize(value));
  }
  return result;
}

describe("Shell das paginas: quebra de titulo e email (U-11)", () => {
  it.each(FORM_PAGES)("o h1 de .%s quebra em qualquer ponto, numa regra so", (page) => {
    const selector = `.${page} > h1`;
    const declaring = rules.filter(
      (rule) =>
        rule.media.length === 0 &&
        !rule.keyframes &&
        rule.selector.split(",").some((s) => s.trim() === selector) &&
        rule.declarations.some((d) => d.property === "overflow-wrap"),
    );
    expect(declaring).toHaveLength(1);
    expect(declarationsForSelector(selector).get("overflow-wrap")).toBe("anywhere");
  });

  it("o email somente leitura do Google quebra em qualquer ponto", () => {
    expect(declarationsFor("page-readonly-email").get("overflow-wrap")).toBe("anywhere");
  });
});

// T-175 / U-13: a spec fixa o h1 em 24/32 (design.md A.8.3); sem regra, valia o padrao do navegador (32px).
// Estilos de base: tokens.css e TextField.css.
const baseRules: CssRule[] = Object.values(baseStyles).flatMap((css) => parseCss(css).rules);

describe("Base: h1 com o token da spec (U-13)", () => {
  it("h1 usa --text-2xl e altura de linha 1.333, sem valor fixo", () => {
    const d = declarationsForSelector("h1", baseRules);
    expect(d.get("font-size")).toBe("var(--text-2xl)");
    expect(d.get("line-height")).toBe("1.333");
  });
});

describe("TextField: o controle quebra linha para o botao descer (U-12)", () => {
  it(".text-field__control tem flex-wrap: wrap e o input uma base de largura", () => {
    const control = declarationsFor("text-field__control", baseRules);
    const input = declarationsFor("text-field__input", baseRules);
    expect(control.get("flex-wrap")).toBe("wrap");
    expect(input.get("flex")).toBe("1 1 5rem");
  });
});
