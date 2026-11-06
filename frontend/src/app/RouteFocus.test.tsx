// T-128 (RED): foco no <h1>, titulo unico por rota e aviso de sessao expirada
// (REQ-011, REQ-012; A11Y-20, A11Y-31, A11Y-34, A11Y-37, MOT-10).
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Link, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { expectNoA11yViolations } from "../shared/testing/expectNoA11yViolations";
import { fillField } from "../shared/testing/fillField";
import { AppRoutes } from "./AppRoutes";
import { SessionProvider } from "./SessionProvider";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const server = setupServer();

beforeAll(() => server.listen({ onUnhandledFrame: "bypass" }));
afterAll(() => server.close());

const PLAYER_NAME = "Jogadora Teste";

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

// Rotas de visitante e do fluxo Google que ja tem tela (S1, S2, S6, S7).
const SCREEN_ROUTES = [
  "/cadastro",
  "/entrar",
  "/esqueci-senha",
  "/redefinir-senha?token=abc",
] as const;

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname + location.search}</p>;
}

function NavLinks() {
  return (
    <nav aria-label="teste">
      {SCREEN_ROUTES.map((to) => (
        <Link key={to} to={to}>
          ir para {to}
        </Link>
      ))}
    </nav>
  );
}

function renderApp(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProvider baseUrl={BASE}>
        <NavLinks />
        <AppRoutes />
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
}

function currentLocation(): { path: string; params: URLSearchParams } {
  const url = new URL(screen.getByTestId("location").textContent ?? "", BASE);
  return { path: url.pathname, params: url.searchParams };
}

async function expectH1Present() {
  const h1 = await screen.findByRole("heading", { level: 1 });
  expect(h1).toHaveAttribute("tabindex", "-1");
}

async function expectH1Focused() {
  const h1 = await screen.findByRole("heading", { level: 1 });
  await waitFor(() => expect(h1).toHaveFocus());
  expect(h1).toHaveAttribute("tabindex", "-1");
}

function stubReducedMotion(reduce: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: reduce && query.includes("prefers-reduced-motion"),
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }));
}

beforeEach(() => {
  document.title = "";
  withoutSession();
});

afterEach(() => {
  server.resetHandlers();
  vi.unstubAllGlobals();
});

describe("foco no <h1> em toda transicao de rota (A11Y-34)", () => {
  it.each(SCREEN_ROUTES)(
    "ao navegar para %s (transicao) o foco vai ao <h1> com tabindex=-1",
    async (route) => {
      const user = userEvent.setup();
      renderApp(route === "/entrar" ? "/cadastro" : "/entrar");
      await screen.findByRole("heading", { level: 1 });
      await user.click(screen.getByRole("link", { name: `ir para ${route}` }));
      await waitFor(() => expect(currentLocation().path).toBe(route.split("?")[0]));
      await expectH1Focused();
    },
  );

  it.each(SCREEN_ROUTES)("na carga de %s o foco NAO vai ao <h1> (QA-03)", async (route) => {
    renderApp(route);
    const h1 = await screen.findByRole("heading", { level: 1 });
    await act(async () => {});
    expect(h1).not.toHaveFocus();
  });

  it("ao navegar entre rotas o foco volta ao <h1> da nova tela", async () => {
    const user = userEvent.setup();
    renderApp("/cadastro");
    await expectH1Present();
    const first = screen.getByRole("heading", { level: 1 }).textContent;

    await user.click(screen.getByRole("link", { name: "ir para /esqueci-senha" }));
    await waitFor(() => expect(currentLocation().path).toBe("/esqueci-senha"));
    await expectH1Focused();
    expect(screen.getByRole("heading", { level: 1 }).textContent).not.toBe(first);

    await user.click(screen.getByRole("link", { name: "ir para /entrar" }));
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    await expectH1Focused();
  });
});

describe("titulo unico por rota, um <h1> e niveis sem saltos (A11Y-20)", () => {
  it("cada tela tem <title> nao vazio e distinto das demais", async () => {
    const titles: string[] = [];
    for (const route of SCREEN_ROUTES) {
      document.title = "";
      const { unmount } = renderApp(route);
      await expectH1Present();
      await waitFor(() => expect(document.title.trim()).not.toBe(""));
      titles.push(document.title);
      unmount();
    }
    expect(new Set(titles).size).toBe(SCREEN_ROUTES.length);
  });

  it("o <title> muda a cada transicao de rota", async () => {
    const user = userEvent.setup();
    renderApp("/cadastro");
    await expectH1Present();
    await waitFor(() => expect(document.title.trim()).not.toBe(""));
    const before = document.title;

    await user.click(screen.getByRole("link", { name: "ir para /entrar" }));
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    await waitFor(() => expect(document.title).not.toBe(before));
    expect(document.title.trim()).not.toBe("");
  });

  it("de /entrar para /perfil o <title> passa a ser o da tela de perfil (U-07)", async () => {
    server.use(
      http.get(SESSION_URL, () =>
        HttpResponse.json({
          displayName: PLAYER_NAME,
          avatar: { kind: "preset", id: "p1" },
          hasPassword: true,
          hasGoogle: false,
        }),
      ),
    );
    document.title = "Entrar - Deckloom";
    renderApp("/entrar");
    await waitFor(() => expect(currentLocation().path).toBe("/perfil"));
    await screen.findByRole("heading", { level: 1, name: "Meu perfil" });
    await waitFor(() => expect(document.title).toBe("Meu perfil - Deckloom"));
  });

  it.each(SCREEN_ROUTES)(
    "%s tem exatamente um <h1>, sem saltos de nivel e sem violacoes axe",
    async (route) => {
      const { container } = renderApp(route);
      await expectH1Present();
      expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);

      const levels = screen
        .getAllByRole("heading")
        .map((heading) => Number(heading.tagName.slice(1)));
      expect(levels[0]).toBe(1);
      for (let i = 1; i < levels.length; i += 1) {
        expect(levels[i] - levels[i - 1]).toBeLessThanOrEqual(1);
      }
      await expectNoA11yViolations(container);
    },
  );
});

