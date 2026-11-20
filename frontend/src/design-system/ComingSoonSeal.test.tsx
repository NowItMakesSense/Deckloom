// T-L02 / REQ-L02 e docs/design-system.md 1.9: o selo "Em breve" marca o que ainda nao existe.
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { ComingSoonSeal } from "./ComingSoonSeal";

describe("ComingSoonSeal", () => {
  it("mostra o texto 'Em breve' (o estado nao depende so de cor)", () => {
    render(<ComingSoonSeal />);

    expect(screen.getByText("Em breve")).toBeInTheDocument();
  });

  it("nao e interativo: nao e link nem botao e nao recebe foco", () => {
    const { container } = render(<ComingSoonSeal />);
    const seal = screen.getByText("Em breve");

    expect(screen.queryByRole("link")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
    expect(seal).not.toHaveAttribute("tabindex");
    expect(container.querySelector("a, button, [tabindex]")).toBeNull();
  });
});
