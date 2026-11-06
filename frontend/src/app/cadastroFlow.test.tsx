// T-122: integracao cadastro -> /perfil com sessao real e guards (REQ-001, design.md S1).
// A mensagem de boas-vindas nao pode se perder quando a sessao muda antes da navegacao.
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { AppRoutes } from "./AppRoutes";
import { SessionProvider } from "./SessionProvider";
import { fillField } from "@/shared/testing/fillField";

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

describe("cadastro com sessao e guards", () => {
  it("mostra a boas-vindas em /perfil depois do cadastro", async () => {
    let registered = false;
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "csrf-token" })),
      http.get(`${BASE}/api/v1/auth/session`, () =>
        registered
          ? HttpResponse.json(summary)
          : HttpResponse.json(
              { code: "unauthenticated" },
              { status: 401, headers: { "Content-Type": "application/problem+json" } },
            ),
      ),
      http.get(`${BASE}/api/v1/legal/terms`, () =>
        HttpResponse.json({ version: "v1", termsUrl: "/termos", privacyUrl: "/privacidade" }),
      ),
      http.post(`${BASE}/api/v1/auth/register`, () => {
        registered = true;
        return new HttpResponse(null, { status: 204 });
      }),
      http.get(`${BASE}/api/v1/me/profile`, () => new HttpResponse(null, { status: 500 })),
    );
    const user = userEvent.setup();
    // useTransitions={false}: mesma configuracao de src/main.tsx.
    render(
      <MemoryRouter initialEntries={["/cadastro"]} useTransitions={false}>
        <SessionProvider baseUrl={BASE}>
          <AppRoutes />
        </SessionProvider>
      </MemoryRouter>,
    );

    await screen.findByLabelText("Email");
    await fillField(user, screen.getByLabelText("Email"), "a@example.test");
    await fillField(user, screen.getByLabelText("Senha", { exact: true }), "senha-segura-123");
    await fillField(user, screen.getByLabelText("Nome de exibição"), "Jogadora");
    await user.click(screen.getByRole("checkbox", { name: /Li e aceito/ }));
    await user.click(screen.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }));
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    expect(await screen.findByText("Conta criada. Bem-vindo(a) ao Deckloom!")).toBeVisible();
  });
});
