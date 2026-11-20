// T-L02 / AC-L02f: os numeros do mock do deck fecham na pagina (tem + faltam = total) antes e depois do "Tenho +1".
import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { AssistantExample, DeckExample, MetaExample, ShopsExample } from "./examples";
import {
  DECK,
  PRICE_ALERT,
  SHOPS,
  addOne,
  formatBRL,
  shopQuote,
  summarize,
} from "./mock/deck-mock";
import { useState } from "react";

function nbsp(text: string): string {
  return text.replace(/\s/g, " ");
}

function Harness() {
  const [deck, setDeck] = useState(DECK);
  return (
    <>
      <DeckExample deck={deck} onHave={(name) => setDeck((current) => addOne(current, name))} />
      <ShopsExample deck={deck} />
    </>
  );
}

function summaryNumbers() {
  const summary = screen.getByRole("status");
  return {
    have: Number(summary.getAttribute("data-have")),
    missing: Number(summary.getAttribute("data-missing")),
    total: Number(summary.getAttribute("data-total")),
    text: summary.textContent ?? "",
  };
}

describe("DeckExample", () => {
  it("mostra 47 de 60 e faltam 13, e o que esta escrito bate com os dados (tem + faltam = total)", () => {
    render(<Harness />);

    const numbers = summaryNumbers();
    expect(numbers).toMatchObject({ have: 47, missing: 13, total: 60 });
    expect(numbers.have + numbers.missing).toBe(numbers.total);
    expect(numbers.text).toContain("47 de 60");
    expect(numbers.text).toContain("faltam 13");
  });

  it("cada 'Tenho +1' soma uma ao que voce tem e tira uma do que falta, ate o deck fechar", () => {
    render(<Harness />);

    let before = summaryNumbers();
    for (let step = 0; step < 13; step += 1) {
      fireEvent.click(screen.getAllByRole("button", { name: /Tenho \+1/ })[0]);
      const after = summaryNumbers();
      expect(after.have).toBe(before.have + 1);
      expect(after.missing).toBe(before.missing - 1);
      expect(after.have + after.missing).toBe(after.total);
      expect(after.text).toContain(`${after.have} de ${after.total}`);
      before = after;
    }

    expect(before).toMatchObject({ have: 60, missing: 0, total: 60 });
    expect(screen.queryAllByRole("button", { name: /Tenho \+1/ })).toHaveLength(0);
    expect(screen.getByText(/Deck completo/)).toBeInTheDocument();
  });

  it("o botao diz de qual carta e (o nome acessivel contem o texto visivel)", () => {
    render(<Harness />);

    const first = screen.getAllByRole("button", { name: /Tenho \+1/ })[0];
    const missing = summarize(DECK).incomplete[0].name;
    expect(first).toHaveTextContent("Tenho +1");
    expect(first).toHaveAccessibleName(`Tenho +1 de ${missing}`);
  });

  it("cada carta da lista mostra o custo de mana com simbolos e quanto ja tem", () => {
    const { container } = render(<Harness />);

    const first = summarize(DECK).incomplete[0];
    const row = screen.getByText(first.name).closest("li") as HTMLElement;
    expect(row.querySelector(".ms")).not.toBeNull();
    expect(row).toHaveTextContent(`${first.have} de ${first.need}`);
    expect(container.textContent).toContain("Revoada Noturna");
  });
});

describe("ShopsExample", () => {
  it("cota so o que falta em cada loja, no total que a tabela soma, e marca o menor preco", () => {
    render(<Harness />);

    const totals = SHOPS.map((shop) => shopQuote(DECK, shop).total);
    for (const shop of SHOPS) {
      const row = screen.getByText(shop.name).closest("li") as HTMLElement;
      expect(nbsp(row.textContent ?? "")).toContain(nbsp(formatBRL(shopQuote(DECK, shop).total)));
    }
    const cheapest = SHOPS[totals.indexOf(Math.min(...totals))];
    const cheapestRow = screen.getByText(cheapest.name).closest("li") as HTMLElement;
    expect(within(cheapestRow).getByText("Menor preço")).toBeInTheDocument();
    expect(screen.getAllByText("Menor preço")).toHaveLength(1);
  });

  it("depois de 'Tenho +1' a cotacao acompanha: uma copia a menos", () => {
    render(<Harness />);
    const shop = SHOPS[0];
    const name = summarize(DECK).incomplete[0].name;

    fireEvent.click(screen.getAllByRole("button", { name: /Tenho \+1/ })[0]);

    const row = screen.getByText(shop.name).closest("li") as HTMLElement;
    const expected = shopQuote(addOne(DECK, name), shop).total;
    expect(nbsp(row.textContent ?? "")).toContain(nbsp(formatBRL(expected)));
    expect(row).toHaveTextContent("faltam 12");
  });

  it("com o deck completo nao ha o que comprar", () => {
    render(<Harness />);

    for (let step = 0; step < 13; step += 1) {
      fireEvent.click(screen.getAllByRole("button", { name: /Tenho \+1/ })[0]);
    }

    expect(screen.getByText("Nada a comprar: você já tem o deck inteiro.")).toBeInTheDocument();
  });
});

describe("MetaExample", () => {
  it("cada deck em alta fecha: tem + faltam = total, escrito e na barra de progresso", () => {
    render(<MetaExample />);

    const rows = screen.getAllByRole("listitem");
    expect(rows).toHaveLength(4);
    const first = rows[0];
    expect(first).toHaveTextContent("Aggro vermelho");
    expect(first).toHaveTextContent("52 de 60");
    expect(first).toHaveTextContent("faltam 8");
    const bar = within(first).getByRole("progressbar");
    expect(bar).toHaveAttribute("aria-valuenow", "52");
    expect(bar).toHaveAttribute("aria-valuemax", "60");
  });
});

describe("AssistantExample", () => {
  it("mostra o pedido em linguagem natural e o alerta de queda de preco com os numeros do mock", () => {
    render(<AssistantExample />);

    const text = nbsp(document.body.textContent ?? "");
    expect(text).toContain(PRICE_ALERT.card);
    expect(text).toContain(nbsp(formatBRL(PRICE_ALERT.from)));
    expect(text).toContain(nbsp(formatBRL(PRICE_ALERT.to)));
    expect(text).toContain(PRICE_ALERT.shop);
    expect(screen.getByText(/linguagem natural/i)).toBeInTheDocument();
  });
});
