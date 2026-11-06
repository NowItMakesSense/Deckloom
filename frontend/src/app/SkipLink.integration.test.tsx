// R-16 (RED): SkipLink montado no app e alvo `#conteudo` no <main> de cada tela
// (A11Y-23, WCAG 2.4.1).
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { AppRoutes } from "./AppRoutes";
import { SessionProvider } from "./SessionProvider";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const server = setupServer();

beforeAll(() => server.listen({ onUnhandledFrame: "bypass" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

const SKIP_NAME = "Pular para o conteúdo";

function withoutSession() {
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

// Fluxo Google: a pendencia existe (a S4/S5 nao redireciona) e a leitura da sessao nunca termina
// (a S3 fica em "processando"), para que cada tela mantenha o proprio <main> durante o teste.
function withGooglePending() {
  server.use(
    http.get(SESSION_URL, () => new Promise<Response>(() => undefined)),
    http.get(`${BASE}/api/v1/auth/google/pending`, () =>
      HttpResponse.json({ email: "jogadora@example.test", suggestedDisplayName: "Jogadora" }),
    ),
    http.all(`${BASE}/api/*`, () => HttpResponse.json({}, { status: 404 })),
  );
}

function withSession() {
  server.use(
    http.get(SESSION_URL, () =>
      HttpResponse.json({
        displayName: "Jogadora Teste",
        avatar: { kind: "preset", id: "p1" },
        hasPassword: true,
        hasGoogle: false,
      }),
    ),
    http.all(`${BASE}/api/*`, () => HttpResponse.json({}, { status: 404 })),
  );
}

function renderApp(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProvider baseUrl={BASE}>
        <AppRoutes />
      </SessionProvider>
    </MemoryRouter>,
  );
}

async function expectSkipLinkFlow() {
  const main = await screen.findByRole("main");
  const user = userEvent.setup();

  // Carga de pagina: nenhum foco e movido ao <h1> (QA-03); o primeiro Tab real chega ao SkipLink.
  await user.tab();
  const skip = screen.getByRole("link", { name: SKIP_NAME });
  expect(skip).toHaveFocus();
  expect(skip).toHaveAttribute("href", "#conteudo");

  expect(main).toHaveAttribute("id", "conteudo");
  expect(main).toHaveAttribute("tabindex", "-1");

  await user.keyboard("{Enter}");
  expect(main).toHaveFocus();
}

describe("SkipLink montado no app (A11Y-23)", () => {
  it.each(["/cadastro", "/entrar", "/esqueci-senha", "/redefinir-senha?token=abc"])(
    "visitante em %s: primeiro Tab e o SkipLink e Enter foca o <main id=conteudo>",
    async (entry) => {
      withoutSession();
      renderApp(entry);
      await expectSkipLinkFlow();
    },
  );

  // R-42: rotas do fluxo Google (fora dos guards) e a variante com formulario da redefinicao,
  // cujo token vem do fragmento da URL.
  it.each([
    "/entrar/google/retorno?resultado=ok",
    "/entrar/google/concluir",
    "/entrar/google/vincular",
  ])(
    "visitante em %s (fluxo Google): primeiro Tab e o SkipLink e Enter foca o <main id=conteudo>",
    async (entry) => {
      withGooglePending();
      renderApp(entry);
      await expectSkipLinkFlow();
    },
  );

  // A11Y-31 (emenda QA-03): aviso de sessao expirada aberto direto nao rouba o foco na carga.
  it("visitante em /entrar?aviso=sessao-expirada (carga): aviso presente, sem foco, primeiro Tab e o SkipLink", async () => {
    withoutSession();
    renderApp("/entrar?aviso=sessao-expirada");
    const alert = await screen.findByRole("alert");
    expect(alert).not.toHaveFocus();
    await expectSkipLinkFlow();
  });

  it("visitante em /redefinir-senha#token=abc (formulario): primeiro Tab e o SkipLink", async () => {
    withoutSession();
    window.location.hash = "#token=abc";
    renderApp("/redefinir-senha");
    await screen.findByLabelText(/nova senha/i);
    await expectSkipLinkFlow();
  });

  it.each(["/perfil", "/perfil/editar"])(
    "jogador em %s: primeiro Tab e o SkipLink e Enter foca o <main id=conteudo>",
    async (entry) => {
      withSession();
      renderApp(entry);
      await expectSkipLinkFlow();
    },
  );
});
