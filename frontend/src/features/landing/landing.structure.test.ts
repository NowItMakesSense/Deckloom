// T-L02 / AC-L02d, AC-L02g, AC-L02h (REQ-L12, REQ-L10, REQ-L03): estrutura da landing que so o codigo-fonte mostra:
// o three so no chunk do palco (fora do bundle inicial), pacotes removidos ausentes, dependencias com versao exata,
// paleta unica e sem tonemapping, docs, favicon, SVG dos logos sem script e nenhuma URL externa.
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { relative, resolve } from "node:path";
import { describe, expect, it } from "vitest";

import { findCssViolations } from "@/shared/testing/cssChecks";
import { parseCss, readAllCss, readProductionSources } from "@/shared/testing/cssScan";

const FRONTEND = resolve(__dirname, "../../..");
const REPO = resolve(FRONTEND, "..");
const read = (path: string) => readFileSync(resolve(FRONTEND, path), "utf-8");

const REMOVED = ["three-stdlib", "@react-three/fiber", "@shadergradient/react", "camera-controls"];
const importOf = (libs: string[]) =>
  new RegExp(
    `^\\s*(import|export)\\s+(?!type\\b)[^;]*?from\\s+["'](${libs.map((l) => l.replace("/", "\\/")).join("|")})(/[^"']*)?["']`,
    "m",
  );
const STAGE_ENGINE = /\/features\/landing\/stage\/engine\//;

