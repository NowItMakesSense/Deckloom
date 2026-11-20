// T-L02 / AC-L02a (REQ-L01): a landing e a rota publica "/". Ela traz a propria navegacao (dynamic island), entao o
// cabecalho global da sessao nao aparece nela; nas outras telas nada muda.
import { render, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";

import { AppRoutes, ROUTE_PATTERNS } from "./AppRoutes";
import { LANDING_ROUTE } from "./routes";
import { SessionProvider } from "./SessionProvider";
import { titleForPath } from "./routeTitles";

const BASE = "http://localhost";
const server = setupServer();

beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function asVisitor() {
  server.use(
    http.get(`${BASE}/api/v1/auth/session`, () =>
      HttpResponse.json(
        { code: "unauthenticated" },
        { status: 401, headers: { "Content-Type": "application/problem+json" } },
      ),
    ),
  );
}

function renderAt(entry: string) {
  asVisitor();
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProvider baseUrl={BASE}>
        <AppRoutes />
      </SessionProvider>
    </MemoryRouter>,
  );
}

describe("rota / (landing)", () => {
  it("e a rota publica da landing, conhecida pelo roteador", () => {
    expect(LANDING_ROUTE).toBe("/");
    expect(ROUTE_PATTERNS).toContain("/");
  });

  it("o visitante em / ve a landing (h1 da mensagem principal)", async () => {
    renderAt("/");

    expect(
      await screen.findByRole("heading", {
        level: 1,
        name: /Monte decks com as cartas que você já tem/,
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Criar conta" })).toHaveAttribute("href", "/cadastro");
  });

  it("a landing tem a propria navegacao: o cabecalho global da sessao nao aparece nela", async () => {
    const { container } = renderAt("/");
    await screen.findByRole("heading", { level: 1 });

    expect(container.querySelector(".app-header")).toBeNull();
    expect(screen.getAllByRole("navigation", { name: "Principal" })).toHaveLength(1);
  });

  it("as outras telas continuam com o cabecalho global", async () => {
    const { container } = renderAt("/entrar");
    await screen.findByRole("heading", { level: 1, name: "Entrar" });

    expect(container.querySelector(".app-header")).not.toBeNull();
  });

  it("o titulo da pagina segue o h1 (A11Y-20)", () => {
    expect(titleForPath("/")).toBe("Monte decks com as cartas que você já tem - Deckloom");
  });
});
