// T-L02 / AC-L02j (REQ-L13, REQ-L01, REQ-L06): os planos de assinante, depois da secao dos cinco jogos.
import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { expectNoA11yViolations } from "@/shared/testing/expectNoA11yViolations";

import { PlansSection } from "./PlansSection";

function renderPlans() {
  return render(
    <MemoryRouter>
      <PlansSection />
    </MemoryRouter>,
  );
}

afterEach(() => vi.restoreAllMocks());

describe("PlansSection", () => {
  it("e a secao de planos, com titulo e dois planos nomeados: Gratis e Arcano (pago)", () => {
    renderPlans();

    const section = screen.getByRole("region", { name: /comece grátis/i });
    expect(section).toHaveAttribute("id", "planos");
    const plans = within(section).getAllByRole("article");
    expect(plans.map((p) => within(p).getByRole("heading", { level: 3 }).textContent)).toEqual([
      expect.stringContaining("Grátis"),
      expect.stringContaining("Arcano"),
    ]);
  });

  it("Gratis salva ate 5 decks", () => {
    renderPlans();

    const free = screen.getByRole("article", { name: /grátis/i });
    expect(free).toHaveTextContent("Até 5 decks");
    expect(free).toHaveTextContent("R$ 0");
  });

  it("o pago custa R$ 19,90 por mes e lista IA, decks ilimitados, alertas de preco e sem anuncios", () => {
    renderPlans();

    const paid = screen.getByRole("article", { name: /arcano/i });
    expect(paid).toHaveTextContent("R$ 19,90");
    expect(paid).toHaveTextContent(/por mês/);
    const items = within(paid)
      .getAllByRole("listitem")
      .map((li) => li.textContent);
    expect(items.join(" | ")).toMatch(
      /Sugestões com IA.*Decks ilimitados.*Alertas de preço.*Sem anúncios/,
    );
  });

  it("todo botao da secao aponta para /cadastro (ainda nao ha pagamento do jogador)", () => {
    renderPlans();

    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(2);
    for (const link of links) expect(link).toHaveAttribute("href", "/cadastro");
    expect(screen.queryAllByRole("button")).toHaveLength(0);
  });

  it("nao cobra nem coleta dado de pagamento: sem formulario, cartao ou checkout", () => {
    const { container } = renderPlans();

    expect(container.querySelector("form, input")).toBeNull();
    expect(container.textContent).toMatch(/ainda não é cobrada/);
  });

  it("os botoes nao repetem o texto 'Criar conta' da navegacao", () => {
    renderPlans();

    expect(screen.queryByRole("link", { name: /^criar conta$/i })).toBeNull();
  });

  it("sem script de animacao (movimento reduzido) o preco ja aparece inteiro", () => {
    const { container } = renderPlans();

    expect(container.querySelector("[data-price]")).toHaveTextContent("R$ 19,90");
  });

  it("o preco que o leitor de tela le e fixo (R$ 19,90) mesmo enquanto o numero visual conta ate la", () => {
    renderPlans();

    const paid = screen.getByRole("article", { name: /arcano/i });
    expect(
      within(paid).getByText("R$ 19,90", { selector: ".landing-sr-only" }),
    ).toBeInTheDocument();
    expect(paid.querySelector("[data-price]")).toHaveAttribute("aria-hidden", "true");
  });

  it("axe sem violacoes", async () => {
    const { container } = renderPlans();

    await expectNoA11yViolations(container);
  }, 30_000);
});
