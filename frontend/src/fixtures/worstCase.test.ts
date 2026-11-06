import { describe, expect, it } from "vitest";

import { graphemeLength } from "@/shared/validation/graphemeLength";

import { WORST_FIXTURE_NAMES, getWorstFixture } from "./worstFixtures";
import { loadWorstFixture, resolveWorstFixtureName } from "./worstMode";

// T-130 / design A.8.5 / WC-16. O build de producao (varredura de dist/) e coberto por teste proprio.

describe("T-130 fixtures A.8.5", () => {
  it("expoe as nove fixtures nomeadas por A.8.5", () => {
    expect([...WORST_FIXTURE_NAMES]).toEqual([
      "vazio",
      "um",
      "enorme",
      "longo",
      "curto",
      "faltando",
      "estranho",
      "lento",
      "erro",
    ]);
  });

  it("vazio: sem bio, sem formatos selecionados, catalogo vazio", () => {
    const f = getWorstFixture("vazio");
    expect(f.bio ?? "").toBe("");
    expect(f.selectedFormats).toHaveLength(0);
    expect(f.catalogFormats).toHaveLength(0);
  });

  it("um: 1 formato e nome de 3 elementos de texto", () => {
    const f = getWorstFixture("um");
    expect(f.selectedFormats).toHaveLength(1);
    expect(graphemeLength(f.displayName)).toBe(3);
  });

  it("curto: nome Ana", () => {
    expect(getWorstFixture("curto").displayName).toBe("Ana");
  });

  it("enorme: 50 formatos no catalogo, bio de 280, email de 254, senha de 128", () => {
    const f = getWorstFixture("enorme");
    expect(f.catalogFormats).toHaveLength(50);
    expect(graphemeLength(f.bio ?? "")).toBe(280);
    expect(f.email).toHaveLength(254);
    expect(f.password).toHaveLength(128);
  });

  it("longo: nome de 30 elementos de texto dentro do limite de 3 a 30", () => {
    const f = getWorstFixture("longo");
    expect(graphemeLength(f.displayName)).toBe(30);
  });

  it("longo: nome de 30 emojis compostos (juncao de largura zero) conta 30 elementos", () => {
    const f = getWorstFixture("longo");
    expect(f.displayName).toContain("‍");
    expect(f.displayName.length).toBeGreaterThan(30);
  });

  it("longo: bio com uma palavra de 280 caracteres, sem espacos", () => {
    const bio = getWorstFixture("longo").bio ?? "";
    expect(bio).toHaveLength(280);
    expect(bio).not.toMatch(/\s/);
  });

  it("longo: nome de arquivo de upload com 200 caracteres", () => {
    expect(getWorstFixture("longo").uploadFileName).toHaveLength(200);
  });

  it("longo: ao menos um nome de formato com 60 caracteres", () => {
    const f = getWorstFixture("longo");
    expect(f.catalogFormats.some((n) => n.length === 60)).toBe(true);
  });

  it("estranho: marcacao digitada fica como texto literal em nome e bio", () => {
    const f = getWorstFixture("estranho");
    const texto = `${f.displayName} ${f.bio ?? ""}`;
    expect(texto).toContain("<b>x</b>");
    expect(texto).toContain("&amp;");
    expect(texto).toContain("**texto**");
  });

  it("faltando: todos os opcionais ausentes e avatar que nao chega", () => {
    const f = getWorstFixture("faltando");
    expect(f.bio).toBeUndefined();
    expect(f.selectedFormats).toHaveLength(0);
    expect(f.uploadFileName).toBeUndefined();
    expect(f.avatarUrl).toBeDefined();
    expect(f.failures).not.toContain("http500");
  });

  it("lento: respostas de 3 s, 12 s e estouro de 20 s", () => {
    expect(getWorstFixture("lento").responseDelaysMs).toEqual([3000, 12000, 20000]);
  });

  it("erro: 500, rede fora e 401 no meio da edicao", () => {
    expect(getWorstFixture("erro").failures).toEqual(["http500", "network", "http401"]);
  });
});

describe("T-130 ativacao por ?data=worst somente em desenvolvimento", () => {
  it("em desenvolvimento, ?data=worst&fixture=<nome> ativa a fixture", () => {
    expect(resolveWorstFixtureName("?data=worst&fixture=longo", true)).toBe("longo");
  });

  it("em desenvolvimento, carrega os dados da fixture escolhida", async () => {
    const loaded = await loadWorstFixture("?data=worst&fixture=curto", true);
    expect(loaded?.displayName).toBe("Ana");
  });

  it("em producao (DEV falso), ?data=worst e ignorado e nada e carregado", async () => {
    expect(resolveWorstFixtureName("?data=worst&fixture=longo", false)).toBeNull();
    expect(await loadWorstFixture("?data=worst&fixture=longo", false)).toBeNull();
  });

  it("sem a flag, o comportamento normal e preservado (nenhuma fixture)", async () => {
    expect(resolveWorstFixtureName("", true)).toBeNull();
    expect(resolveWorstFixtureName("?data=other&fixture=longo", true)).toBeNull();
    expect(await loadWorstFixture("?foo=bar", true)).toBeNull();
  });
});
