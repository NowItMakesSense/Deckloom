// T-L01 / AC-L01a - docs/design-system.md v1.0 "Grimorio arcano" documenta o que o codigo faz, e so isso.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

import { readAllCss } from "../shared/testing/cssScan";

const doc = readFileSync(resolve(__dirname, "../../../docs/design-system.md"), "utf-8");
const plain = (text: string) =>
  text
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase();
const docPlain = plain(doc);
const tokensCss = readAllCss()["/src/design-system/tokens.css"] ?? "";
const definedTokens = new Set([...tokensCss.matchAll(/(--[a-z0-9-]+)\s*:/g)].map((m) => m[1]));

describe("docs/design-system.md v1.0 - identidade Grimorio arcano", () => {
  it("o cabecalho e o status declaram a v1.0 e a direcao Grimorio arcano para o app inteiro", () => {
    expect(doc).toMatch(/^# Design system do Deckloom \(v1\.0\)/m);
    expect(docPlain).toContain("grimorio arcano");
    expect(docPlain).toMatch(/status:[^\n]*v1\.0/);
    expect(docPlain).toContain("app inteiro");
  });

  it("registra a frase da ideia e o historico, com a v0.2 'ficha de colecao' descartada pelo usuario", () => {
    expect(docPlain).toMatch(/nesta pagina, .+ vira .+/);
    expect(docPlain).toMatch(/v0\.2[^\n]*descartad[^\n]*usuario/);
    expect(docPlain).toMatch(/- \*\*v1\.0\*\*/);
  });

  it.each([
    ["tokens do tema claro", /tema claro/],
    ["tokens do tema escuro", /tema escuro/],
    ["Cinzel Decorative", /cinzel decorative/],
    ["Albert Sans", /albert sans/],
    ["licenca OFL 1.1", /sil open font license|ofl 1\.1/],
    ["repositorio oficial do Cinzel", /github\.com\/ndiscover\/cinzel/],
    ["repositorio oficial do Albert Sans", /github\.com\/usted\/albert-sans/],
    ["vidro: onde usa", /onde o vidro vale/],
    ["vidro: limites", /limites do vidro/],
    [
      "vidro: fallback sem backdrop-filter",
      /fallback[^\n]*backdrop-filter|backdrop-filter[^\n]*fallback/,
    ],
    ["vidro: pior fundo", /pior fundo/],
    ["troca de tema: segue o sistema", /prefers-color-scheme/],
    ["troca de tema: data-theme", /data-theme/],
    ["troca de tema: localStorage", /localstorage/],
    ["troca de tema: CSP sem script inline", /script-src 'self'/],
    ["movimento: anime.js", /anime\.js/],
    ["movimento: versao fixa do animejs", /animejs 4\.\d+\.\d+/],
    ["ornamentos: moldura com cantos", /cantos ornamentados/],
    ["ornamentos: filete com florao", /florao/],
    ["ornamentos: runas originais", /runas originais/],
    ["ornamentos: selo de cera", /selo de cera/],
    [
      "texturas: pergaminho e noite estrelada",
      /pergaminho[^\n]*noite estrelada|noite estrelada[^\n]*pergaminho/,
    ],
    ["movimento: reduced motion", /prefers-reduced-motion/],
    ["cena 3D: three com versao fixa", /three 0\.\d+\.\d+/],
    ["cena 3D: fallback estatico", /fallback estatico/],
    ["cena 3D: devicePixelRatio limitado", /devicepixelratio/],
    ["selo Em breve", /selo "em breve"/],
    ["icones: lucide-animated", /lucide-animated/],
    ["excecoes as regras da skill", /excecoes as regras da skill/],
    ["excecao: glass", /excecao: glass|glass \(vidro\)/],
    ["excecao: violeta", /violeta/],
    ["excecao: textura de fantasia", /textura de fantasia/],
  ])("documenta %s", (_name, pattern) => {
    expect(docPlain).toMatch(pattern);
  });

  it("nao carrega mais o azul da v0.1, a landing em escopo proprio nem as fontes da v0.2", () => {
    expect(doc).not.toMatch(/#1D4ED8/i);
    expect(doc).not.toMatch(/--landing-/);
    expect(doc).not.toMatch(/Big Shoulders|IBM Plex|Imbue/);
    expect(docPlain).not.toMatch(/somente tema claro/);
    expect(docPlain).not.toMatch(/sem vidro nem desfoque/);
    expect(docPlain).not.toMatch(/primeiro jogo|seal--now|inicio do app/);
  });

  it("todo token citado entre crases no documento existe em tokens.css", () => {
    const cited = [...new Set([...doc.matchAll(/`(--[a-z0-9-]+)`/g)].map((m) => m[1]))];
    const missing = cited.filter((name) => !definedTokens.has(name));

    expect(cited.length).toBeGreaterThan(40);
    expect(missing).toEqual([]);
  });

  it("todo token de cor, vidro, aura, fonte e escala definido em tokens.css esta no documento", () => {
    const mustBeDocumented = [...definedTokens].filter((name) =>
      /^--(color|glass|aura|font|step|display|text-leading)/.test(name),
    );
    const undocumented = mustBeDocumented.filter((name) => !doc.includes(`\`${name}\``));

    expect(mustBeDocumented.length).toBeGreaterThan(40);
    expect(undocumented).toEqual([]);
  });

  it("cada valor hexadecimal dos temas aparece no documento (a tabela e o codigo nao divergem)", () => {
    const hexes = new Set(
      [...tokensCss.matchAll(/--(?:color|glass|aura)-[a-z-]+:\s*(#[0-9a-fA-F]{6})\s*;/g)].map((m) =>
        m[1].toLowerCase(),
      ),
    );
    const missing = [...hexes].filter((hex) => !doc.toLowerCase().includes(hex));

    expect(hexes.size).toBeGreaterThan(30);
    expect(missing).toEqual([]);
  });
});

describe("docs/design-system.md v1.0 - revisao v5 do usuario (marca, navegacao, rodape, mana e logos)", () => {
  it.each([
    ["navegacao dynamic island", /dynamic island/],
    ["pilula flutuante centralizada", /pilula flutuante/],
    ["botao primario com icone", /botao primario[^\n]*icone|icone[^\n]*botao primario/],
    ["logo: favicon", /favicon\.svg/],
    ["logo: arquivos em public/brand/deckloom", /public\/brand\/deckloom/],
    ["rodape refeito", /rodape/],
    ["mana-font com versao fixa", /mana-font 1\.\d+\.\d+/],
    ["fonte Mana: licencas", /andrew gioia/],
    ["logos oficiais dos jogos", /logos oficiais/],
    ["nota de marcas", /nao e afiliado nem endossado/],
    ["nota de marcas provisoria, sujeita a revisao juridica", /nota de marcas[^\n]*provisori/],
    ["risco registrado", /risk-feat-landing-6/],
    ["revisao juridica", /revisao juridica/],
  ])("documenta %s", (_name, pattern) => {
    expect(docPlain).toMatch(pattern);
  });

  it("nao diz mais que o Deckloom nao usa marca nem simbolo dos jogos", () => {
    expect(docPlain).not.toMatch(
      /sem arte nem marcas dos jogos|nada vem de arte, simbolo ou marca/,
    );
    expect(docPlain).not.toMatch(/faixa final \(`\/cadastro`/);
  });

  it("nao contradiz o REQ-L01 v3 nem o REQ-L03 v5 (sem faixa final de CTA, navegacao em pilula)", () => {
    expect(docPlain).not.toMatch(/faixa de chamada final/);
    expect(docPlain).not.toMatch(/sem forma de pilula/);
  });

  it("o shell descreve a navegacao como a pilula flutuante, sem barra de ponta a ponta", () => {
    expect(docPlain).not.toMatch(/barra superior de 56px/);
    expect(docPlain).not.toMatch(/barra de navegacao \(de ponta a ponta/);
  });

  it("respiro e tamanho minimo da logo estao pendentes de decisao do usuario, sem valores inventados", () => {
    const section = doc.slice(doc.indexOf("### 1.13"), doc.indexOf("## 2. "));
    const plainSection = plain(section);

    expect(plainSection).toMatch(/area de respiro[^\n]*pendente de decisao do usuario/);
    expect(plainSection).toMatch(/tamanho minimo[^\n]*pendente de decisao do usuario/);
    expect(plainSection).not.toMatch(/\b(15|24|32)px/);
  });
});

describe("docs/design-system.md - marcacao das tabelas", () => {
  it("toda linha de tabela tem o mesmo numero de celulas do cabecalho (nenhum paragrafo engolido)", () => {
    const cells = (line: string) =>
      line
        .trim()
        .replace(/^\||\|$/g, "")
        .split(/(?<!\\)\|/).length;
    const offenders: string[] = [];
    let header = 0;
    doc.split(/\r?\n/).forEach((line, i) => {
      if (!line.startsWith("|")) {
        header = 0;
        return;
      }
      if (header === 0) header = cells(line);
      else if (cells(line) !== header) offenders.push(`linha ${i + 1}: ${line.slice(0, 60)}`);
    });

    expect(offenders).toEqual([]);
  });
});
