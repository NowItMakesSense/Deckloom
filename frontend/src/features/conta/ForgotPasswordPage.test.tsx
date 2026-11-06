// T-113 (RED): S6 Esqueci minha senha - email, validacao do cliente, resposta uniforme e sem enumeracao.
// REQ-028, REQ-032; UX-02; ART-02.
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { ForgotPasswordPage } from "./ForgotPasswordPage";
import { fillField } from "@/shared/testing/fillField";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const REQUEST_URL = `${BASE}/api/v1/auth/password-reset/request`;
const EMAIL = "jogadora@exemplo.com";
const UNIFORM =
  "Se existir uma conta com esse email, enviaremos instruções para redefinir a senha. Confira também a caixa de spam.";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  server.resetHandlers();
  vi.restoreAllMocks();
});
afterAll(() => server.close());

interface Harness {
  bodies: { email: string }[];
}

type Responder = () => Response | Promise<Response>;

const accepted: Responder = () => new HttpResponse(null, { status: 202 });

function setup(respond: Responder): Harness {
  const bodies: { email: string }[] = [];
  server.use(
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(SESSION_URL, () =>
      HttpResponse.json(
        { code: "unauthenticated" },
        { status: 401, headers: { "Content-Type": "application/problem+json" } },
      ),
    ),
    http.post(REQUEST_URL, async ({ request }) => {
      bodies.push((await request.json()) as { email: string });
      return respond();
    }),
  );
  return { bodies };
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname + location.search}</p>;
}

function renderPage(respond: Responder = accepted) {
  const harness = setup(respond);
  const view = render(
    <MemoryRouter initialEntries={["/esqueci-senha"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/esqueci-senha" element={<ForgotPasswordPage />} />
          <Route path="/entrar" element={<main>pagina entrar</main>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...harness, ...view };
}

const submitButton = () => screen.getByRole("button", { name: "Enviar instruções" });

describe("S6 Esqueci minha senha - estrutura (REQ-028, ART-02)", () => {
  it("existe: renderiza conteudo (garante que o axe nao passa vazio)", () => {
    const { container } = renderPage();
    expect(container).not.toBeEmptyDOMElement();
    expect(screen.getByRole("heading", { level: 1, name: "Redefinir senha" })).toBeInTheDocument();
  });

  it("tem o campo Email (username), o submit unico e o link Voltar para entrar", () => {
    renderPage();
    const email = screen.getByLabelText("Email");
    expect(email).toHaveAttribute("type", "email");
    expect(email).toHaveAttribute("autocomplete", "username");
    expect(submitButton()).toHaveAttribute("type", "submit");
    expect(screen.getAllByRole("button")).toHaveLength(1);
    expect(screen.getByRole("link", { name: "Voltar para entrar" })).toHaveAttribute(
      "href",
      "/entrar",
    );
  });

  it("na carga o foco NAO vai ao h1 (QA-03, A11Y-23)", async () => {
    renderPage();
    const h1 = screen.getByRole("heading", { level: 1, name: "Redefinir senha" });
    await act(async () => {});
    expect(h1).not.toHaveFocus();
  });

  it("nao ha violacoes de acessibilidade (axe)", async () => {
    const { container } = renderPage();
    expect(screen.getByRole("heading", { level: 1, name: "Redefinir senha" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });

  it("nao usa recurso externo", () => {
    const { container } = renderPage();
    expect(screen.getByRole("heading", { level: 1, name: "Redefinir senha" })).toBeInTheDocument();
    expect(container.querySelectorAll("[src], link[href], script")).toHaveLength(0);
    const hrefs = Array.from(container.querySelectorAll("a[href]")).map((a) =>
      a.getAttribute("href"),
    );
    for (const href of hrefs) expect(href).toMatch(/^\//);
  });
});

describe("S6 Esqueci minha senha - validacao do cliente (REQ-028)", () => {
  it("email vazio mostra a mensagem exata, foca o campo e nao chama o servidor", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await user.click(submitButton());

    expect((await screen.findAllByText("Informe seu email.")).length).toBeGreaterThan(0);
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(screen.getByLabelText("Email")).toHaveFocus());
    expect(screen.queryByText(UNIFORM)).not.toBeInTheDocument();
    expect(bodies).toHaveLength(0);
  });

  it("email invalido mostra a mensagem exata e nao chama o servidor", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await fillField(user, screen.getByLabelText("Email"), "isto-nao-e-email");
    await user.click(submitButton());

    expect(
      (await screen.findAllByText("Informe um email válido, como nome@exemplo.com.")).length,
    ).toBeGreaterThan(0);
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByText(UNIFORM)).not.toBeInTheDocument();
    expect(bodies).toHaveLength(0);
  });
});

describe("S6 Esqueci minha senha - resposta uniforme (REQ-028, REQ-032, UX-02)", () => {
  it("envia POST com {email} e mostra a confirmacao em role=status", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await fillField(user, screen.getByLabelText("Email"), EMAIL);
    await user.click(submitButton());

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent(UNIFORM);
    expect(bodies).toEqual([{ email: EMAIL }]);
    expect(screen.getByTestId("location")).toHaveTextContent("/esqueci-senha");
  });

  it.each([["email existente"], ["email inexistente"], ["conta so com Google"]])(
    "%s: o texto e identico e nao revela a conta",
    async () => {
      renderPage(accepted);
      const user = userEvent.setup();
      await fillField(user, screen.getByLabelText("Email"), EMAIL);
      await user.click(submitButton());

      const status = await screen.findByRole("status");
      expect(status.textContent).toBe(UNIFORM);
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(document.body.textContent).not.toMatch(/n[ãa]o existe|inexistente|Google|encontrad/i);
    },
  );

  it("nao duplica o envio durante a requisicao (aria-busy) e mantem o email digitado", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { bodies } = renderPage(async () => {
      await gate;
      return new HttpResponse(null, { status: 202 });
    });
    const user = userEvent.setup();
    await fillField(user, screen.getByLabelText("Email"), EMAIL);
    const button = submitButton();
    await user.click(button);
    await waitFor(() => expect(button).toHaveAttribute("aria-busy", "true"));
    await user.click(button);
    await user.click(button);
    release();

    await screen.findByRole("status");
    expect(bodies).toHaveLength(1);
    expect(screen.getByLabelText("Email")).toHaveValue(EMAIL);
  });
});

describe("S6 Esqueci minha senha - o email nunca vaza (REQ-028)", () => {
  it("o email nao aparece no console nem na URL", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((method) =>
      vi.spyOn(console, method).mockImplementation(() => undefined),
    );
    renderPage();
    const user = userEvent.setup();
    await fillField(user, screen.getByLabelText("Email"), EMAIL);
    await user.click(submitButton());
    await screen.findByRole("status");

    expect(screen.getByTestId("location").textContent).not.toContain("jogadora");
    expect(window.location.href).not.toContain("jogadora");
    for (const spy of spies) {
      expect(JSON.stringify(spy.mock.calls)).not.toContain(EMAIL);
    }
  });
});
