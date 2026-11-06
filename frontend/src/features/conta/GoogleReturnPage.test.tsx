// T-110 (RED): S3 Retorno do Google - le `resultado` e redireciona (design S3).
// REQ-006, REQ-007, REQ-008, REQ-009, REQ-023, REQ-042; A11Y-34; MOT-08; MOT-10; UX-12.
import { act, render, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { GoogleReturnPage } from "./GoogleReturnPage";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const PENDING_URL = `${BASE}/api/v1/auth/google/pending`;
const GOOGLE_FAILED = "/entrar?aviso=google-falhou";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname + location.search}</p>;
}

// S4 e S5 sao rotas proprias (T-111/T-112): aqui so se confere o destino.
function renderPage(query: string) {
  const counters = { session: 0, pending: 0 };
  server.use(
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(SESSION_URL, () => {
      counters.session += 1;
      return HttpResponse.json({
        displayName: "Jogador",
        avatar: null,
        hasPassword: true,
        hasGoogle: true,
      });
    }),
    http.get(PENDING_URL, () => {
      counters.pending += 1;
      return HttpResponse.json({ kind: "signup", email: "a@b.com", suggestedDisplayName: "X" });
    }),
  );
  const view = render(
    <MemoryRouter initialEntries={[`/entrar/google/retorno${query}`]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/entrar/google/retorno" element={<GoogleReturnPage />} />
          <Route path="*" element={<main>destino</main>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { counters, ...view };
}

const location = () => screen.getByTestId("location");
const heading = () =>
  screen.getByRole("heading", { level: 1, name: "Concluindo o login com Google" });

describe("S3 Retorno do Google - estrutura (MOT-08, A11Y-34)", () => {
  it("existe: h1, spinner em role=status com 'Aguarde um instante...' e sem formulario", () => {
    const { container } = renderPage("?resultado=ok");
    expect(container).not.toBeEmptyDOMElement();
    expect(heading()).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("Aguarde um instante...");
    expect(container.querySelector("form")).toBeNull();
  });

  it("na carga o foco NAO vai ao h1 (QA-03, A11Y-23)", async () => {
    renderPage("?resultado=ok");
    const h1 = heading();
    await act(async () => {});
    expect(h1).not.toHaveFocus();
  });

  it("nao ha violacoes de acessibilidade (axe) enquanto conclui", async () => {
    const { container } = renderPage("?resultado=ok");
    expect(heading()).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("S3 Retorno do Google - destinos (REQ-006 a 009, 023, 042)", () => {
  it("ok: revalida a sessao e vai a /perfil", async () => {
    const { counters } = renderPage("?resultado=ok");
    await waitFor(() => expect(location()).toHaveTextContent(/^\/perfil$/));
    expect(counters.session).toBeGreaterThanOrEqual(2);
  });

  it("ok com retorno interno conhecido vai ao retorno", async () => {
    renderPage("?resultado=ok&retorno=/perfil/editar");
    await waitFor(() => expect(location()).toHaveTextContent(/^\/perfil\/editar$/));
  });

  it.each(["https://evil.example/x", "//evil.example", "/rota-desconhecida"])(
    "ok com retorno externo/desconhecido (%s) e ignorado: vai a /perfil (UX-12)",
    async (retorno) => {
      renderPage(`?resultado=ok&retorno=${encodeURIComponent(retorno)}`);
      await waitFor(() => expect(location()).toHaveTextContent(/^\/perfil$/));
      expect(location().textContent).not.toContain("evil");
      expect(location().textContent).not.toContain("desconhecida");
    },
  );

  it("concluir: vai a S4 (/entrar/google/concluir) sem consultar a pendencia", async () => {
    const { counters } = renderPage("?resultado=concluir");
    await waitFor(() => expect(location()).toHaveTextContent("/entrar/google/concluir"));
    expect(counters.pending).toBe(0);
  });

  it("vincular: vai a S5 (/entrar/google/vincular) sem consultar a pendencia", async () => {
    const { counters } = renderPage("?resultado=vincular");
    await waitFor(() => expect(location()).toHaveTextContent("/entrar/google/vincular"));
    expect(counters.pending).toBe(0);
  });

  it("reautenticado: vai a /perfil?exclusao=confirmar", async () => {
    renderPage("?resultado=reautenticado");
    await waitFor(() => expect(location()).toHaveTextContent("/perfil?exclusao=confirmar"));
  });

  it("reautenticacao-falhou: vai a /perfil?exclusao=falhou", async () => {
    renderPage("?resultado=reautenticacao-falhou");
    await waitFor(() => expect(location()).toHaveTextContent("/perfil?exclusao=falhou"));
  });

  it("falhou: vai a /entrar?aviso=google-falhou", async () => {
    renderPage("?resultado=falhou");
    await waitFor(() => expect(location()).toHaveTextContent(GOOGLE_FAILED));
  });

  it("resultado ausente: vai a /entrar?aviso=google-falhou", async () => {
    renderPage("");
    await waitFor(() => expect(location()).toHaveTextContent(GOOGLE_FAILED));
  });

  it("resultado desconhecido: vai a /entrar?aviso=google-falhou", async () => {
    renderPage("?resultado=qualquer-coisa");
    await waitFor(() => expect(location()).toHaveTextContent(GOOGLE_FAILED));
  });
});

describe("S3 Retorno do Google - sem dado sensivel (REQ-006)", () => {
  it("token, subject e codigo na URL nao vao ao destino nem ao DOM", async () => {
    renderPage("?resultado=falhou&token=SEGREDO-TOKEN&sub=SUBJECT-123&code=CODE-9");
    await waitFor(() => expect(location()).toHaveTextContent(GOOGLE_FAILED));
    for (const secret of ["SEGREDO-TOKEN", "SUBJECT-123", "CODE-9"]) {
      expect(location().textContent).not.toContain(secret);
      expect(document.body.textContent).not.toContain(secret);
    }
  });
});
