// T-095 - varredura de TODO o CSS do frontend (design.md A.8.7: ART-01/03/04/05/06/07/08, A11Y-09/32/37/38/39).
// Vale para todas as tarefas seguintes: qualquer CSS novo em src/ entra na varredura.
import { describe, expect, it } from "vitest";

import {
  findCssViolations,
  findTextViolations,
  formatViolations,
  type Violation,
} from "./shared/testing/cssChecks";
import { parseCss, readAllCss, readProductionSources } from "./shared/testing/cssScan";

const ids = (violations: Violation[]) => [...new Set(violations.map((v) => v.id))].sort();

describe("cssRules - o verificador reconhece cada violacao (fixtures)", () => {
  const cases: Array<[string, string, string]> = [
    ["ART-01 gradiente", ".a { background: linear-gradient(#fff, #eee); }", "ART-01"],
    ["ART-01 backdrop-filter", ".a { backdrop-filter: blur(8px); }", "ART-01"],
    ["ART-01 brilho", ".a { filter: drop-shadow(0 0 8px #1d4ed8); }", "ART-01"],
    ["ART-01 sombra em repouso", ".card { box-shadow: 0 1px 2px rgba(0,0,0,.1); }", "ART-01"],
    ["ART-01 sombra token em nao flutuante", ".card { box-shadow: var(--shadow-menu); }", "ART-01"],
    ["ART-03 raio 12px", ".a { border-radius: 12px; }", "ART-03"],
    ["ART-03 raio 5px", ".a { border-top-left-radius: 5px; }", "ART-03"],
    ["ART-06 hover solto", ".a:hover { color: red; }", "ART-06"],
    [
      "ART-06 hover so com hover fino",
      "@media (hover: hover) { .a:hover { color: red; } }",
      "ART-06",
    ],
    ["ART-06 pressionavel sem :active", ".button { color: red; }", "ART-06"],
    ["ART-07 transition all", ".a { transition: all 150ms ease; }", "ART-07"],
    ["ART-07 transition sem propriedade", ".a { transition: 150ms ease; }", "ART-07"],
    ["ART-07 propriedade nao permitida", ".a { transition: width 150ms ease; }", "ART-07"],
    ["ART-07 will-change", ".a { will-change: transform; }", "ART-07"],
    ["ART-07 keyframes de altura", "@keyframes k { to { height: 10px; } }", "ART-07"],
    ["ART-08 controle baixo", "button { min-height: 32px; }", "ART-08"],
    ["ART-04 maiuscula", ".a { text-transform: uppercase; }", "ART-04"],
    ["ART-04 tracking", ".a { letter-spacing: 0.1em; }", "ART-04"],
    ["A11Y-09 outline none", ".a:focus { outline: none; }", "A11Y-09"],
    [
      "A11Y-32 conteudo so por hover",
      "@media (hover: hover) and (pointer: fine) { .tip:hover .x { opacity: 1; display: block; } }",
      "A11Y-32",
    ],
    [
      "A11Y-37 reduced-motion com rotacao",
      "@media (prefers-reduced-motion: reduce) { .s { transform: rotate(1turn); } }",
      "A11Y-37",
    ],
    ["A11Y-39 superficie translucida", ".p { background: rgba(255,255,255,0.7); }", "A11Y-39"],
    ["TYPE-MIN fonte 12px", ".a { font-size: 12px; }", "TYPE-MIN"],
    ["EXT fonte externa", "@import url('https://fonts.example/x.css');", "EXT"],
  ];

  it.each(cases)("detecta %s", (_name, css, expected) => {
    expect(ids(findCssViolations(css))).toContain(expected);
  });

  it("aceita CSS que segue as regras da casa", () => {
    const good = `
      :root { --radius-md: 6px; --shadow-menu: 0 1px 2px rgba(17,24,39,0.08); --color-scrim: rgba(17,24,39,0.6); }
      .button { min-height: var(--target-min); border-radius: var(--radius-md);
        transition: transform var(--duration-press) var(--ease-out), background-color 150ms ease; }
      .button:active { transform: scale(0.97); }
      @media (hover: hover) and (pointer: fine) { .button:hover { background-color: #1e40af; } }
      .account-menu__panel { box-shadow: var(--shadow-menu); }
      .dialog { box-shadow: var(--shadow-dialog); }
      .a:focus:not(:focus-visible) { outline: none; }
      @keyframes spin { to { transform: rotate(1turn); } }
      @media (prefers-reduced-motion: reduce) { .spinner { animation: none; } }
    `;
    expect(formatViolations(findCssViolations(good))).toBe("");
  });

  it("detecta emoji, lorem, Acme e eyebrow nos textos de producao", () => {
    const found = findTextViolations({
      "a.tsx": "const x = '\u{1F600}'",
      "b.tsx": "<p>Lorem ipsum</p>",
      "c.tsx": "<p>Acme Corp</p>",
      "d.css": ".eyebrow { }",
    });
    expect(found.map((v) => v.where).sort()).toEqual(["a.tsx", "b.tsx", "c.tsx", "d.css"]);
  });
});

