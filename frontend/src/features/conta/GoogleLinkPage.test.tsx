// T-112 (RED): S5 Vincular Google com senha.
// REQ-008, REQ-032, REQ-036, REQ-038; WC-06, WC-12, ART-02; A11Y (foco, axe, status).
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { messageFor } from "../../shared/messages/messages";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { GoogleLinkPage } from "./GoogleLinkPage";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const PENDING_URL = `${BASE}/api/v1/auth/google/pending`;
const LINK_URL = `${BASE}/api/v1/auth/google/link`;
const GOOGLE_EMAIL = "google.jogadora@exemplo.com";
const SECRET_SUBJECT = "SUBJECT-SECRETO-123";
const SECRET_TOKEN = "TOKEN-SECRETO-456";
const PASSWORD = "SenhaDaConta#2026";
// Textos da spec (design S5 / tasks T-112).
const H1 = "Vincular sua conta Google";
const SUBMIT = "Vincular e entrar";
const REQUIRED_MSG = "Informe a senha da conta.";
const TOO_LONG_MSG = "A senha pode ter no máximo 128 caracteres.";
const INCORRECT_MSG = "Senha incorreta. O Google não foi vinculado.";
const SUCCESS_MSG = "Google vinculado. Da próxima vez você pode entrar com o Google.";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function pendingOk(email = GOOGLE_EMAIL) {
  return HttpResponse.json({
    kind: "link",
    email,
    subject: SECRET_SUBJECT,
    token: SECRET_TOKEN,
  });
}

function baseHandlers(pending: () => Response | Promise<Response> = () => pendingOk()) {
  server.use(
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(SESSION_URL, () => problem(401, { code: "unauthenticated" })),
    http.get(PENDING_URL, () => pending()),
  );
}

function captureLink(
  respond: () => Response | Promise<Response> = () =>
    HttpResponse.json({
      displayName: "Jogadora Teste",
      avatar: null,
      hasPassword: true,
      hasGoogle: true,
    }),
) {
  const bodies: unknown[] = [];
  server.use(
    http.post(LINK_URL, async ({ request }) => {
      bodies.push(await request.json());
      return respond();
    }),
  );
  return bodies;
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>;
}

function PerfilStub() {
  const location = useLocation();
  const message = (location.state as { message?: string } | null)?.message;
  return (
    <main>
      <p>pagina perfil</p>
      {message ? <p role="status">{message}</p> : null}
    </main>
  );
}

function renderPage(pending?: () => Response | Promise<Response>) {
  baseHandlers(pending);
  return render(
    <MemoryRouter initialEntries={["/entrar/google/vincular"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/entrar/google/vincular" element={<GoogleLinkPage />} />
          <Route path="/perfil" element={<PerfilStub />} />
          <Route path="/entrar" element={<p>pagina entrar</p>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
}

async function renderReady(pending?: () => Response | Promise<Response>) {
  const view = renderPage(pending);
  await screen.findByLabelText("Senha");
  return view;
}

async function typePasswordAndSubmit(user: ReturnType<typeof userEvent.setup>, value: string) {
  const field = screen.getByLabelText("Senha");
  await user.click(field);
  if (value) await user.paste(value);
  await user.click(screen.getByRole("button", { name: SUBMIT }));
}

describe("S5 - consulta da pendencia (REQ-008)", () => {
  it("mostra h1 e estado de carregamento role=status, sem formulario, enquanto consulta (WC-12)", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    renderPage(async () => {
      await gate;
      return pendingOk();
    });
    expect(screen.getByRole("heading", { level: 1, name: H1 })).toBeInTheDocument();
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByLabelText("Senha")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: SUBMIT })).not.toBeInTheDocument();
    release();
    expect(await screen.findByLabelText("Senha")).toBeInTheDocument();
  });

  it("404 pending_not_found na consulta vai a /entrar?aviso=google-falhou", async () => {
    renderPage(() => problem(404, { code: "pending_not_found" }));
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=google-falhou"),
    );
  });
});