describe("titulo do link invalido de /redefinir-senha dentro do app (U-08, A11Y-20)", () => {
  const INVALID_DOCUMENT_TITLE = "Link inválido ou expirado - Deckloom";

  afterEach(() => {
    window.history.replaceState(null, "", "/");
  });

  it("sem token o RouteTitle nao sobrescreve o titulo do link invalido", async () => {
    renderApp("/redefinir-senha");
    await screen.findByRole("heading", { level: 1, name: "Link inválido ou expirado" });
    await waitFor(() => expect(document.title).toBe(INVALID_DOCUMENT_TITLE));
    await act(async () => {});
    expect(document.title).toBe(INVALID_DOCUMENT_TITLE);
  });

  it("ao navegar de /cadastro para o link invalido (mesmo commit da troca de rota) o titulo e o do link invalido", async () => {
    const user = userEvent.setup();
    renderApp("/cadastro");
    await expectH1Present();
    await waitFor(() => expect(document.title).toBe("Criar conta - Deckloom"));

    await user.click(screen.getByRole("link", { name: "ir para /redefinir-senha?token=abc" }));

    await screen.findByRole("heading", { level: 1, name: "Link inválido ou expirado" });
    await waitFor(() => expect(document.title).toBe(INVALID_DOCUMENT_TITLE));
    await act(async () => {});
    expect(document.title).toBe(INVALID_DOCUMENT_TITLE);
  });

  it("depois de reset_link_invalid o titulo continua o do link invalido", async () => {
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "csrf-token" })),
      http.post(`${BASE}/api/v1/auth/password-reset/confirm`, () =>
        HttpResponse.json(
          { code: "reset_link_invalid" },
          { status: 400, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );
    window.history.replaceState(null, "", "/redefinir-senha#token=tok-abc123");
    const user = userEvent.setup();
    renderApp("/redefinir-senha");
    await waitFor(() => expect(document.title).toBe("Criar nova senha - Deckloom"));

    await fillField(user, await screen.findByLabelText("Nova senha"), "nova-senha-123");
    await user.click(screen.getByRole("button", { name: "Salvar nova senha" }));

    await screen.findByRole("heading", { level: 1, name: "Link inválido ou expirado" });
    await waitFor(() => expect(document.title).toBe(INVALID_DOCUMENT_TITLE));
    await act(async () => {});
    expect(document.title).toBe(INVALID_DOCUMENT_TITLE);
  });
});

describe("a troca de rota nao anima (MOT-10)", () => {
  it("nem o <main> nem o <h1> tem animation ou transition apos a transicao", async () => {
    const user = userEvent.setup();
    renderApp("/cadastro");
    await expectH1Present();
    await user.click(screen.getByRole("link", { name: "ir para /entrar" }));
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    await expectH1Focused();

    for (const element of [screen.getByRole("main"), screen.getByRole("heading", { level: 1 })]) {
      const style = getComputedStyle(element);
      expect(["", "none"]).toContain(style.animationName);
      expect(["", "0s"]).toContain(style.transitionDuration);
      expect(element.getAnimations?.() ?? []).toHaveLength(0);
    }
  });
});

describe("sessao expirada em rota de Jogador (A11Y-31, REQ-011, REQ-012)", () => {
  it("vai a /entrar?aviso=sessao-expirada e o aviso role=alert recebe o foco", async () => {
    renderApp("/perfil");
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    expect(currentLocation().params.get("aviso")).toBe("sessao-expirada");

    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveFocus());
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    await waitFor(() => expect(document.title.trim()).not.toBe(""));
  });

  it("nenhum dado do Jogador fica visivel apos a expiracao", async () => {
    renderApp("/perfil/editar");
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    await screen.findByRole("alert");
    expect(document.body.textContent ?? "").not.toContain(PLAYER_NAME);
    expect(screen.queryByTestId("player-data")).not.toBeInTheDocument();
  });
});

describe("prefers-reduced-motion nao altera nada aqui (A11Y-37)", () => {
  it.each([false, true])(
    "com reduce=%s: foco no <h1>, titulo e aviso se comportam igual",
    async (reduce) => {
      stubReducedMotion(reduce);

      const first = renderApp("/cadastro");
      await expectH1Present();
      await waitFor(() => expect(document.title.trim()).not.toBe(""));
      first.unmount();

      renderApp("/perfil");
      await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
      const alert = await screen.findByRole("alert");
      await waitFor(() => expect(alert).toHaveFocus());
    },
  );
});