describe("cssRules - varredura do CSS real do frontend", () => {
  const files = Object.entries(readAllCss());

  it("inclui src/design-system/tokens.css na varredura", () => {
    expect(
      files.map(([path]) => path),
      "o CSS de tokens precisa existir para a varredura valer (T-095 GREEN)",
    ).toContain("/src/design-system/tokens.css");
  });

  it("ART-01/03/04/06/07/08, A11Y-09/32/37/39: nenhum CSS viola as regras da casa", () => {
    const violations = files.flatMap(([path, css]) => findCssViolations(css, path));

    expect(formatViolations(violations)).toBe("");
  });

  it("ART-04: sem emoji, eyebrow, lorem ou Acme no codigo e nos textos de producao", () => {
    const sources = { ...readProductionSources(), ...Object.fromEntries(files) };

    expect(formatViolations(findTextViolations(sources))).toBe("");
  });

  it("ART-05: existe token ou classe com font-variant-numeric: tabular-nums", () => {
    const rules = files.flatMap(([, css]) => parseCss(css).rules);

    const hasTabular = rules.some((rule) =>
      rule.declarations.some(
        (d) => d.property === "font-variant-numeric" && d.value.toLowerCase() === "tabular-nums",
      ),
    );

    expect(hasTabular, "esperado uma regra com font-variant-numeric: tabular-nums").toBe(true);
  });

  it("ART-06/MOT-01: o botao base responde ao toque com :active scale(0.97) em 120ms --ease-out", () => {
    const rules = files.flatMap(([, css]) => parseCss(css).rules);

    const active = rules.find(
      (rule) => /(^|,)\s*button:active/.test(rule.selector) && rule.media.length === 0,
    );
    const base = rules.find(
      (rule) =>
        /(^|,)\s*button\s*(,|$)/.test(rule.selector) &&
        rule.declarations.some((d) => d.property === "transition"),
    );

    expect(active?.declarations).toContainEqual({ property: "transform", value: "scale(0.97)" });
    expect(base?.declarations.find((d) => d.property === "transition")?.value).toMatch(
      /transform var\(--duration-press\) var\(--ease-out\)/,
    );
  });

  it("ART-08/A11Y-36: controles base (button, input, select, textarea) tem min-height: var(--target-min)", () => {
    const rules = files.flatMap(([, css]) => parseCss(css).rules);

    for (const element of ["button", "input", "select", "textarea"]) {
      const rule = rules.find(
        (r) =>
          r.media.length === 0 &&
          r.selector.split(",").some((s) => s.trim() === element) &&
          r.declarations.some((d) => d.property === "min-height"),
      );
      expect(
        rule?.declarations.find((d) => d.property === "min-height")?.value,
        `min-height de '${element}'`,
      ).toBe("var(--target-min)");
    }
  });

  it("A11Y-38: prefers-contrast: more usa --color-text no texto de apoio", () => {
    const rules = files.flatMap(([, css]) => parseCss(css).rules);

    const rule = rules.find((r) => r.media.some((m) => /prefers-contrast:\s*more/.test(m)));

    expect(rule?.declarations).toContainEqual({
      property: "--color-text-muted",
      value: "var(--color-text)",
    });
  });
});
