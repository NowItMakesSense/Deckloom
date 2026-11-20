// T-L01 / AC-L01f e T-L02 / AC-L02f: os numeros do mock do deck fecham: tem + faltam = total, antes e depois do "Tenho +1".
import { describe, expect, it } from "vitest";

import {
  DECK,
  type Deck,
  META,
  PRICE_ALERT,
  SHOPS,
  addOne,
  cardCost,
  formatBRL,
  manaLabel,
  manaSymbols,
  shopQuote,
  summarize,
} from "./deck-mock";

function expectInvariants(deck: Deck) {
  const s = summarize(deck);
  expect(s.have + s.missing).toBe(s.total);
  expect(s.complete.copies + s.incomplete.reduce((sum, c) => sum + c.have, 0)).toBe(s.have);
  expect(s.incomplete.reduce((sum, c) => sum + c.missing, 0)).toBe(s.missing);
  expect(s.complete.copies + s.incomplete.reduce((sum, c) => sum + c.need, 0)).toBe(s.total);
  for (const card of deck.cards) {
    expect(card.have).toBeGreaterThanOrEqual(0);
    expect(card.have).toBeLessThanOrEqual(card.need);
  }
  return s;
}

describe("mock do grimorio (hero): os numeros fecham", () => {
  it("o deck de exemplo tem 60 cartas, voce ja tem 47 e faltam 13", () => {
    const s = expectInvariants(DECK);

    expect(s.total).toBe(60);
    expect(s.have).toBe(47);
    expect(s.missing).toBe(13);
    expect(s.incomplete.length).toBeGreaterThanOrEqual(4);
    expect(s.complete.cards).toBeGreaterThanOrEqual(5);
  });

  it("cada 'Tenho +1' soma 1 ao que voce tem, tira 1 do que falta e mantem o total, ate o deck fechar", () => {
    let deck = DECK;
    let before = summarize(deck);
    let steps = 0;
    while (before.incomplete.length > 0) {
      deck = addOne(deck, before.incomplete[0].name);
      const after = expectInvariants(deck);
      expect(after.have).toBe(before.have + 1);
      expect(after.missing).toBe(before.missing - 1);
      expect(after.total).toBe(before.total);
      before = after;
      steps += 1;
    }

    expect(steps).toBe(13);
    expect(before.have).toBe(60);
    expect(before.complete.copies).toBe(60);
  });

  it("'Tenho +1' numa carta completa ou desconhecida nao muda nada e nao altera o deck original", () => {
    const complete = DECK.cards.find((c) => c.have === c.need)!;

    expect(summarize(addOne(DECK, complete.name))).toEqual(summarize(DECK));
    expect(summarize(addOne(DECK, "Carta que nao existe"))).toEqual(summarize(DECK));
    addOne(DECK, summarize(DECK).incomplete[0].name);
    expect(summarize(DECK).have).toBe(47);
  });

  it("nomes consistentes: cartas unicas e o nome do deck nao se confunde com o de nenhuma carta", () => {
    const names = DECK.cards.map((c) => c.name);
    const stem = (text: string) => text.toLowerCase().split(/\s+/)[0].replace(/s$/, "");

    expect(new Set(names).size).toBe(names.length);
    for (const name of names) {
      expect(stem(name)).not.toBe(stem(DECK.name));
    }
  });
});

describe("lojas parceiras: a cotacao soma so o que falta", () => {
  it("cada loja cota todas as cartas que faltam, na quantidade que falta, e o total e a soma das linhas", () => {
    const s = summarize(DECK);
    for (const shop of SHOPS) {
      const quote = shopQuote(DECK, shop);
      expect(quote.lines.map((l) => l.name)).toEqual(s.incomplete.map((c) => c.name));
      expect(quote.lines.reduce((sum, l) => sum + l.qty, 0)).toBe(s.missing);
      for (const line of quote.lines) expect(line.subtotal).toBe(line.qty * line.unit);
      expect(quote.total).toBe(quote.lines.reduce((sum, l) => sum + l.subtotal, 0));
    }
  });

  it("depois de 'Tenho +1' a cotacao acompanha (uma copia a menos)", () => {
    const name = summarize(DECK).incomplete[0].name;
    const shop = SHOPS[0];
    const before = shopQuote(DECK, shop);
    const after = shopQuote(addOne(DECK, name), shop);

    expect(after.lines.reduce((sum, l) => sum + l.qty, 0)).toBe(12);
    expect(before.total - after.total).toBe(shop.prices[name]);
  });

  it("o alerta de queda de preco usa o preco que a tabela mostra e uma carta que falta", () => {
    const shop = SHOPS.find((s) => s.name === PRICE_ALERT.shop);

    expect(shop?.prices[PRICE_ALERT.card]).toBe(PRICE_ALERT.to);
    expect(PRICE_ALERT.from).toBeGreaterThan(PRICE_ALERT.to);
    expect(summarize(DECK).incomplete.map((c) => c.name)).toContain(PRICE_ALERT.card);
  });

  it("formata em reais no padrao pt-BR", () => {
    expect(formatBRL(740).replace(/\s/g, " ")).toBe("R$ 7,40");
  });
});

describe("meta contra a colecao: cada deck em alta fecha", () => {
  it("tem + faltam = total em todos os decks de exemplo", () => {
    expect(META.length).toBeGreaterThanOrEqual(4);
    for (const row of META) {
      expect(row.have).toBeLessThanOrEqual(row.total);
      expect(row.have + (row.total - row.have)).toBe(row.total);
    }
  });
});

describe("custo de mana: simbolos, nome acessivel e busca por carta", () => {
  it("manaSymbols devolve os codigos das classes da fonte Mana, em minusculas e na ordem do custo", () => {
    expect(manaSymbols("{2}{U}{B}")).toEqual(["2", "u", "b"]);
    expect(manaSymbols("{1}{U}{B}")).toEqual(["1", "u", "b"]);
    expect(manaSymbols("{B}")).toEqual(["b"]);
  });

  it("manaSymbols aceita custo vazio (terreno) e rejeita o que nao e custo de mana", () => {
    expect(manaSymbols("")).toEqual([]);
    expect(() => manaSymbols("{X}")).toThrow(/custo de mana invalido/);
    expect(() => manaSymbols("2UB")).toThrow(/custo de mana invalido/);
    expect(() => manaSymbols('{U}"><script>')).toThrow(/custo de mana invalido/);
  });

  it("manaLabel descreve o custo em portugues para leitor de tela", () => {
    expect(manaLabel("{2}{U}{B}")).toBe("Custo de mana: 2 genérico, azul, preto");
    expect(manaLabel("{W}{R}{G}{C}")).toBe("Custo de mana: branco, vermelho, verde, incolor");
    expect(manaLabel("")).toBe("Terreno, sem custo de mana");
  });

  it("cardCost devolve o custo da carta do deck, vazio para terreno e para carta desconhecida", () => {
    expect(cardCost("Sentinela de Cinzas")).toBe("{2}{B}");
    expect(cardCost("Pena de Obsidiana")).toBe("{3}{U}{B}");
    expect(cardCost("Torre Esquecida")).toBe("");
    expect(cardCost("Carta que nao existe")).toBe("");
  });

  it("todo custo do deck de exemplo e valido e usa so simbolos que o recorte da fonte tem", () => {
    const inFont = new Set(["0", "1", "2", "3", "4", "5", "w", "u", "b", "r", "g"]);
    for (const card of DECK.cards) {
      for (const symbol of manaSymbols(card.cost)) expect(inFont.has(symbol), symbol).toBe(true);
    }
  });
});
