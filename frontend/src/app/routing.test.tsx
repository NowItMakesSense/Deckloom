// T-106 (RED): retorno seguro, SessionProvider, guards e rotas (REQ-010, REQ-011, REQ-012).
import { render, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { AppRoutes, ROUTE_PATTERNS } from "./AppRoutes";
import { GuestRoute, PlayerRoute } from "./guards";
import { KNOWN_ROUTES, safeReturnTo } from "./routes";
import { SessionProvider, useApiClient, useRefreshSession, useSession } from "./SessionProvider";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const PROFILE_URL = `${BASE}/api/v1/me/profile`;
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

function unauthenticated() {
  return HttpResponse.json(
    { code: "unauthenticated" },
    { status: 401, headers: { "Content-Type": "application/problem+json" } },
  );
}

function withSession() {
  server.use(http.get(SESSION_URL, () => HttpResponse.json(summary)));
}

function withoutSession() {
  server.use(http.get(SESSION_URL, unauthenticated));
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname + location.search}</p>;
}

function currentLocation(): { path: string; params: URLSearchParams } {
  const text = screen.getByTestId("location").textContent ?? "";
  const url = new URL(text, BASE);
  return { path: url.pathname, params: url.searchParams };
}

function renderAt(entry: string, routes: ReactNode) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProvider baseUrl={BASE}>
        {routes}
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
}

describe("safeReturnTo (REQ-010, UX-12, A10)", () => {
  it("aceita caminho interno conhecido", () => {
    expect(safeReturnTo("/perfil/editar")).toBe("/perfil/editar");
    expect(safeReturnTo("/perfil")).toBe("/perfil");
  });

  it.each([
    "//evil.example",
    "/\\evil.example",
    "\\evil.example",
    "https://evil.example/perfil",
    "javascript:alert(1)",
    "perfil",
    "/rota-desconhecida",
    "",
    null,
    undefined,
  ])("retorna /perfil para valor inseguro ou desconhecido: %s", (value) => {
    expect(safeReturnTo(value as string | null | undefined)).toBe("/perfil");
  });

  it("a lista de rotas conhecidas contem /perfil e /perfil/editar e nenhuma rota insegura", () => {
    expect(KNOWN_ROUTES).toContain("/perfil");
    expect(KNOWN_ROUTES).toContain("/perfil/editar");
    for (const route of KNOWN_ROUTES) {
      expect(route.startsWith("/")).toBe(true);
      expect(route.startsWith("//")).toBe(false);
      expect(route).not.toContain("\\");
    }
  });
});

describe("SessionProvider (REQ-011, REQ-012)", () => {
  function Probe() {
    const session = useSession();
    return (
      <p data-testid="state">
        {session.status === "player" ? `player:${session.player.displayName}` : session.status}
      </p>
    );
  }

  it("consulta GET /api/v1/auth/session e expoe o jogador", async () => {
    withSession();
    renderAt("/qualquer", <Probe />);
    await waitFor(() =>
      expect(screen.getByTestId("state")).toHaveTextContent("player:Jogadora Teste"),
    );
  });

  it("expoe visitante quando a sessao responde 401", async () => {
    withoutSession();
    renderAt("/qualquer", <Probe />);
    await waitFor(() => expect(screen.getByTestId("state")).toHaveTextContent("guest"));
  });

  it("refresh() refaz GET /auth/session e atualiza o estado (T-107)", async () => {
    withoutSession();
    function RefreshProbe() {
      const refresh = useRefreshSession();
      return (
        <button type="button" onClick={() => void refresh()}>
          atualizar
        </button>
      );
    }
    renderAt(
      "/qualquer",
      <>
        <Probe />
        <RefreshProbe />
      </>,
    );
    await waitFor(() => expect(screen.getByTestId("state")).toHaveTextContent("guest"));

    withSession();
    screen.getByRole("button", { name: "atualizar" }).click();
    await waitFor(() =>
      expect(screen.getByTestId("state")).toHaveTextContent("player:Jogadora Teste"),
    );
  });
});

