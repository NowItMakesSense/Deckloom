// T-095 - varredura de TODO o CSS do frontend (design.md A.8.7: ART-01/03/04/05/06/07/08, A11Y-09/32/37/38/39).
// Vale para todas as tarefas seguintes: qualquer CSS novo em src/ entra na varredura.
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { relative, resolve } from "node:path";
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
    const violations = files
      .flatMap(([path, css]) => findCssViolations(css, path))
      // Excecao documentada (docs/design-system.md secao 5, "glass"): o vidro usa backdrop-filter. A regra
      // abaixo exige o solido de reserva em todo arquivo que o usa; qualquer outro ART-01 continua valendo.
      .filter((v) => !(v.id === "ART-01" && v.message === "backdrop-filter"));

    expect(formatViolations(violations)).toBe("");
  });

  it("vidro (excecao 'glass'): todo CSS com backdrop-filter tem o fundo solido de reserva em @supports not", () => {
    const users = files.filter(([, css]) => /backdrop-filter/.test(css));

    for (const [path, css] of users) {
      const fallback = parseCss(css).rules.filter((rule) =>
        rule.media.some((m) => /@supports\s+not\s*\(+\s*(-webkit-)?backdrop-filter/.test(m)),
      );
      expect(fallback.length, `${path}: falta @supports not (backdrop-filter)`).toBeGreaterThan(0);
      expect(JSON.stringify(fallback), path).toContain("--glass-solid");
    }
  });

  it("o verificador ainda pega backdrop-filter e gradiente fora da excecao (fixtures)", () => {
    expect(ids(findCssViolations(".a { backdrop-filter: blur(8px); }"))).toContain("ART-01");
    expect(ids(findCssViolations(".a { background: linear-gradient(#fff, #eee); }"))).toContain(
      "ART-01",
    );
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

describe("cssRules - sem recurso de terceiros e fontes proprias (LGPD, T-L01)", () => {
  const files = Object.entries(readAllCss());
  const publicDir = resolve(__dirname, "../public");
  const external = /(@import[^;]*|url\(\s*|src\s*:[^;]*|image-set\([^)]*?)["']?\s*(https?:|\/\/)/i;
  const withoutComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, "");
  const externalOffenders = (entries: Array<[string, string]>) =>
    entries.filter(([, css]) => external.test(withoutComments(css))).map(([path]) => path);
  const cssUnder = (dir: string): Array<[string, string]> =>
    readdirSync(dir, { withFileTypes: true }).flatMap((entry): Array<[string, string]> => {
      const full = resolve(dir, entry.name);
      if (entry.isDirectory()) return cssUnder(full);
      return entry.name.endsWith(".css")
        ? [[relative(publicDir, full).replaceAll("\\", "/"), readFileSync(full, "utf-8")]]
        : [];
    });

  it("nenhum CSS referencia URL externa em @import, url() ou src", () => {
    const offenders = files.filter(([, css]) =>
      external.test(css.replace(/\/\*[\s\S]*?\*\//g, "")),
    );

    expect(offenders.map(([path]) => path)).toEqual([]);
  });

  it("nenhum CSS de public/ (por exemplo brand/mana/mana.css) referencia URL externa", () => {
    const publicCss = cssUnder(publicDir);

    expect(publicCss.map(([path]) => path)).toContain("brand/mana/mana.css");
    expect(externalOffenders(publicCss)).toEqual([]);
  });

  it("a varredura de public/ falha com URL externa (caso de regressao sobre CSS de exemplo)", () => {
    const bad: Array<[string, string]> = [
      ["brand/x/a.css", ".a{background:url(https://x.test/a.png)}"],
      ["brand/x/b.css", '.b{background:image-set("https://x.test/b.png" 1x)}'],
      ["brand/x/c.css", '.c{background:image-set("ok.png" 1x, "//x.test/c.png" 2x)}'],
    ];
    const good: Array<[string, string]> = [
      ["brand/x/d.css", '.d{background:url("/brand/d.png")} /* https://x.test e so comentario */'],
    ];

    expect(externalOffenders([...bad, ...good])).toEqual(bad.map(([path]) => path));
  });

  it("o detector de URL externa reconhece as tres formas", () => {
    for (const css of [
      "@import 'https://x.test/a.css';",
      "@import url(//x.test/a.css);",
      ".a{background:url(http://x.test/a.png)}",
      "@font-face{src: local(x), url('https://x.test/f.woff2')}",
      '.a{background:image-set("https://x.test/a.png" 1x)}',
    ]) {
      expect(external.test(css), css).toBe(true);
    }
  });

  it("cada @font-face aponta para arquivo existente em public/fonts e a familia tem OFL.txt", () => {
    const faces = files.flatMap(([path, css]) =>
      [...css.matchAll(/@font-face\s*{[^}]*}/g)].map((m) => ({ path, block: m[0] })),
    );

    expect(faces.length, "esperado ao menos um @font-face (fonts.css)").toBeGreaterThan(0);
    for (const { block } of faces) {
      const urls = [...block.matchAll(/url\(\s*["']?([^"')]+)["']?\s*\)/g)].map((m) => m[1]);
      expect(urls.length, block).toBeGreaterThan(0);
      for (const url of urls) {
        expect(url.startsWith("/fonts/"), `url fora de /fonts: ${url}`).toBe(true);
        expect(existsSync(resolve(publicDir, url.slice(1))), `arquivo ausente: ${url}`).toBe(true);
        const family = url.split("/")[2];
        expect(
          existsSync(resolve(publicDir, "fonts", family, "OFL.txt")),
          `OFL.txt de ${family}`,
        ).toBe(true);
      }
      expect(block).toMatch(/font-display:\s*swap/);
    }
  });

  it("as fontes do Grimorio arcano (Cinzel Decorative e Albert Sans) estao no repositorio com a licenca OFL 1.1", () => {
    const fonts: Array<[string, string, string]> = [
      ["cinzel-decorative", "CinzelDecorative-700-latin.woff2", "Natanael Gama"],
      ["cinzel-decorative", "CinzelDecorative-900-latin.woff2", "Natanael Gama"],
      ["albert-sans", "AlbertSans-var-latin.woff2", "The Albert Sans Project Authors"],
    ];
    for (const [dir, file, holder] of fonts) {
      const woff2 = readFileSync(resolve(publicDir, "fonts", dir, file));
      expect(woff2.subarray(0, 4).toString("latin1"), `${file} nao e woff2`).toBe("wOF2");
      expect(woff2.length, file).toBeGreaterThan(10_000);
      const licence = readFileSync(resolve(publicDir, "fonts", dir, "OFL.txt"), "utf-8");
      expect(licence).toContain(holder);
      expect(licence).toMatch(/SIL OPEN FONT LICENSE Version 1\.1/i);
    }
  });

  it("as fontes das direcoes descartadas (Imbue, Big Shoulders, IBM Plex) foram removidas", () => {
    for (const dir of ["imbue", "big-shoulders-display", "ibm-plex-sans", "ibm-plex-mono"]) {
      expect(existsSync(resolve(publicDir, "fonts", dir)), dir).toBe(false);
    }
  });
});

describe("anime.js e three: dependencias com versao exata (REQ-L09, REQ-L10)", () => {
  const frontendDir = resolve(__dirname, "..");
  const pkg = JSON.parse(readFileSync(resolve(frontendDir, "package.json"), "utf-8")) as {
    dependencies: Record<string, string>;
  };

  it("animejs esta nas dependencias com versao exata (sem ^ nem ~)", () => {
    expect(pkg.dependencies.animejs).toMatch(/^\d+\.\d+\.\d+$/);
  });

  it("three esta nas dependencias com versao exata (sem ^ nem ~)", () => {
    expect(pkg.dependencies.three).toMatch(/^0\.\d+\.\d+$/);
  });
});

describe("arquivos de marca de terceiros: fora do lint e do prettier", () => {
  const frontendDir = resolve(__dirname, "..");

  it("o ESLint e o Prettier ignoram public/brand (logos e simbolos de terceiros)", () => {
    const eslint = readFileSync(resolve(frontendDir, "eslint.config.js"), "utf-8");
    const prettierIgnore = readFileSync(resolve(frontendDir, ".prettierignore"), "utf-8")
      .split(/\r?\n/)
      .map((l) => l.trim());

    expect(eslint).toMatch(/ignores:\s*\[[^\]]*"public\/brand"/);
    expect(prettierIgnore).toEqual(expect.arrayContaining(["public/brand"]));
  });
});

describe("previa estatica aposentada (APR-045, AC-L01i)", () => {
  const frontendDir = resolve(__dirname, "..");
  const repoDir = resolve(frontendDir, "..");
  const configs = [
    "eslint.config.js",
    ".prettierignore",
    "vite.config.ts",
    "package.json",
    "index.html",
    "tsconfig.json",
  ];

  it("frontend/preview nao existe mais", () => {
    expect(existsSync(resolve(frontendDir, "preview"))).toBe(false);
  });

  it("nenhum arquivo de configuracao do frontend referencia a previa", () => {
    for (const file of configs) {
      const text = readFileSync(resolve(frontendDir, file), "utf-8");

      expect(text, file).not.toMatch(/preview/i);
    }
  });

  it("o doc do design system nao referencia mais a previa estatica", () => {
    const doc = readFileSync(resolve(repoDir, "docs/design-system.md"), "utf-8");

    expect(doc).not.toMatch(/frontend\/preview|preview\/vendor|preview\.css|scene-gate\.js/);
    expect(doc).not.toMatch(/^## .*Pr[eé]via/m);
    expect(doc).not.toMatch(/a pr[eé]via/i);
  });

  it("nenhum arquivo de texto de public/brand (css, md, txt) referencia a previa", () => {
    const textFiles = (dir: string): string[] =>
      readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
        const full = resolve(dir, entry.name);
        if (entry.isDirectory()) return textFiles(full);
        return /\.(css|md|txt)$/.test(entry.name) ? [full] : [];
      });
    const files = textFiles(resolve(frontendDir, "public/brand"));

    expect(files.length).toBeGreaterThanOrEqual(5);
    for (const file of files) {
      expect(readFileSync(file, "utf-8"), file).not.toMatch(/preview|pr[eé]via/i);
    }
  });

  it("o mock do deck e o seu teste moram em src/features/landing/mock", () => {
    const mockDir = resolve(frontendDir, "src/features/landing/mock");

    expect(existsSync(resolve(mockDir, "deck-mock.ts"))).toBe(true);
    expect(existsSync(resolve(mockDir, "deck-mock.test.ts"))).toBe(true);
  });
});
