// T-111 (RED): S4 Concluir cadastro com Google.
// REQ-006, REQ-033, REQ-034, REQ-039, REQ-040, REQ-043, REQ-044, REQ-048; A11Y-02/04/05/22/24/25; ART-02.
import { render, screen, waitFor, within, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { messageFor } from "../../shared/messages/messages";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { GoogleSignupPage } from "./GoogleSignupPage";
import { fillField } from "@/shared/testing/fillField";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const TERMS_URL = `${BASE}/api/v1/legal/terms`;
const PENDING_URL = `${BASE}/api/v1/auth/google/pending`;
const COMPLETE_URL = `${BASE}/api/v1/auth/google/complete-signup`;
const TERMS_VERSION = "2026-10-01";
const GOOGLE_EMAIL = "google.jogadora@exemplo.com";
const GOOGLE_NAME = "Nome do Google";
const SECRET_SUBJECT = "SUBJECT-SECRETO-123";
const SECRET_TOKEN = "TOKEN-SECRETO-456";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

interface CompleteBody {
  displayName: string;
  acceptTerms: boolean;
  termsVersion: string;
  adultDeclaration: boolean;
}

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function pendingOk(suggestedDisplayName = GOOGLE_NAME) {
  return HttpResponse.json({
    kind: "signup",
    email: GOOGLE_EMAIL,
    suggestedDisplayName,
    subject: SECRET_SUBJECT,
    token: SECRET_TOKEN,
  });
}

function baseHandlers(pending: () => Response | Promise<Response> = () => pendingOk()) {
  server.use(
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(SESSION_URL, () => problem(401, { code: "unauthenticated" })),
    http.get(TERMS_URL, () =>
      HttpResponse.json({
        version: TERMS_VERSION,
        termsUrl: "/termos",
        privacyUrl: "/privacidade",
      }),
    ),
    http.get(PENDING_URL, () => pending()),
  );
}

function captureComplete(
  respond: () => Response | Promise<Response> = () =>
    HttpResponse.json(
      { displayName: "Jogadora Teste", avatar: null, hasPassword: false, hasGoogle: true },
      { status: 201 },
    ),
) {
  const bodies: CompleteBody[] = [];
  server.use(
    http.post(COMPLETE_URL, async ({ request }) => {
      bodies.push((await request.json()) as CompleteBody);
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
    <MemoryRouter initialEntries={["/entrar/google/concluir"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/entrar/google/concluir" element={<GoogleSignupPage />} />
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
  await screen.findByLabelText("Nome de exibição");
  return view;
}

async function acceptAll(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("checkbox", { name: /Termos de Uso/ }));
  await user.click(screen.getByRole("checkbox", { name: /18 anos ou mais/ }));
}

function summaryMessages(): string[] {
  const active = document.activeElement as HTMLElement;
  return within(active)
    .getAllByRole("link")
    .map((link) => link.textContent ?? "");
}

describe("S4 Google - consulta da pendencia (REQ-039)", () => {
  it("mostra h1 e estado de carregamento role=status, sem formulario, enquanto consulta", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    renderPage(async () => {
      await gate;
      return pendingOk();
    });
    expect(screen.getByRole("heading", { level: 1, name: "Quase lá" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByLabelText("Nome de exibição")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Criar conta" })).not.toBeInTheDocument();
    release();
    expect(await screen.findByLabelText("Nome de exibição")).toBeInTheDocument();
  });

  it("404 pending_not_found na consulta vai a /entrar?aviso=google-falhou", async () => {
    renderPage(() => problem(404, { code: "pending_not_found" }));
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=google-falhou"),
    );
  });
});

describe("S4 Google - estrutura (REQ-039, A11Y-22, A11Y-24, ART-02)", () => {
  it("existe: renderiza conteudo (garante que o axe nao passa vazio)", async () => {
    const { container } = await renderReady();
    expect(container).not.toBeEmptyDOMElement();
    expect(screen.getByRole("heading", { level: 1, name: "Quase lá" })).toBeInTheDocument();
  });

  it("mostra o email do Google somente leitura, sem campo editavel de email", async () => {
    await renderReady();
    expect(screen.getByText(GOOGLE_EMAIL, { exact: false })).toBeInTheDocument();
    expect(screen.queryByLabelText("Email")).not.toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /email/i })).not.toBeInTheDocument();
    expect(document.querySelector('input[type="email"]')).toBeNull();
    for (const input of Array.from(document.querySelectorAll("input"))) {
      expect(input.value).not.toBe(GOOGLE_EMAIL);
    }
  });

  it("nome sugerido vem preenchido, editavel e com autocomplete nickname", async () => {
    const user = userEvent.setup();
    await renderReady();
    const name = screen.getByLabelText("Nome de exibição");
    expect(name).toHaveValue(GOOGLE_NAME);
    expect(name).toHaveAttribute("autocomplete", "nickname");
    await user.clear(name);
    await fillField(user, name, "Outro Nome");
    expect(name).toHaveValue("Outro Nome");
    expect(
      screen.getByText("de 3 a 30 caracteres. Outros Jogadores podem ter o mesmo nome.", {
        exact: false,
      }),
    ).toBeInTheDocument();
  });

  it("termos e 18+ sao checkboxes desmarcados, com links em nova aba; botao de envio unico", async () => {
    await renderReady();
    expect(
      screen.getByRole("checkbox", { name: /Li e aceito os Termos de Uso/ }),
    ).not.toBeChecked();
    expect(
      screen.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }),
    ).not.toBeChecked();
    const links = await screen.findAllByRole("link", { name: /\(abre em nova aba\)/ });
    expect(links).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Criar conta" })).toHaveAttribute("type", "submit");
  });

  it("nao ha campo de senha no formulario", async () => {
    await renderReady();
    expect(document.querySelector('input[type="password"]')).toBeNull();
    expect(screen.queryByLabelText(/senha/i)).not.toBeInTheDocument();
  });

  it("na carga o foco NAO vai ao h1 (QA-03, A11Y-23)", async () => {
    renderPage();
    const h1 = screen.getByRole("heading", { level: 1, name: "Quase lá" });
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
    expect(screen.getByLabelText("Nome de exibição")).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("S4 Google - validacao do cliente (REQ-033, REQ-034, REQ-040)", () => {
  it("sem termos e sem 18+ mostra as mensagens exatas e nao chama o servidor", async () => {
    const bodies = captureComplete();
    const user = userEvent.setup();
    await renderReady();
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([
      "Aceite os Termos de Uso e a Política de Privacidade para criar a conta.",
      "Confirme que você tem 18 anos ou mais para criar a conta.",
    ]);
    expect(bodies).toHaveLength(0);
  });

  it.each([
    ["nome curto", "ab"],
    ["nome longo", "n".repeat(31)],
    ["nome vazio", ""],
  ])("%s: 'de 3 a 30 caracteres' no resumo e no campo, sem chamar o servidor", async (_c, nome) => {
    const bodies = captureComplete();
    const user = userEvent.setup();
    await renderReady();
    const name = screen.getByLabelText("Nome de exibição");
    await user.clear(name);
    if (nome) {
      await user.click(name);
      await user.paste(nome);
    }
    await acceptAll(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    const message = "O nome de exibição precisa ter de 3 a 30 caracteres.";
    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([message]);
    expect(screen.getAllByText(message).length).toBeGreaterThanOrEqual(2);
    expect(screen.getByLabelText("Nome de exibição")).toHaveAttribute("aria-invalid", "true");
    expect(bodies).toHaveLength(0);
  });

  it("nome do Google de 40 caracteres vem inteiro e enviar sem editar mostra o erro (WC-07)", async () => {
    const longName = "n".repeat(40);
    const bodies = captureComplete();
    const user = userEvent.setup();
    await renderReady(() => pendingOk(longName));
    expect(screen.getByLabelText("Nome de exibição")).toHaveValue(longName);
    await acceptAll(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));
    await waitFor(() =>
      expect(summaryMessages()).toEqual(["O nome de exibição precisa ter de 3 a 30 caracteres."]),
    );
    expect(bodies).toHaveLength(0);
  });

  it("nome do Google vazio mostra o campo vazio sem erro ate o envio (WC-07)", async () => {
    await renderReady(() => pendingOk(""));
    expect(screen.getByLabelText("Nome de exibição")).toHaveValue("");
    expect(screen.getByLabelText("Nome de exibição")).not.toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("S4 Google - envio e sucesso (REQ-006, REQ-033, REQ-034, REQ-044)", () => {
  it("envia {displayName, acceptTerms, termsVersion, adultDeclaration} sem senha nem email", async () => {
    const bodies = captureComplete();
    const user = userEvent.setup();
    await renderReady();
    await acceptAll(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({
      displayName: GOOGLE_NAME,
      acceptTerms: true,
      termsVersion: TERMS_VERSION,
      adultDeclaration: true,
    });
  });

  it("durante o envio o botao fica aria-busy e nao duplica a requisicao", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const bodies = captureComplete(async () => {
      await gate;
      return HttpResponse.json({ displayName: "x", avatar: null }, { status: 201 });
    });
    const user = userEvent.setup();
    await renderReady();
    await acceptAll(user);
    const button = screen.getByRole("button", { name: "Criar conta" });
    await user.click(button);
    await waitFor(() => expect(button).toHaveAttribute("aria-busy", "true"));
    await user.click(button);
    await user.click(button);
    release();
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(bodies).toHaveLength(1);
  });

  it("201 leva a /perfil com 'Conta criada. Bem-vindo(a) ao Deckloom!' em role=status", async () => {
    captureComplete();
    const user = userEvent.setup();
    await renderReady();
    await acceptAll(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(await screen.findByRole("status")).toHaveTextContent(
      "Conta criada. Bem-vindo(a) ao Deckloom!",
    );
  });
});

describe("S4 Google - erros do servidor (REQ-039, REQ-040, REQ-043)", () => {
  it("404 pending_not_found no envio vai a /entrar?aviso=google-falhou", async () => {
    captureComplete(() => problem(404, { code: "pending_not_found" }));
    const user = userEvent.setup();
    await renderReady();
    await acceptAll(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=google-falhou"),
    );
  });

  it("409 google_not_completed mostra a mensagem, mantem a pendencia e oferece voltar para /entrar", async () => {
    captureComplete(() => problem(409, { code: "google_not_completed" }));
    const user = userEvent.setup();
    await renderReady();
    await acceptAll(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    const message = messageFor("google_not_completed");
    await waitFor(() => expect(screen.getAllByText(message).length).toBeGreaterThan(0));
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar/google/concluir");
    expect(screen.getByLabelText("Nome de exibição")).toHaveValue(GOOGLE_NAME);
    const back = screen.getByRole("link", { name: /Voltar para entrar|Cancelar/ });
    expect(back).toHaveAttribute("href", "/entrar");
  });

  it("400 validation_failed mapeia erros por campo com textos da spec e nunca o texto do servidor", async () => {
    captureComplete(() =>
      problem(400, {
        code: "validation_failed",
        detail: "TEXTO-DO-SERVIDOR",
        errors: {
          displayName: ["invalid_characters"],
          acceptTerms: ["version_mismatch"],
          adultDeclaration: ["required"],
        },
      }),
    );
    const user = userEvent.setup();
    await renderReady();
    await acceptAll(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([
      "O nome de exibição não pode ter quebras de linha nem caracteres de controle.",
      "Os Termos de Uso ou a Política de Privacidade foram atualizados. Revise e aceite novamente.",
      "Confirme que você tem 18 anos ou mais para criar a conta.",
    ]);
    expect(screen.getByLabelText("Nome de exibição")).toHaveAttribute("aria-invalid", "true");
    expect(document.body.textContent).not.toContain("TEXTO-DO-SERVIDOR");
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar/google/concluir");
  });
});

describe("S4 Google - cancelar (design S4)", () => {
  it("Cancelar chama DELETE /auth/google/pending e depois navega para /entrar", async () => {
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