describe("guards (REQ-010, REQ-011, REQ-012)", () => {
  const playerTree = (
    <Routes>
      <Route element={<PlayerRoute />}>
        <Route path="/perfil" element={<p>pagina perfil</p>} />
        <Route path="/perfil/editar" element={<p>pagina editar</p>} />
      </Route>
      <Route path="/entrar" element={<p>pagina entrar</p>} />
    </Routes>
  );

  it("rota de Jogador sem sessao redireciona para /entrar com aviso e retorno exatos", async () => {
    withoutSession();
    renderAt("/perfil/editar", playerTree);
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    const { params } = currentLocation();
    expect(params.get("aviso")).toBe("sessao-expirada");
    expect(params.get("retorno")).toBe("/perfil/editar");
    expect(screen.queryByText("pagina editar")).not.toBeInTheDocument();
  });

  it("rota de Jogador com sessao renderiza a pagina", async () => {
    withSession();
    renderAt("/perfil", playerTree);
    expect(await screen.findByText("pagina perfil")).toBeInTheDocument();
    expect(currentLocation().path).toBe("/perfil");
  });

  it.each(["/cadastro", "/entrar", "/esqueci-senha", "/redefinir-senha"])(
    "rota de visitante %s com sessao ativa redireciona para /perfil",
    async (path) => {
      withSession();
      renderAt(
        path,
        <Routes>
          <Route element={<GuestRoute />}>
            <Route path={path} element={<p>pagina visitante</p>} />
          </Route>
          <Route path="/perfil" element={<p>pagina perfil</p>} />
        </Routes>,
      );
      await waitFor(() => expect(currentLocation().path).toBe("/perfil"));
      expect(screen.queryByText("pagina visitante")).not.toBeInTheDocument();
    },
  );

  it("rota de visitante sem sessao renderiza a pagina", async () => {
    withoutSession();
    renderAt(
      "/cadastro",
      <Routes>
        <Route element={<GuestRoute />}>
          <Route path="/cadastro" element={<p>pagina visitante</p>} />
        </Route>
      </Routes>,
    );
    expect(await screen.findByText("pagina visitante")).toBeInTheDocument();
  });

  it("401 em qualquer chamada leva ao login sem exibir dados do jogador", async () => {
    withSession();
    server.use(http.get(PROFILE_URL, unauthenticated));
    function ProfilePage() {
      const client = useApiClient();
      const session = useSession();
      return (
        <button
          type="button"
          onClick={() => {
            client.get("/api/v1/me/profile").catch(() => undefined);
          }}
        >
          carregar {session.status === "player" ? session.player.displayName : ""}
        </button>
      );
    }
    renderAt(
      "/perfil",
      <Routes>
        <Route element={<PlayerRoute />}>
          <Route path="/perfil" element={<ProfilePage />} />
        </Route>
        <Route path="/entrar" element={<p>pagina entrar</p>} />
      </Routes>,
    );
    const button = await screen.findByRole("button");
    button.click();
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    const { params } = currentLocation();
    expect(params.get("aviso")).toBe("sessao-expirada");
    expect(params.get("retorno")).toBe("/perfil");
    expect(screen.queryByText(/Jogadora Teste/)).not.toBeInTheDocument();
  });
});

describe("tabela de rotas (REQ-013)", () => {
  it("nao existe rota com identificador de outro Jogador", () => {
    expect(ROUTE_PATTERNS.length).toBeGreaterThan(0);
    for (const pattern of ROUTE_PATTERNS) {
      expect(pattern).not.toContain(":");
      expect(pattern).not.toContain("*");
    }
    expect(ROUTE_PATTERNS).toContain("/perfil");
    expect(ROUTE_PATTERNS).toContain("/perfil/editar");
  });

  it("AppRoutes sem sessao em /perfil leva a /entrar?aviso=sessao-expirada&retorno=/perfil", async () => {
    withoutSession();
    renderAt("/perfil", <AppRoutes />);
    await waitFor(() => expect(currentLocation().path).toBe("/entrar"));
    expect(currentLocation().params.get("aviso")).toBe("sessao-expirada");
    expect(currentLocation().params.get("retorno")).toBe("/perfil");
  });

  it("AppRoutes com sessao em /cadastro leva a /perfil", async () => {
    withSession();
    renderAt("/cadastro", <AppRoutes />);
    await waitFor(() => expect(currentLocation().path).toBe("/perfil"));
  });
});
