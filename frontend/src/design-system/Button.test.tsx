import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Button } from "./Button";

// T-173 / U-10: a largura estavel (WC-12) e em rem e cresce com o texto; a 320px/200% passava da largura da tela.
// Layout real so existe no navegador (E2E em accessibility.spec.ts); aqui, o contrato do CSS.
const styles = import.meta.glob("./Button.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

function bodyOf(selector: string): string {
  const css = styles["./Button.css"].replace(/\/\*[\s\S]*?\*\//g, "");
  return [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
    .filter((m) => m[1].trim() === selector)
    .map((m) => m[2])
    .join("\n");
}

describe("Button stableWidth (WC-12, U-10)", () => {
  it("marca o botao com data-stable so quando stableWidth", () => {
    const { rerender } = render(<Button>Criar conta</Button>);
    expect(screen.getByRole("button")).not.toHaveAttribute("data-stable");
    rerender(<Button stableWidth>Criar conta</Button>);
    expect(screen.getByRole("button")).toHaveAttribute("data-stable", "true");
  });

  it("a largura minima estavel e 11rem limitada a largura disponivel (CSS)", () => {
    expect(bodyOf('[data-stable="true"]')).toMatch(
      /min-width:\s*min\(\s*11rem\s*,\s*100%\s*\)\s*;/,
    );
  });

  it("nao fixa a largura minima por estilo em linha", () => {
    render(
      <Button stableWidth loading>
        Criar conta
      </Button>,
    );
    expect(screen.getByRole("button").getAttribute("style")).toBeNull();
  });
});
