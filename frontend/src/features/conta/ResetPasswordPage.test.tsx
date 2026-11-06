// T-114 (RED): S7 Redefinir senha - token no fragmento, nova senha, link invalido e sucesso.
// REQ-029, REQ-030, REQ-031, REQ-038; UX-03; UX-11; WC-06; ART-02.
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { ResetPasswordPage } from "./ResetPasswordPage";
import { fillField } from "@/shared/testing/fillField";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const CONFIRM_URL = `${BASE}/api/v1/auth/password-reset/confirm`;
const TOKEN = "tok-abc123_XYZ";
const PASSWORD = "nova-senha-123";
const TOO_SHORT = "A senha precisa ter pelo menos 8 caracteres.";
const TOO_LONG = "A senha pode ter no máximo 128 caracteres.";
const INVALID_TITLE = "Link inválido ou expirado";
const INVALID_TEXT = "Este link de redefinição não pode ser usado. Solicite um novo.";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  server.resetHandlers();
  vi.restoreAllMocks();
  window.history.replaceState(null, "", "/");
});
afterAll(() => server.close());

interface ConfirmBody {
  token: string;
  newPassword: string;
}

type Responder = () => Response | Promise<Response>;

const noContent: Responder = () => new HttpResponse(null, { status: 204 });

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname + location.search}</p>;
}

