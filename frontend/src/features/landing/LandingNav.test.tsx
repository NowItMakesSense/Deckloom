// T-L02 / AC-L02g (REQ-L01, REQ-L03): nav "dynamic island" em vidro, com logo, Criar conta, Entrar e um unico
// controle de tema; colapsa no celular (aria-expanded) e fecha com Esc.
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { setThemePreference } from "@/app/theme/theme";

import { LandingNav } from "./LandingNav";

// O botao de menu so aparece no celular (CSS: display none em janela larga). Escondido, o jsdom nao calcula o nome
// acessivel dele, entao os testes o buscam pelo vinculo com o menu (aria-controls) e checam o nome em um teste proprio.
function menuToggle(): HTMLButtonElement {
  return document.querySelector<HTMLButtonElement>('button[aria-controls="landing-menu"]')!;
}

function renderNav() {
  return render(
    <MemoryRouter>
      <LandingNav />
    </MemoryRouter>,
  );
}

beforeEach(() => act(() => setThemePreference("light")));
afterEach(() => act(() => setThemePreference("system")));

describe("LandingNav", () => {
  it("e uma navegacao principal com a logo do Deckloom (arquivo local), que leva ao inicio", () => {
    renderNav();

    const nav = screen.getByRole("navigation", { name: "Principal" });
    const logo = within(nav).getByRole("img", { name: "Deckloom" });
    expect(logo.getAttribute("src")).toBe("/brand/deckloom/deckloom-logo.svg");
    expect(within(nav).getByRole("link", { name: "Deckloom" })).toHaveAttribute("href", "/");
  });

  it("usa a logo para fundo escuro no tema escuro", () => {
    renderNav();

    act(() => setThemePreference("dark"));

    expect(screen.getByRole("img", { name: "Deckloom" }).getAttribute("src")).toBe(
      "/brand/deckloom/deckloom-logo-on-dark.svg",
    );
  });

  it("no celular a logo vira so o simbolo (picture), sem texto duplicado", () => {
    const { container } = renderNav();

    const source = container.querySelector("picture source") as HTMLSourceElement;
    expect(source.getAttribute("srcset")).toBe("/brand/deckloom/deckloom-symbol.svg");
    expect(source.getAttribute("media")).toMatch(/max-width/);
  });

  it("Criar conta e o botao primario para /cadastro, com icone", () => {
    renderNav();

    const create = screen.getByRole("link", { name: "Criar conta" });
    expect(create).toHaveAttribute("href", "/cadastro");
    expect(create).toHaveClass("landing-cta");
    expect(create.querySelector("svg")).not.toBeNull();
  });

  it("Entrar leva a /entrar", () => {
    renderNav();

    expect(screen.getByRole("link", { name: "Entrar" })).toHaveAttribute("href", "/entrar");
  });

  it("os links de secao apontam para ancoras da propria pagina, na ordem da jornada", () => {
    renderNav();

    const nav = screen.getByRole("navigation", { name: "Principal" });
    const anchors = within(nav)
      .getAllByRole("link")
      .map((link) => link.getAttribute("href"))
      .filter((href) => href?.startsWith("#"));
    expect(anchors).toEqual(["#como-funciona", "#planos"]);
  });

  it("o botao de menu comeca recolhido e alterna aria-expanded, apontando para o menu", () => {
    const { container } = renderNav();

    const toggle = menuToggle();
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const menu = container.querySelector(`#${toggle.getAttribute("aria-controls")}`);
    expect(menu).not.toBeNull();
    expect(menu).toHaveAttribute("data-open", "false");

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(menu).toHaveAttribute("data-open", "true");

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });

  it("o botao de menu tem o nome 'Menu' (aria-label) e nao muda de nome ao abrir", () => {
    renderNav();

    expect(menuToggle()).toHaveAttribute("aria-label", "Menu");
    fireEvent.click(menuToggle());
    expect(menuToggle()).toHaveAttribute("aria-label", "Menu");
  });

  it("Esc fecha o menu e devolve o foco ao botao", () => {
    renderNav();
    const toggle = menuToggle();
    // Escondido (jsdom nao aplica a media query do celular), o botao nao aceita foco: espiona a chamada.
    const focus = vi.spyOn(toggle, "focus");
    fireEvent.click(toggle);

    fireEvent.keyDown(screen.getByRole("link", { name: "Entrar" }), { key: "Escape" });

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(focus).toHaveBeenCalledTimes(1);
  });

  it("Esc com o menu fechado nao muda nada", () => {
    renderNav();
    const toggle = menuToggle();
    const focus = vi.spyOn(toggle, "focus");

    fireEvent.keyDown(toggle, { key: "Escape" });

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(focus).not.toHaveBeenCalled();
  });

  it("fecha ao escolher um link e ao tocar fora da navegacao", () => {
    renderNav();
    const toggle = menuToggle();

    fireEvent.click(toggle);
    fireEvent.click(screen.getByRole("link", { name: "Planos" }));
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(toggle);
    fireEvent.pointerDown(document.body);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });

  it("ha um unico controle de tema, com o nome da acao, e ele troca o tema", () => {
    renderNav();

    const controls = screen.getAllByRole("button", { name: /tema/i });
    expect(controls).toHaveLength(1);
    expect(controls[0]).toHaveAccessibleName("Usar tema escuro");

    fireEvent.click(controls[0]);

    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(screen.getByRole("button", { name: /tema/i })).toHaveAccessibleName("Usar tema claro");
  });
});
