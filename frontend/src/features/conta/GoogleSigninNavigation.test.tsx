// T-123 (correcao): o botao "Continuar com Google" da S1 e da S2 navega para o inicio do fluxo OIDC (REQ-006, REQ-007).
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { CadastroPage } from "./CadastroPage";
import { LoginPage } from "./LoginPage";

const BASE = "http://localhost";
const START_PATH = "/api/v1/auth/google/start?intent=signin";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  server.resetHandlers();
  vi.unstubAllGlobals();
});
afterAll(() => server.close());

function stubBrowserNavigation() {
  const visited: string[] = [];
  const record = (url: string | URL) => {
    visited.push(String(url));
  };
  vi.stubGlobal("location", {
    origin: BASE,
    protocol: "http:",
    host: "localhost",
    hostname: "localhost",
    pathname: "/",
    search: "",
    hash: "",
    assign: record,
    replace: record,
    reload: () => undefined,
    get href() {
      return `${BASE}/`;
    },
    set href(value: string) {
      record(value);
    },
  });
  return visited;
}

function renderAt(path: string) {
  server.use(
    http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(`${BASE}/api/v1/auth/session`, () =>
      HttpResponse.json(
        { code: "unauthenticated" },
        { status: 401, headers: { "Content-Type": "application/problem+json" } },
      ),
    ),
    http.get(`${BASE}/api/v1/legal/terms`, () =>
      HttpResponse.json({ version: "2026-10-01", termsUrl: "/termos", privacyUrl: "/privacidade" }),
    ),
  );
  render(
    <MemoryRouter initialEntries={[path]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/entrar" element={<LoginPage />} />
          <Route path="/cadastro" element={<CadastroPage />} />
        </Routes>
      </SessionProvider>
    </MemoryRouter>,
  );
}

describe("Continuar com Google inicia o fluxo OIDC", () => {
  it.each([
    ["S2 Login", "/entrar"],
    ["S1 Cadastro", "/cadastro"],
  ])("%s: o botao navega para %s", async (_screen, path) => {
    const visited = stubBrowserNavigation();
    renderAt(path);

    await userEvent.setup().click(screen.getByRole("button", { name: /Continuar com Google/ }));

    expect(visited).toEqual([START_PATH]);
  });

  it("S2 Login com retorno interno conhecido: leva returnTo ao inicio do fluxo (R-34)", async () => {
    const visited = stubBrowserNavigation();
    renderAt("/entrar?retorno=%2Fperfil%2Feditar");

    await userEvent.setup().click(screen.getByRole("button", { name: /Continuar com Google/ }));

    expect(visited).toEqual([`${START_PATH}&returnTo=%2Fperfil%2Feditar`]);
  });

  it.each(["https://evil.example/x", "//evil.example", "/rota-que-nao-existe"])(
    "S2 Login com retorno inseguro %s: cai em /perfil (R-34)",
    async (retorno) => {
      const visited = stubBrowserNavigation();
      renderAt(`/entrar?retorno=${encodeURIComponent(retorno)}`);

      await userEvent.setup().click(screen.getByRole("button", { name: /Continuar com Google/ }));

      expect(visited).toEqual([`${START_PATH}&returnTo=%2Fperfil`]);
    },
  );
});