// O token chega no fragmento da URL real do navegador (nao da rota em memoria).
function renderPage(options: { url?: string; respond?: Responder } = {}) {
  const { url = `/redefinir-senha#token=${TOKEN}`, respond = noContent } = options;
  window.history.replaceState(null, "", url);
  const bodies: ConfirmBody[] = [];
  server.use(
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(SESSION_URL, () => problem(401, { code: "unauthenticated" })),
    http.post(CONFIRM_URL, async ({ request }) => {
      bodies.push((await request.json()) as ConfirmBody);
      return respond();
    }),
  );
  const view = render(
    <MemoryRouter initialEntries={["/redefinir-senha"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/redefinir-senha" element={<ResetPasswordPage />} />
          <Route path="/entrar" element={<main>pagina entrar</main>} />
          <Route path="/esqueci-senha" element={<main>pagina esqueci</main>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { bodies, ...view };
}

const heading = () => screen.getByRole("heading", { level: 1, name: "Criar nova senha" });
const field = () => screen.getByLabelText("Nova senha");
const submitButton = () => screen.getByRole("button", { name: "Salvar nova senha" });

describe("S7 Redefinir senha - estrutura (REQ-029, ART-02)", () => {
  it("existe: renderiza conteudo (garante que o axe nao passa vazio)", () => {
    const { container } = renderPage();
    expect(container).not.toBeEmptyDOMElement();
    expect(heading()).toBeInTheDocument();
  });

  it("tem so o campo Nova senha (new-password) com dica 8 a 128, sem confirmacao, e submit unico", () => {
    renderPage();
    expect(field()).toHaveAttribute("autocomplete", "new-password");
    expect(screen.getByText("de 8 a 128 caracteres", { exact: false })).toBeInTheDocument();
    expect(screen.queryByLabelText(/confirm/i)).not.toBeInTheDocument();
    expect(submitButton()).toHaveAttribute("type", "submit");
    expect(screen.getByRole("button", { name: "Mostrar senha" })).toHaveAttribute("type", "button");
  });

  it("na carga o foco NAO vai ao h1 (QA-03, A11Y-23)", async () => {
    renderPage();
    await act(async () => {});
    expect(heading()).not.toHaveFocus();
  });

  it("nao ha violacoes de acessibilidade (axe) no formulario", async () => {
    const { container } = renderPage();
    expect(heading()).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });

  it("nao usa recurso externo", () => {
    const { container } = renderPage();
    expect(heading()).toBeInTheDocument();
    expect(container.querySelectorAll("[src], link[href], script")).toHaveLength(0);
    for (const a of Array.from(container.querySelectorAll("a[href]"))) {
      expect(a.getAttribute("href")).toMatch(/^\//);
    }
  });
});

describe("S7 Redefinir senha - token no fragmento (REQ-029, UX-11)", () => {
  it("le o token do fragmento e o remove da URL com history.replaceState", async () => {
    const replace = vi.spyOn(window.history, "replaceState");
    renderPage();
    expect(heading()).toBeInTheDocument();
    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(window.location.hash).toBe("");
    expect(window.location.href).not.toContain(TOKEN);

    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/entrar"));
  });

  it("recarregar apos o replaceState (sem fragmento) leva a tela de link invalido", () => {
    const { unmount, bodies } = renderPage();
    expect(heading()).toBeInTheDocument();
    unmount();
    renderPage({ url: window.location.pathname + window.location.search });
    expect(screen.getByRole("heading", { level: 1, name: INVALID_TITLE })).toBeInTheDocument();
    expect(bodies).toHaveLength(0);
  });

  it("token em query string nunca e aceito: tela de link invalido", () => {
    const { bodies } = renderPage({ url: `/redefinir-senha?token=${TOKEN}` });
    expect(screen.getByRole("heading", { level: 1, name: INVALID_TITLE })).toBeInTheDocument();
    expect(screen.queryByLabelText("Nova senha")).not.toBeInTheDocument();
    expect(bodies).toHaveLength(0);
  });
});

describe("S7 Redefinir senha - link invalido sem token (REQ-031, UX-03)", () => {
  it("sem token: tela de link invalido, sem formulario, com 'Solicitar novo link' para /esqueci-senha", async () => {
    const { bodies, container } = renderPage({ url: "/redefinir-senha" });
    const h1 = screen.getByRole("heading", { level: 1, name: INVALID_TITLE });
    await act(async () => {});
    expect(h1).not.toHaveFocus();
    expect(screen.getByText(INVALID_TEXT)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Solicitar novo link" })).toHaveAttribute(
      "href",
      "/esqueci-senha",
    );
    expect(screen.queryByLabelText("Nova senha")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Salvar nova senha" })).not.toBeInTheDocument();
    expect(bodies).toHaveLength(0);
    await expectNoA11yViolations(container);
  });

  it("fragmento sem valor de token (#token=) tambem e link invalido", () => {
    renderPage({ url: "/redefinir-senha#token=" });
    expect(screen.getByRole("heading", { level: 1, name: INVALID_TITLE })).toBeInTheDocument();
  });
});

describe("S7 Redefinir senha - validacao do cliente (REQ-038, UX-03)", () => {
  it("senha curta: mensagem exata, campo limpo com foco, sem chamada ao servidor", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await fillField(user, field(), "curta");
    await user.click(submitButton());

    expect((await screen.findAllByText(TOO_SHORT)).length).toBeGreaterThan(0);
    expect(field()).toHaveAttribute("aria-invalid", "true");
    expect(field()).toHaveValue("");
    await waitFor(() => expect(field()).toHaveFocus());
    expect(bodies).toHaveLength(0);
  });

  it("senha vazia: mensagem de campo obrigatorio e sem chamada ao servidor", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await user.click(submitButton());

    expect((await screen.findAllByText("Informe uma senha.")).length).toBeGreaterThan(0);
    expect(field()).toHaveAttribute("aria-invalid", "true");
    expect(bodies).toHaveLength(0);
  });

  it("senha com 129 caracteres: mensagem exata de maximo e o token continua aceito num segundo envio valido", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Mostrar senha" }));
    await user.click(field());
    await user.paste("a".repeat(129));
    await user.click(submitButton());

    expect((await screen.findAllByText(TOO_LONG)).length).toBeGreaterThan(0);
    expect(field()).toHaveValue("");
    expect(bodies).toHaveLength(0);

    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());
    await waitFor(() => expect(bodies).toEqual([{ token: TOKEN, newPassword: PASSWORD }]));
  });

  it("senha de 128 caracteres com 'Mostrar' visivel e aceita (WC-06)", async () => {
    const long = "b".repeat(128);
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Mostrar senha" }));
    expect(field()).toHaveAttribute("type", "text");
    await user.click(field());
    await user.paste(long);
    expect(field()).toHaveValue(long);
    await user.click(submitButton());
    await waitFor(() => expect(bodies).toEqual([{ token: TOKEN, newPassword: long }]));
  });
});

describe("S7 Redefinir senha - envio e sucesso (REQ-030)", () => {
  it("POST com {token, newPassword}; 204 leva a /entrar?aviso=senha-alterada, sem token na URL", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());

    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=senha-alterada"),
    );
    expect(bodies).toEqual([{ token: TOKEN, newPassword: PASSWORD }]);
    expect(window.location.href).not.toContain(TOKEN);
    expect(window.location.hash).toBe("");
  });

  it("nao duplica o envio durante a requisicao (aria-busy)", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { bodies } = renderPage({
      respond: async () => {
        await gate;
        return new HttpResponse(null, { status: 204 });
      },
    });
    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    const button = submitButton();
    await user.click(button);
    await waitFor(() => expect(button).toHaveAttribute("aria-busy", "true"));
    await user.click(button);
    await user.click(button);
    release();

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/entrar"));
    expect(bodies).toHaveLength(1);
  });
});

