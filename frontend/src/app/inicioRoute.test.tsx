// T-L03 / REQ-L04 v1, REQ-L05 v1 (AC-L03a, AC-L03b): home /inicio depois do login e destino padrao.
import { render, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";

import { AppRoutes } from "./AppRoutes";
import { DEFAULT_RETURN_TO, safeReturnTo } from "./routes";
import { SessionProvider } from "./SessionProvider";
import { titleForPath } from "./routeTitles";

const BASE = "http://localhost";
const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

const summary = {
  displayName: "Jogadora Teste",
  avatar: { kind: "preset", id: "p1" },
  hasPassword: true,
  hasGoogle: false,
};

function Probe() {
  const l = useLocation();
  return <p data-testid="location">{l.pathname + l.search}</p>;
}

function renderAt(entry: string, player: boolean) {
  server.use(
    http.get(`${BASE}/api/v1/auth/session`, () =>
      player
        ? HttpResponse.json(summary)
        : HttpResponse.json(
            { code: "unauthenticated" },
            { status: 401, headers: { "Content-Type": "application/problem+json" } },
          ),
    ),
  );
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProvider baseUrl={BASE}>
        <AppRoutes />
        <Probe />
      </SessionProvider>
    </MemoryRouter>,
  );
}

const path = () => new URL(screen.getByTestId("location").textContent ?? "", BASE).pathname;

describe("/inicio (AC-L03a)", () => {
  it("jogador logado ve Decks, Colecao, Meta e Lojas marcados 'em breve' e o atalho do perfil", async () => {
    renderAt("/inicio", true);
    await screen.findByRole("heading", { level: 1, name: /Jogadora Teste/ });
    const list = screen.getByRole("list", { name: "Áreas do Deckloom" });
    for (const name of ["Decks", "Coleção", "Meta", "Lojas"]) {
      const item = screen.getByRole("heading", { level: 2, name }).closest("li")!;
      expect(list).toContainElement(item);
      expect(item).toHaveTextContent("Em breve");
      expect(item.querySelector("a")).toBeNull();
    }
    expect(screen.getByRole("link", { name: /Meu perfil/ })).toHaveAttribute("href", "/perfil");
  });

  it("o visitante em /inicio vai para /entrar", async () => {
    renderAt("/inicio", false);
    await waitFor(() => expect(path()).toBe("/entrar"));
  });

  it("tem titulo proprio", () => {
    expect(titleForPath("/inicio")).toBe("Início - Deckloom");
  });
});

describe("destino padrao (AC-L03b)", () => {
  it("DEFAULT_RETURN_TO e /inicio e retorno valido continua respeitado", () => {
    expect(DEFAULT_RETURN_TO).toBe("/inicio");
    expect(safeReturnTo(null)).toBe("/inicio");
    expect(safeReturnTo("/perfil/editar")).toBe("/perfil/editar");
    expect(safeReturnTo("/inicio")).toBe("/inicio");
  });

  it("jogador logado em / vai para /inicio", async () => {
    renderAt("/", true);
    await waitFor(() => expect(path()).toBe("/inicio"));
  });

  it("jogador logado em /entrar vai para /inicio", async () => {
    renderAt("/entrar", true);
    await waitFor(() => expect(path()).toBe("/inicio"));
  });
});
