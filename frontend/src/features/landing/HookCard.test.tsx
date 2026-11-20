// T-L02 / AC-L02f (REQ-L11): o gancho e um card com a anatomia de uma carta: faixa de titulo, janela de arte
// com o simbolo do Deckloom, linha de tipo e caixa de texto; conteudo em HTML real.
import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { HookCard } from "./HookCard";
import { HOOKS } from "./hooks";

function renderCard(index: number) {
  const hook = HOOKS[index];
  const view = render(
    <HookCard hook={hook}>
      <p>conteudo de exemplo</p>
    </HookCard>,
  );
  return { hook, ...view };
}

describe("HookCard", () => {
  it("e um artigo nomeado pelo titulo do gancho, que e o titulo da secao (h2)", () => {
    const { hook } = renderCard(0);

    const card = screen.getByRole("article", { name: hook.title });
    expect(within(card).getByRole("heading", { level: 2, name: hook.title })).toBeInTheDocument();
  });

  it("tem as quatro partes da carta, nesta ordem: titulo, arte, tipo, texto", () => {
    const { container } = renderCard(1);

    const parts = [...container.querySelectorAll("[data-part]")].map((el) =>
      el.getAttribute("data-part"),
    );
    expect(parts).toEqual(["title", "art", "type", "text"]);
  });

  it("a faixa de titulo leva o selo 'Em breve' (o recurso ainda nao existe)", () => {
    const { container } = renderCard(2);

    const title = container.querySelector('[data-part="title"]') as HTMLElement;
    expect(within(title).getByText("Em breve")).toBeInTheDocument();
  });

  it("a janela de arte mostra o simbolo do Deckloom, decorativo, de arquivo local", () => {
    const { container } = renderCard(0);

    const art = container.querySelector('[data-part="art"]') as HTMLElement;
    const symbol = art.querySelector("img") as HTMLImageElement;
    expect(symbol.getAttribute("src")).toBe("/brand/deckloom/deckloom-symbol.svg");
    expect(symbol).toHaveAttribute("alt", "");
  });

  it("a linha de tipo lista cada recurso do gancho como elemento proprio", () => {
    const { container } = renderCard(0);

    const type = container.querySelector('[data-part="type"]') as HTMLElement;
    expect(within(type).getByText("Coleção")).toBeInTheDocument();
    expect(within(type).getByText("Decks")).toBeInTheDocument();
  });

  it("a caixa de texto traz o texto do gancho e o exemplo, em HTML real", () => {
    const { container, hook } = renderCard(3);

    const text = container.querySelector('[data-part="text"]') as HTMLElement;
    expect(within(text).getByText(hook.text)).toBeInTheDocument();
    expect(within(text).getByText("conteudo de exemplo")).toBeInTheDocument();
    expect(container.querySelector("canvas")).toBeNull();
  });

  it("as quatro partes que entram com animacao estao marcadas (title, art, type, text)", () => {
    const { container } = renderCard(2);

    expect(container.querySelectorAll("[data-reveal-item]")).toHaveLength(4);
    expect(container.querySelector("figure")).toBeNull();
  });

  it("o card identifica o gancho (data-hook) para o palco e para os testes", () => {
    const { container } = renderCard(1);

    expect(container.querySelector("article")).toHaveAttribute("data-hook", "meta");
  });
});
