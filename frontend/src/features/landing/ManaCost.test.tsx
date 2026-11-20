// T-L02 / REQ-L01: simbolos de mana renderizados com JSX (sem HTML interpolado): custo com nome acessivel.
import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ManaCost } from "./ManaCost";

afterEach(() => vi.restoreAllMocks());

describe("ManaCost", () => {
  it("renderiza um simbolo decorativo por item do custo, na ordem, com as classes da fonte Mana", () => {
    const { container } = render(<ManaCost cost="{2}{U}{B}" />);
    const symbols = [...container.querySelectorAll(".ms")];

    expect(symbols.map((el) => [...el.classList].find((c) => /^ms-[0-9a-z]$/.test(c)))).toEqual([
      "ms-2",
      "ms-u",
      "ms-b",
    ]);
    for (const symbol of symbols) expect(symbol).toHaveAttribute("aria-hidden", "true");
  });

  it("o grupo tem nome acessivel em portugues e guarda o custo original", () => {
    render(<ManaCost cost="{1}{U}{B}" />);

    const group = screen.getByRole("img", { name: "Custo de mana: 1 genérico, azul, preto" });
    expect(group).toHaveAttribute("data-cost", "{1}{U}{B}");
  });

  it("terreno nao tem custo: mostra o rotulo 'terreno' e o nome acessivel diz isso", () => {
    const { container } = render(<ManaCost cost="" />);

    expect(screen.getByRole("img", { name: "Terreno, sem custo de mana" })).toBeInTheDocument();
    expect(container.querySelectorAll(".ms")).toHaveLength(0);
    expect(container.querySelector(".cost__land")).toHaveTextContent("terreno");
  });

  it("nunca injeta HTML: o custo invalido falha em vez de virar marcacao", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});

    expect(() => render(<ManaCost cost={'{U}"><img src=x onerror=alert(1)>'} />)).toThrow(
      /custo de mana invalido/,
    );
    expect(document.querySelector("img")).toBeNull();
  });
});