describe("S5 - estrutura (REQ-036, WC-06, ART-02)", () => {
  it("existe: renderiza conteudo (garante que o axe nao passa vazio)", async () => {
    const { container } = await renderReady();
    expect(container).not.toBeEmptyDOMElement();
    expect(screen.getByRole("heading", { level: 1, name: H1 })).toBeInTheDocument();
  });

  it("mostra o email do Google em texto (nao editavel), campo de senha current-password e Mostrar senha", async () => {
    await renderReady();
    expect(screen.getByText(GOOGLE_EMAIL, { exact: false })).toBeInTheDocument();
    expect(screen.queryByLabelText("Email")).not.toBeInTheDocument();
    expect(document.querySelector('input[type="email"]')).toBeNull();
    for (const input of Array.from(document.querySelectorAll("input"))) {
      expect(input.value).not.toBe(GOOGLE_EMAIL);
    }
    expect(screen.getByLabelText("Senha")).toHaveAttribute("autocomplete", "current-password");
    expect(screen.getByRole("button", { name: "Mostrar senha" })).toBeInTheDocument();
  });

  it("tem Vincular e entrar (submit), Esqueci minha senha, Cancelar e nenhum campo de confirmacao", async () => {
    await renderReady();
    expect(screen.getByRole("button", { name: SUBMIT })).toHaveAttribute("type", "submit");
    expect(screen.getByRole("link", { name: "Esqueci minha senha" })).toHaveAttribute(
      "href",
      "/esqueci-senha",
    );
    expect(screen.getByRole("button", { name: "Cancelar" })).toBeInTheDocument();
    expect(screen.queryByLabelText(/confirm/i)).not.toBeInTheDocument();
    expect(document.querySelectorAll('input[type="password"]')).toHaveLength(1);
  });

  it("email verificado de 254 caracteres aparece inteiro (WC-06)", async () => {
    const longEmail = `${"a".repeat(64)}@${"d".repeat(63)}.${"d".repeat(63)}.${"d".repeat(55)}.com`;
    await renderReady(() => pendingOk(longEmail));
    expect(screen.getByText(longEmail, { exact: false })).toBeInTheDocument();
  });

  it("na carga o foco NAO vai ao h1 (QA-03, A11Y-23)", async () => {
    renderPage();
    const h1 = screen.getByRole("heading", { level: 1, name: H1 });
    await act(async () => {});
    expect(h1).not.toHaveFocus();
  });

  it("token e subject nunca aparecem na URL nem no DOM; nenhum recurso externo", async () => {
    const { container } = await renderReady();
    expect(screen.getByTestId("location")).not.toHaveTextContent(SECRET_SUBJECT);
    expect(screen.getByTestId("location")).not.toHaveTextContent(SECRET_TOKEN);
    expect(document.body.innerHTML).not.toContain(SECRET_SUBJECT);
    expect(document.body.innerHTML).not.toContain(SECRET_TOKEN);
    for (const el of Array.from(container.querySelectorAll("[src], link[href]"))) {
      const url = el.getAttribute("src") ?? el.getAttribute("href") ?? "";
      expect(url).not.toMatch(/^(https?:)?\/\//);
    }
    expect(container.querySelector("img")).toBeNull();
  });

  it("nao ha violacoes de acessibilidade (axe)", async () => {
    const { container } = await renderReady();
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
    expect(screen.getByLabelText("Senha")).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("S5 - validacao do cliente (REQ-038)", () => {
  it("senha vazia mostra 'Informe a senha da conta.' e nao chama o servidor", async () => {
    const bodies = captureLink();
    const user = userEvent.setup();
    await renderReady();
    await user.click(screen.getByRole("button", { name: SUBMIT }));
    await waitFor(() => expect(screen.getAllByText(REQUIRED_MSG).length).toBeGreaterThan(0));
    expect(screen.getByLabelText("Senha")).toHaveAttribute("aria-invalid", "true");
    expect(bodies).toHaveLength(0);
  });

  it("senha de 129 caracteres mostra o erro de tamanho e nao chama o servidor", async () => {
    const bodies = captureLink();
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, "s".repeat(129));
    await waitFor(() => expect(screen.getAllByText(TOO_LONG_MSG).length).toBeGreaterThan(0));
    expect(bodies).toHaveLength(0);
  });

  it("senha de 128 caracteres e enviada inteira e Mostrar senha continua visivel (WC-06)", async () => {
    const bodies = captureLink();
    const user = userEvent.setup();
    await renderReady();
    expect(screen.getByRole("button", { name: "Mostrar senha" })).toBeVisible();
    await typePasswordAndSubmit(user, "s".repeat(128));
    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({ password: "s".repeat(128) });
  });
});

describe("S5 - envio e sucesso (REQ-008, REQ-036)", () => {
  it("envia somente {password}", async () => {
    const bodies = captureLink();
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);
    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({ password: PASSWORD });
  });

  it("durante o envio o botao fica aria-busy e nao duplica a requisicao", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const bodies = captureLink(async () => {
      await gate;
      return HttpResponse.json({ displayName: "x", avatar: null });
    });
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);
    const button = screen.getByRole("button", { name: SUBMIT });
    await waitFor(() => expect(button).toHaveAttribute("aria-busy", "true"));
    await user.click(button);
    await user.click(button);
    release();
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(bodies).toHaveLength(1);
  });

  it("200 leva a /perfil com a mensagem de vinculo em state.message (role=status)", async () => {
    captureLink();
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(await screen.findByRole("status")).toHaveTextContent(SUCCESS_MSG);
    expect(screen.getByTestId("location")).not.toHaveTextContent(PASSWORD);
    expect(document.body.textContent).not.toContain(PASSWORD);
  });
});

describe("S5 - erros do servidor (REQ-032, REQ-036, REQ-038)", () => {
  it("401 incorrect_password: erro no campo, campo limpo com foco, pendencia segue, sem texto do servidor", async () => {
    captureLink(() => problem(401, { code: "incorrect_password", detail: "TEXTO-DO-SERVIDOR" }));
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);

    await waitFor(() => expect(screen.getAllByText(INCORRECT_MSG).length).toBeGreaterThan(0));
    const field = screen.getByLabelText("Senha");
    expect(field).toHaveValue("");
    await waitFor(() => expect(field).toHaveFocus());
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar/google/vincular");
    expect(screen.getByText(GOOGLE_EMAIL, { exact: false })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: SUBMIT })).toBeInTheDocument();
    expect(document.body.textContent).not.toContain("TEXTO-DO-SERVIDOR");
    expect(document.body.textContent).not.toContain(PASSWORD);
    expect(messageFor("incorrect_password")).toBeTruthy();
  });

  it("429 too_many_attempts mostra o alerta com a mensagem aprovada e mantem a pendencia", async () => {
    captureLink(() => problem(429, { code: "too_many_attempts" }));
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Muitas tentativas. Aguarde alguns minutos e tente de novo.");
    expect(document.body.textContent).not.toContain(INCORRECT_MSG);
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar/google/vincular");
    expect(screen.getByRole("button", { name: SUBMIT })).toBeInTheDocument();
  });

  it("404 pending_not_found no envio vai a /entrar?aviso=google-falhou", async () => {
    captureLink(() => problem(404, { code: "pending_not_found" }));
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=google-falhou"),
    );
  });

  it("409 google_not_completed mostra a mensagem e mantem a tela", async () => {
    captureLink(() => problem(409, { code: "google_not_completed" }));
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);
    await waitFor(() =>
      expect(screen.getAllByText(messageFor("google_not_completed")).length).toBeGreaterThan(0),
    );
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar/google/vincular");
  });

  it("400 validation_failed mapeia erro de campo e nunca mostra o texto do servidor", async () => {
    captureLink(() =>
      problem(400, {
        code: "validation_failed",
        detail: "TEXTO-DO-SERVIDOR",
        errors: { password: ["too_long"] },
      }),
    );
    const user = userEvent.setup();
    await renderReady();
    await typePasswordAndSubmit(user, PASSWORD);
    await waitFor(() => expect(screen.getAllByText(TOO_LONG_MSG).length).toBeGreaterThan(0));
    expect(screen.getByLabelText("Senha")).toHaveAttribute("aria-invalid", "true");
    expect(document.body.textContent).not.toContain("TEXTO-DO-SERVIDOR");
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar/google/vincular");
  });
});

describe("S5 - cancelar", () => {
  it("Cancelar chama DELETE /auth/google/pending uma vez e navega para /entrar", async () => {
    let deleted = 0;
    server.use(
      http.delete(PENDING_URL, () => {
        deleted += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    await renderReady();
    await user.click(screen.getByRole("button", { name: "Cancelar" }));

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^\/entrar$/));
    expect(screen.getByTestId("location")).not.toHaveTextContent("/entrar/google");
    expect(deleted).toBe(1);
  });
});