describe("three fora do bundle inicial (AC-L02d, REQ-L12)", () => {
  const sources = Object.entries(readProductionSources());

  it("so o chunk do palco (stage/engine) importa o three", () => {
    const offenders = sources
      .filter(([path]) => !STAGE_ENGINE.test(path) && !/\.test\./.test(path))
      .filter(([, text]) => importOf(["three"]).test(text))
      .map(([path]) => path);

    expect(offenders).toEqual([]);
  });

  it("o chunk do palco existe e realmente importa o three (a regra acima nao e vazia)", () => {
    const engine = sources.filter(([path]) => STAGE_ENGINE.test(path) && !/\.test\./.test(path));

    expect(engine.length).toBeGreaterThan(3);
    expect(engine.map(([, text]) => text).join("\n")).toContain('from "three"');
  });

  it("o palco entra por import dinamico e ninguem importa o createStage de forma estatica", () => {
    const stage = sources.find(([path]) => path.endsWith("/stage/Stage.tsx"))?.[1] ?? "";

    expect(stage).toMatch(/import\(\s*["']\.\/engine\/createStage["']\s*\)/);
    const staticImporters = sources
      .filter(([path]) => !/\.test\./.test(path))
      .filter(([, text]) => /^\s*import\s[^;]*from\s+["'][^"']*createStage["']/m.test(text))
      .map(([path]) => path);
    expect(staticImporters).toEqual([]);
  });

  it("nenhum arquivo de producao importa os pacotes removidos", () => {
    const offenders = sources
      .filter(([, text]) => importOf(REMOVED).test(text))
      .map(([path]) => path);

    expect(offenders).toEqual([]);
  });

  it("a decisao de montar o palco (capabilities) fica no bundle inicial, sem three", () => {
    const capabilities = sources.find(([path]) => path.endsWith("/stage/capabilities.ts"))?.[1];

    expect(capabilities).toBeDefined();
    expect(capabilities).not.toMatch(/from\s+["']three/);
  });
});

describe("o palco: paleta unica, sem tonemapping, recursos liberados (AC-L02h, AC-L02d)", () => {
  const stageFiles = (names: string[]) =>
    names.map((name) => read(`src/features/landing/stage/${name}`));

  it("as cores da floresta, do ceu, da caixa e dos tokens vem de palette.ts (nenhum hexadecimal solto)", () => {
    const files = [
      "engine/world.ts",
      "engine/createStage.ts",
      "engine/dof.ts",
      "silhouettes.ts",
      "clearing.ts",
      "tokens.ts",
      "StaticClearing.tsx",
      "Stage.tsx",
      "board.ts",
      "fire.ts",
      "mist.ts",
      "engine/campfire.ts",
      "engine/noticeBoard.ts",
      "engine/boardTextures.ts",
      "engine/flame.ts",
    ];
    for (const [i, text] of stageFiles(files).entries()) {
      const withoutComments = text.replace(/\/\/.*$/gm, "").replace(/\/\*[\s\S]*?\*\//g, "");
      expect(withoutComments.match(/#[0-9a-fA-F]{6}\b/g) ?? [], files[i]).toEqual([]);
    }
    const palette = read("src/features/landing/stage/palette.ts");
    expect(palette).toMatch(/export const FOREST/);
    // a noite, o fogo e o mural tambem moram na paleta
    expect(palette).toMatch(/export const NIGHT = 0\.\d+/);
    expect(palette).toMatch(/export const FIRE/);
    expect(palette).toMatch(/export const BOARD/);
  });

  it("o renderer fica sem tonemapping (o ACES lavava as silhuetas) e nenhum outro tonemapping existe", () => {
    const engine = stageFiles(["engine/createStage.ts", "engine/world.ts", "engine/dof.ts"]).join(
      "\n",
    );

    expect(engine).toContain("NoToneMapping");
    expect(engine).not.toMatch(
      /ACESFilmic|Reinhard|Cineon|AgX|NeutralToneMapping|LinearToneMapping/,
    );
    expect(engine).toContain("#include <colorspace_fragment>");
  });

  it("o laco de quadros para ao desmontar e libera geometrias, materiais, texturas, alvo de profundidade e renderer", () => {
    const stage = read("src/features/landing/stage/engine/createStage.ts");

    expect(stage).toContain("cancelAnimationFrame(");
    expect(stage).toContain("disposeObject3D(");
    expect(stage).toMatch(/dof\.dispose\(\)/);
    expect(stage).toMatch(/renderer\.dispose\(\)/);
    expect(read("src/features/landing/stage/engine/dof.ts")).toMatch(/target\.dispose\(\)/);
  });

  it("a Stage usa so uma camada de WebGL: o canvas do palco (aria-hidden)", () => {
    expect(read("src/features/landing/stage/engine/createStage.ts")).toContain(
      '"aria-hidden", "true"',
    );
  });
});

describe("dependencias com versao exata (AC-L02h, REQ-L12)", () => {
  const pkg = JSON.parse(read("package.json")) as {
    dependencies: Record<string, string>;
    devDependencies: Record<string, string>;
  };
  const lock = JSON.parse(read("package-lock.json")) as { packages: Record<string, unknown> };

  it.each([
    ["three", "0.186.1"],
    ["animejs", "4.5.0"],
  ])("%s esta nas dependencias em %s, sem ^ nem ~", (name, version) => {
    expect(pkg.dependencies[name]).toBe(version);
  });

  it("@types/three fica nas dependencias de desenvolvimento, com versao exata", () => {
    expect(pkg.devDependencies["@types/three"]).toMatch(/^\d+\.\d+\.\d+$/);
    expect(pkg.dependencies["@types/three"]).toBeUndefined();
  });

  it.each(REMOVED)("%s saiu do package.json e do package-lock.json", (name) => {
    expect(pkg.dependencies[name]).toBeUndefined();
    expect(pkg.devDependencies[name]).toBeUndefined();
    expect(lock.packages[`node_modules/${name}`]).toBeUndefined();
  });

  it("os avisos de terceiros listam o three e nao citam os pacotes removidos", () => {
    const notices = read("THIRD_PARTY_NOTICES.md");

    expect(notices).toContain("three 0.186.1");
    for (const name of REMOVED) expect(notices, name).not.toContain(name);
  });
});

describe("docs/design-system.md documenta o palco, a floresta, a deck box, o altar e os fallbacks (AC-L02h)", () => {
  const doc = readFileSync(resolve(REPO, "docs/design-system.md"), "utf-8");

  it.each([
    [/^### 1\.12 .*[Pp]alco/m, "secao do palco WebGL"],
    [/^### 1\.14 .*[Cc]ards? de gancho/m, "secao dos cards de gancho"],
    [/^### 1\.15 .*[Pp]lanos/m, "secao dos planos"],
    [/^#### .*[Ff]allback/m, "subsecao do fallback"],
  ])("tem %s", (pattern) => {
    expect(doc).toMatch(pattern);
  });

  it.each([
    "palco WebGL único",
    "floresta élfica",
    "perspectiva atmosférica",
    "deck box",
    "altar",
    "runas",
    "ganchos",
    "mesa",
    "epílogo",
    "planos",
    "profundidade de campo",
    "sem tonemapping",
    "sem copiar a moldura",
    "prefers-reduced-motion",
    "funções puras",
    "noite",
    "velas",
    "tochas",
    "chama procedural",
    "mural de avisos",
    "pan final",
    "véu de névoa",
  ])("cita %s", (text) => {
    expect(doc.toLowerCase()).toContain(text.toLowerCase());
  });

  it("nao cita mais os pacotes removidos como parte da landing", () => {
    const section = doc.slice(doc.indexOf("### 1.12"), doc.indexOf("### 1.13"));

    for (const name of REMOVED) expect(section, name).not.toContain(name);
  });
});

describe("marca no HTML (AC-L02g)", () => {
  const html = read("index.html");

  it("o favicon e o simbolo do Deckloom, local", () => {
    expect(html).toMatch(/<link[^>]*rel="icon"[^>]*href="\/favicon\.svg"/);
    expect(existsSync(resolve(FRONTEND, "public/favicon.svg"))).toBe(true);
  });

  it("o CSS dos simbolos de mana e local", () => {
    expect(html).toMatch(/<link[^>]*rel="stylesheet"[^>]*href="\/brand\/mana\/mana\.css"/);
  });
});

describe("SVG dos logos dos jogos: sanitizados (sem script, sem handler, sem href externo)", () => {
  const dir = resolve(FRONTEND, "public/brand");
  const svgs = (folder: string): string[] =>
    readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
      const full = resolve(folder, entry.name);
      if (entry.isDirectory()) return svgs(full);
      return entry.name.endsWith(".svg") ? [full] : [];
    });
  const files = svgs(dir);

  it("ha SVGs de logos para verificar", () => {
    expect(files.map((f) => relative(dir, f).replaceAll("\\", "/"))).toEqual(
      expect.arrayContaining([
        "games/magic-the-gathering.svg",
        "games/pokemon-tcg.svg",
        "deckloom/deckloom-logo.svg",
      ]),
    );
  });

  it.each(files.map((f) => [relative(dir, f).replaceAll("\\", "/"), f] as const))(
    "%s",
    (_name, file) => {
      const svg = readFileSync(file, "utf-8");

      expect(svg).not.toMatch(/<script/i);
      expect(svg).not.toMatch(/\son[a-z]+\s*=/i);
      expect(svg).not.toMatch(/<foreignObject/i);
      expect(svg).not.toMatch(/javascript:/i);
      // Unico endereco permitido: os identificadores de namespace do proprio SVG.
      const withoutNamespaces = svg.replace(
        /xmlns(:\w+)?\s*=\s*["']http:\/\/www\.w3\.org\/[^"']*["']/g,
        "",
      );
      // href so para ancora interna ou imagem embutida (data:image): nada que o navegador busque na rede.
      const hrefs = [...withoutNamespaces.matchAll(/(?:xlink:)?href\s*=\s*["']([^"']*)["']/gi)].map(
        (m) => m[1],
      );
      for (const href of hrefs)
        expect(
          href.startsWith("#") || /^data:image\/(png|jpe?g|webp);base64,/.test(href),
          href.slice(0, 40),
        ).toBe(true);
      expect(
        withoutNamespaces.replace(/data:image\/[a-z]+;base64,[A-Za-z0-9+/=]+/g, ""),
      ).not.toMatch(/https?:\/\//i);
      expect(withoutNamespaces).not.toMatch(/url\(\s*["']?(?!#)/i);
    },
  );
});

describe("nenhuma URL externa no codigo da landing (LGPD)", () => {
  it("fontes de producao da landing e do roteador nao citam http(s)://", () => {
    const offenders = Object.entries(readProductionSources())
      .filter(([path]) => /\/features\/landing\/|\/app\//.test(path))
      .filter(([, text]) => /https?:\/\//i.test(text))
      .map(([path]) => path);

    expect(offenders).toEqual([]);
  });

  it("o CSS da landing nao referencia URL externa", () => {
    const css = Object.entries(readAllCss()).filter(([path]) =>
      path.includes("/features/landing/"),
    );

    expect(css.length).toBeGreaterThan(0);
    for (const [path, text] of css)
      expect(text, path).not.toMatch(/https?:\/\/|url\(\s*["']?\/\//i);
  });
});

describe("vidro da landing (docs/design-system.md 1.6 e secao 5)", () => {
  const landingCss = Object.entries(readAllCss()).filter(([path]) =>
    path.includes("/features/landing/"),
  );

  it("todo CSS da landing que usa backdrop-filter tem o solido de reserva em @supports not", () => {
    const users = landingCss.filter(([, css]) => css.includes("backdrop-filter"));

    expect(users.length).toBeGreaterThan(0);
    for (const [path, css] of users) {
      const fallback = parseCss(css).rules.filter((rule) =>
        rule.media.some((m) => /@supports\s+not\s*\(+\s*(-webkit-)?backdrop-filter/.test(m)),
      );
      expect(fallback.length, `${path}: sem @supports not (backdrop-filter)`).toBeGreaterThan(0);
      expect(JSON.stringify(fallback), path).toContain("--glass-solid");
    }
  });

  it("fora o vidro e o fundo de fantasia (excecoes documentadas), o CSS da landing segue as regras da casa", () => {
    const violations = landingCss.flatMap(([path, css]) => findCssViolations(css, path));
    const unexpected = violations.filter(
      (v) => !(v.id === "ART-01" && /backdrop-filter/.test(v.message)),
    );

    expect(unexpected.map((v) => `[${v.id}] ${v.where} - ${v.message}`)).toEqual([]);
  });
});