describe("S7 Redefinir senha - erros do servidor (REQ-031, REQ-038, UX-03)", () => {
  it("400 reset_link_invalid: tela de link invalido, sem detalhar o motivo, com 'Solicitar novo link'", async () => {
    renderPage({
      respond: () =>
        problem(400, { code: "reset_link_invalid", detail: "TOKEN-EXPIRADO-DO-SERVIDOR" }),
    });
    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());

    expect(
      await screen.findByRole("heading", { level: 1, name: INVALID_TITLE }),
    ).toBeInTheDocument();
    expect(screen.getByText(INVALID_TEXT)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Solicitar novo link" })).toHaveAttribute(
      "href",
      "/esqueci-senha",
    );
    expect(screen.queryByLabelText("Nova senha")).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(
      /TOKEN-EXPIRADO|expirou|j[áa] foi usado|usado anteriormente|revogado/i,
    );
    expect(screen.getByTestId("location")).toHaveTextContent("/redefinir-senha");
  });

  it("400 validation_failed com errors.newPassword: erro no campo, campo limpo e o mesmo token no reenvio", async () => {
    let calls = 0;
    const { bodies } = renderPage({
      respond: () => {
        calls += 1;
        return calls === 1
          ? problem(400, {
              code: "validation_failed",
              detail: "TEXTO-DO-SERVIDOR",
              errors: { newPassword: ["too_long"] },
            })
          : new HttpResponse(null, { status: 204 });
      },
    });
    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());

    expect((await screen.findAllByText(TOO_LONG)).length).toBeGreaterThan(0);
    expect(field()).toHaveAttribute("aria-invalid", "true");
    expect(field()).toHaveValue("");
    expect(document.body.textContent).not.toContain("TEXTO-DO-SERVIDOR");
    expect(screen.queryByRole("heading", { name: INVALID_TITLE })).not.toBeInTheDocument();

    await fillField(user, field(), "outra-senha-ok");
    await user.click(submitButton());
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/entrar"));
    expect(bodies).toEqual([
      { token: TOKEN, newPassword: PASSWORD },
      { token: TOKEN, newPassword: "outra-senha-ok" },
    ]);
  });

  it("400 validation_failed com errors.newPassword too_common: mensagem no campo e o formulario continua na pagina", async () => {
    renderPage({
      respond: () =>
        problem(400, { code: "validation_failed", errors: { newPassword: ["too_common"] } }),
    });
    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());

    const message = "Essa senha é muito comum. Escolha uma senha menos previsível.";
    expect((await screen.findAllByText(message)).length).toBeGreaterThan(0);
    expect(field()).toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("heading", { name: INVALID_TITLE })).not.toBeInTheDocument();
    expect(screen.getByTestId("location")).toHaveTextContent("/redefinir-senha");
  });
});

describe("S7 Redefinir senha - segredos nunca vazam (REQ-029)", () => {
  it("a senha e o token nao aparecem no console nem na URL", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((method) =>
      vi.spyOn(console, method).mockImplementation(() => undefined),
    );
    renderPage();
    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/entrar"));

    expect(screen.getByTestId("location").textContent).not.toContain(PASSWORD);
    expect(window.location.href).not.toContain(PASSWORD);
    expect(window.location.href).not.toContain(TOKEN);
    for (const spy of spies) {
      const logged = JSON.stringify(spy.mock.calls);
      expect(logged).not.toContain(PASSWORD);
      expect(logged).not.toContain(TOKEN);
    }
  });
});

describe("S7 Redefinir senha - titulo por estado da tela (U-08, A11Y-20)", () => {
  const INVALID_DOCUMENT_TITLE = `${INVALID_TITLE} - Deckloom`;

  it("com token o titulo do documento nao e o do link invalido", () => {
    document.title = "";
    renderPage();
    expect(heading()).toBeInTheDocument();
    expect(document.title).not.toBe(INVALID_DOCUMENT_TITLE);
  });

  it("sem token o titulo do documento e o do <h1> do link invalido", async () => {
    document.title = "Criar nova senha - Deckloom";
    renderPage({ url: "/redefinir-senha" });
    await screen.findByRole("heading", { level: 1, name: INVALID_TITLE });
    await waitFor(() => expect(document.title).toBe(INVALID_DOCUMENT_TITLE));
  });

  it("depois de reset_link_invalid o titulo do documento passa a ser o do link invalido", async () => {
    document.title = "Criar nova senha - Deckloom";
    renderPage({ respond: () => problem(400, { code: "reset_link_invalid" }) });
    const user = userEvent.setup();
    await fillField(user, field(), PASSWORD);
    await user.click(submitButton());
    await screen.findByRole("heading", { level: 1, name: INVALID_TITLE });
    await waitFor(() => expect(document.title).toBe(INVALID_DOCUMENT_TITLE));
  });
});
