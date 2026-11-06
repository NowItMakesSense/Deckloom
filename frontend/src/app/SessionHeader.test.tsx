// R-19: header estavel durante `loading` (sem flash de visitante) e links internos pelo router (sem recarga).
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { AppRoutes } from "./AppRoutes";
import { SessionProvider } from "./SessionProvider";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const server = setupServer();

beforeAll(() => server.listen({ onUnhandledFrame: "bypass" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

const PLAYER = {
  displayName: "Jogadora Teste",
  avatar: { kind: "preset", id: "p1" },
  hasPassword: true,
  hasGoogle: false,
};

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname}</p>;
}

function renderApp(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProvider baseUrl={BASE}>
        <AppRoutes />
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
}

function withGuest() {
  server.use(
    http.get(SESSION_URL, () =>
      HttpResponse.json(
        { code: "unauthenticated" },
        { status: 401, headers: { "Content-Type": "application/problem+json" } },
      ),
    ),
    http.all(`${BASE}/api/*`, () => HttpResponse.json({}, { status: 404 })),
  );
}

describe("header estavel durante loading (R-19, A11Y-30)", () => {
  it("nao mostra Entrar/Criar conta a quem ainda carrega e nao desmonta o header ao resolver a sessao", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get(SESSION_URL, async () => {
        await gate;
        return HttpResponse.json(PLAYER);
      }),
      http.all(`${BASE}/api/*`, () => HttpResponse.json({}, { status: 404 })),
    );
    renderApp("/perfil");

    const header = screen.getByRole("banner");
    expect(screen.queryByRole("link", { name: "Entrar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Criar conta" })).not.toBeInTheDocument();
    expect(screen.queryByRole("navigation", { name: "Acesso" })).not.toBeInTheDocument();

    release();
    await screen.findByRole("button", { name: /Menu da conta/ });
    expect(screen.getByRole("banner")).toBe(header);
  });

  it("visitante resolvido ve Entrar e Criar conta no mesmo header", async () => {
    withGuest();
    renderApp("/entrar");
    const header = screen.getByRole("banner");
    await screen.findByRole("navigation", { name: "Acesso" });
    expect(screen.getByRole("banner")).toBe(header);
  });
});

describe("links internos navegam pelo router, sem recarga (R-19)", () => {
  it("link Criar conta do header muda a rota e o clique tem o padrao cancelado", async () => {
    withGuest();
    renderApp("/entrar");
    const header = screen.getByRole("banner");
    const link = await within(header).findByRole("link", { name: "Criar conta" });
    const notPrevented = fireEvent.click(link);
    expect(notPrevented).toBe(false);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/cadastro"));
  });

  it("link do corpo da pagina (Entrar em /cadastro) navega pelo router", async () => {
    withGuest();
    renderApp("/cadastro");
    const main = await screen.findByRole("main");
    const link = within(main).getByRole("link", { name: "Entrar" });
    expect(fireEvent.click(link)).toBe(false);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/entrar"));
  });

  it("Meu perfil do menu da conta navega pelo router", async () => {
    server.use(
      http.get(SESSION_URL, () => HttpResponse.json(PLAYER)),
      http.all(`${BASE}/api/*`, () => HttpResponse.json({}, { status: 404 })),
    );
    const user = userEvent.setup();
    renderApp("/perfil/editar");
    await user.click(await screen.findByRole("button", { name: /Menu da conta/ }));
    const item = screen.getByRole("link", { name: "Meu perfil" });
    expect(fireEvent.click(item)).toBe(false);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^\/perfil$/));
  });
});
