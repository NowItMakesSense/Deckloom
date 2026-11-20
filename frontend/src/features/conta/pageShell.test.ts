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
    // v1.0: vidro forte (1.6), no ::before para nao conter o Dialog fixo
    expect(declarationsFor(`${page}::before`).get("background-color")).toBe(
      "var(--glass-bg-strong)",
    );
    expect(d.get("border-radius")).toBe("var(--radius-lg)");
  });

  it("perfil: tela de leitura em painel de ate 36rem", () => {
    const d = declarationsFor("profile");
    expect(d.get("max-width")).toBe("36rem");
    expect(d.get("margin-inline")).toBe("auto");
    expect(declarationsFor("profile::before").get("background-color")).toBe(
      "var(--glass-bg-strong)",
    );
  });

  it("formularios das paginas espacam os campos com --space-4", () => {
    const d = declarationsFor("page-form");
    expect(d.get("display")).toBe("flex");
    expect(d.get("gap")).toBe("var(--space-4)");
  });
});

describe("AppHeader: barra flutuante de 56px em vidro (v1.0)", () => {
  const header = declarationsFor(
    "app-header",
    Object.values(chromeStyles).flatMap((css) => parseCss(css).rules),
  );

  it("sem borda inferior de ponta a ponta e em vidro comum", () => {
    expect(header.has("border-bottom")).toBe(false);
    // Tentativa 4: o vidro mora no ::before, para a pilula nao virar a raiz do fundo dos menus aninhados.
    const glass = declarationsFor(
      "app-header::before",
      Object.values(chromeStyles).flatMap((css) => parseCss(css).rules),
    );
    expect(glass.get("background-color")).toBe("var(--glass-bg)");
    expect(header.has("backdrop-filter")).toBe(false);
    expect(header.has("background-color")).toBe(false);
  });

  it("altura minima de 56px", () => {
    expect(header.get("min-height")).toBe("56px");
  });
});

// T-L01C tentativa 2 / REQ-L03 v5 (design-system 1.13): o cabecalho e a pilula "dynamic island" da landing,
// centralizada e com a largura do conteudo (no celular ocupa a largura entre as margens, para o menu da conta ancorar).
describe("AppHeader: pilula centralizada (dynamic island)", () => {
  const chromeRules = Object.values(chromeStyles).flatMap((css) => parseCss(css).rules);
  const header = declarationsFor("app-header", chromeRules);
  const narrow = new Map<string, string>();
  for (const rule of chromeRules) {
    if (rule.keyframes || !rule.media.some((m) => /max-width:\s*479/.test(m))) continue;
    if (
      rule.selector
        .split(",")
        .map((s) => s.trim())
        .includes(".app-header")
    ) {
      for (const { property, value } of rule.declarations) narrow.set(property, normalize(value));
    }
  }

  it("e uma pilula (raio total) e nao uma barra de ponta a ponta", () => {
    expect(header.get("border-radius")).toBe("var(--radius-full)");
    expect(header.get("width")).toBe("fit-content");
  });

  it("fica centralizada, com folga das bordas da tela", () => {
    expect(header.get("margin")).toBe("var(--space-3) auto 0");
    expect(header.get("max-width")).toMatch(/^calc\(100% - /);
  });

  it("no celular ocupa a largura entre as margens", () => {
    expect(narrow.get("width")).toBe("auto");
  });

  it("a logo, as acoes de visitante, o botao primario e o botao de tema tem regras proprias", () => {
    for (const selector of [
      "app-header__logo",
      "app-header__link",
      "app-header__cta",
      "app-header__icon",
    ]) {
      expect(declarationsFor(selector, chromeRules).size, selector).toBeGreaterThan(0);
    }
    expect(declarationsFor("app-header__cta", chromeRules).get("background-color")).toBe(
      "var(--color-primary)",
    );
    expect(declarationsFor("app-header__icon", chromeRules).get("width")).toBe("var(--target-min)");
    // ART-08: a logo vira so o simbolo (32px) no celular e o alvo continua com 44px de largura.
    expect(declarationsFor("app-header__logo", chromeRules).get("min-width")).toBe(
      "var(--target-min)",
    );
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

// T-L01C tentativa 4 (decisoes do usuario): (1) no celular a pilula fica numa linha so, com o Entrar atras de um botao
// de menu no mesmo ponto de quebra da landing (1099.98px); (2) a pilula fica fixa no topo ao rolar (sticky, com
// safe-area) e o documento reserva a altura dela no scroll-padding, para nenhum campo focado ficar sob a pilula.
describe("AppHeader: pilula fixa no topo e menu do visitante no celular", () => {
  const chromeRules = Object.values(chromeStyles).flatMap((css) => parseCss(css).rules);
  const SAFE_TOP = "env(safe-area-inset-top, 0px)";

  function declarationsIn(selector: string, media: RegExp | null): Map<string, string> {
    const result = new Map<string, string>();
    for (const rule of chromeRules) {
      if (rule.keyframes) continue;
      // So a regra direta do @media (sem o @supports de reserva aninhado nele).
      if (
        media === null
          ? rule.media.length > 0
          : rule.media.length !== 1 || !media.test(rule.media[0])
      )
        continue;
      if (!rule.selector.split(",").some((s) => s.trim() === selector)) continue;
      for (const { property, value } of rule.declarations) result.set(property, normalize(value));
    }
    return result;
  }

  const MOBILE = /max-width:\s*1099\.98px/;

  it("a pilula e sticky no topo, com a folga de --space-3 somada a safe-area", () => {
    const header = declarationsIn(".app-header", null);
    expect(header.get("position")).toBe("sticky");
    expect(header.get("top")).toBe(`calc(var(--space-3) + ${SAFE_TOP})`);
    expect(header.get("margin-block-start")).toBe(`calc(var(--space-3) + ${SAFE_TOP})`);
  });

  it("fica acima do conteudo e abaixo do dialogo (scrim em 100)", () => {
    const z = Number(declarationsIn(".app-header", null).get("z-index"));
    expect(z).toBeGreaterThan(0);
    expect(z).toBeLessThan(100);
  });

  it("o documento reserva a altura medida da pilula no scroll-padding (campo focado nunca fica sob ela)", () => {
    const root = declarationsIn(":root:has(.app-header)", null);
    const padding = root.get("scroll-padding-block-start") ?? "";
    expect(padding).toContain("var(--app-header-block-size");
    expect(padding).toContain(SAFE_TOP);
  });

  it("o botao de menu so aparece no ponto de quebra da landing", () => {
    expect(declarationsIn(".app-header__menu-toggle", null).get("display")).toBe("none");
    expect(declarationsIn(".app-header__menu-toggle", MOBILE).get("display")).toBe("inline-flex");
  });

  it("no celular o menu abre como painel de vidro abaixo da pilula, so quando aberto", () => {
    const menu = declarationsIn(".app-header__menu", MOBILE);
    expect(menu.get("position")).toBe("absolute");
    expect(menu.get("display")).toBe("none");
    expect(menu.get("background-color")).toBe("var(--glass-bg-strong)");
    expect(declarationsIn('.app-header__menu[data-open="true"]', MOBILE).get("display")).toBe(
      "flex",
    );
  });

  it("no celular a pilula nao quebra de linha com texto normal (as acoes nao encolhem nem descem)", () => {
    // flex-wrap continua (reflow a 200%, U-09); a linha unica e medida no E2E a 320 e 375px.
    expect(declarationsIn(".app-header__cta", null).get("white-space")).toBe("nowrap");
    expect(declarationsIn(".app-header__menu-toggle", MOBILE).get("flex-shrink")).toBe("0");
  });
});
