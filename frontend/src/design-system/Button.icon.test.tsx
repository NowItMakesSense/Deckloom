// T-L01C / REQ-L03 v5: botoes de acao com icone (lucide-animated, decorativo e herdando a cor do texto).
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { Button } from "./Button";
import { LogInIcon } from "./icons";

describe("Button com icone", () => {
  it("mostra o icone decorativo antes do texto e mantem o nome acessivel", () => {
    render(<Button icon={<LogInIcon size={18} />}>Entrar</Button>);

    const button = screen.getByRole("button", { name: "Entrar" });
    expect(button.querySelector("svg")).not.toBeNull();
    expect(
      button.querySelector("[aria-hidden='true'] svg, svg[aria-hidden='true']"),
    ).not.toBeNull();
    expect(button.firstElementChild?.querySelector("svg")).not.toBeNull();
  });

  it("em loading o icone sai e entra o indicador, sem mudar o nome", () => {
    render(
      <Button loading icon={<LogInIcon size={18} />}>
        Entrar
      </Button>,
    );

    const button = screen.getByRole("button", { name: "Entrar" });
    expect(button.querySelector(".button__spinner")).not.toBeNull();
    expect(button.querySelector("svg")).toBeNull();
  });

  it("sem icone o botao segue como antes", () => {
    render(<Button>Salvar</Button>);

    expect(screen.getByRole("button", { name: "Salvar" }).querySelector("svg")).toBeNull();
  });
});
