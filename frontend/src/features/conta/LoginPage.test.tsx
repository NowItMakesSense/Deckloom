// T-109 (RED): S2 Login - formulario, validacao do cliente, erro uniforme, avisos e retorno seguro.
// REQ-004, REQ-005, REQ-010, REQ-011, REQ-027; UX-01, UX-12; A11Y-05; ART-02.
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { renderToString } from "react-dom/server";
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { LoginPage } from "./LoginPage";
import { fillField } from "@/shared/testing/fillField";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const LOGIN_URL = `${BASE}/api/v1/auth/login`;
const PASSWORD = "senha-segura-1";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  server.resetHandlers();
  vi.restoreAllMocks();
});
afterAll(() => server.close());

interface LoginBody {
  email: string;
  password: string;
  rememberMe: boolean;
}

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

interface Harness {
  bodies: LoginBody[];
  sessionCalls: () => number;
  sessionCallsAtLogin: () => number;
}

function setup(respond: () => Response | Promise<Response> = () => HttpResponse.json({})): Harness {
  const bodies: LoginBody[] = [];
  let sessionCalls = 0;
  let sessionCallsAtLogin = -1;
  let loggedIn = false;
  server.use(
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(SESSION_URL, () => {
      sessionCalls += 1;
      return loggedIn
        ? HttpResponse.json({
            displayName: "Jogadora Teste",
            avatar: { kind: "preset", id: "p1" },
            hasPassword: true,
            hasGoogle: false,
          })
        : problem(401, { code: "unauthenticated" });
    }),
    http.post(LOGIN_URL, async ({ request }) => {
      bodies.push((await request.json()) as LoginBody);
      sessionCallsAtLogin = sessionCalls;
      const response = await respond();
      if (response.ok) loggedIn = true;
      return response;
    }),
  );
  return {
    bodies,
    sessionCalls: () => sessionCalls,
    sessionCallsAtLogin: () => sessionCallsAtLogin,
  };
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname + location.search}</p>;
}

function renderPage(entry = "/entrar", respond?: () => Response | Promise<Response>) {
  const harness = setup(respond);
  const view = render(
    <MemoryRouter initialEntries={[entry]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/entrar" element={<LoginPage />} />
          <Route path="/perfil" element={<main>pagina perfil</main>} />
          <Route path="/perfil/editar" element={<main>pagina editar</main>} />
          <Route path="/cadastro" element={<main>pagina cadastro</main>} />
          <Route path="/esqueci-senha" element={<main>pagina esqueci</main>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...harness, ...view };
}

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await fillField(user, screen.getByLabelText("Email"), "jogadora@exemplo.com");
  await fillField(user, screen.getByLabelText("Senha"), PASSWORD);
}

describe("S2 Login - estrutura (REQ-004, A11Y-22, ART-02)", () => {
  it("existe: renderiza conteudo (garante que o axe nao passa vazio)", () => {
    const { container } = renderPage();
    expect(container).not.toBeEmptyDOMElement();
    expect(screen.getByRole("heading", { level: 1, name: "Entrar" })).toBeInTheDocument();
  });

  it("tem email (username) e senha (current-password) com botao Mostrar senha", () => {
    renderPage();
    const email = screen.getByLabelText("Email");
    expect(email).toHaveAttribute("type", "email");
    expect(email).toHaveAttribute("autocomplete", "username");
    expect(screen.getByLabelText("Senha")).toHaveAttribute("autocomplete", "current-password");
    expect(screen.getByRole("button", { name: "Mostrar senha" })).toBeInTheDocument();
  });

  it("tem o checkbox 'Lembrar-me neste dispositivo por 30 dias' desmarcado", () => {
    renderPage();
    expect(
      screen.getByRole("checkbox", { name: "Lembrar-me neste dispositivo por 30 dias" }),
    ).not.toBeChecked();
  });

  it("tem 'Entrar' como submit, link Esqueci minha senha, Google, dica e link Criar conta", () => {
    renderPage();
    expect(screen.getByRole("button", { name: "Entrar" })).toHaveAttribute("type", "submit");
    expect(screen.getByRole("link", { name: "Esqueci minha senha" })).toHaveAttribute(
      "href",
      "/esqueci-senha",
    );
    expect(screen.getByRole("button", { name: /Continuar com Google/ })).toBeInTheDocument();
    expect(document.querySelectorAll('button[data-variant="google"]')).toHaveLength(1);
    expect(
      screen.getByText('Criou sua conta com o Google? Use "Continuar com Google".', {
        exact: false,
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Criar conta" })).toHaveAttribute("href", "/cadastro");
  });

  it("na carga o foco NAO vai ao h1 (QA-03, A11Y-23)", async () => {
    renderPage();
    const h1 = screen.getByRole("heading", { level: 1, name: "Entrar" });
    await act(async () => {});
    expect(h1).not.toHaveFocus();
  });

  it("nao ha violacoes de acessibilidade (axe)", async () => {
    const { container } = renderPage();
    expect(screen.getByRole("heading", { level: 1, name: "Entrar" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("S2 Login - validacao do cliente (REQ-004)", () => {
  it("envio vazio mostra as mensagens exatas, foca o primeiro campo invalido e nao chama o servidor", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    expect((await screen.findAllByText("Informe seu email.")).length).toBeGreaterThan(0);
    expect(screen.getAllByText("Informe sua senha.").length).toBeGreaterThan(0);
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Senha")).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(screen.getByLabelText("Email")).toHaveFocus());
    expect(bodies).toHaveLength(0);
  });

  it("so a senha vazia mostra 'Informe sua senha.' e nao chama o servidor", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await fillField(user, screen.getByLabelText("Email"), "jogadora@exemplo.com");
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    expect((await screen.findAllByText("Informe sua senha.")).length).toBeGreaterThan(0);
    expect(screen.queryByText("Informe seu email.")).not.toBeInTheDocument();
    expect(bodies).toHaveLength(0);
  });
});

describe("S2 Login - sucesso e retorno seguro (REQ-004, UX-12)", () => {
  it("envia email, password e rememberMe=false por padrao", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({
      email: "jogadora@exemplo.com",
      password: PASSWORD,
      rememberMe: false,
    });
    // QA-14: o fluxo (login + refresh da sessao) termina antes do teste, para o GET /auth/session nao contar no seguinte.
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
  });

  it("envia rememberMe=true quando o checkbox esta marcado", async () => {
    const { bodies } = renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("checkbox", { name: /Lembrar-me/ }));
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0].rememberMe).toBe(true);
    // QA-14: espera o fim do fluxo (navegacao) antes de terminar.
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
  });

  it("200 sem retorno leva a /perfil, com a sessao atualizada antes da navegacao", async () => {
    const { sessionCalls, sessionCallsAtLogin } = renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(screen.getByText("pagina perfil")).toBeInTheDocument();
    // 1 leitura inicial antes do login + 1 refresh depois dele, ambos antes de chegar ao destino.
    expect(sessionCallsAtLogin()).toBe(1);
    expect(sessionCalls()).toBe(2);
  });

  it("?retorno=/perfil/editar e usado apos o login", async () => {
    renderPage("/entrar?retorno=%2Fperfil%2Feditar");
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil/editar"));
    expect(screen.getByText("pagina editar")).toBeInTheDocument();
  });

  it.each([["https://malicioso.example/x"], ["//malicioso.example"], ["/rota-desconhecida"]])(
    "retorno invalido (%s) e ignorado: vai a /perfil",
    async (retorno) => {
      renderPage(`/entrar?retorno=${encodeURIComponent(retorno)}`);
      const user = userEvent.setup();
      await fillValid(user);
      await user.click(screen.getByRole("button", { name: "Entrar" }));

      await waitFor(() => expect(screen.getByTestId("location").textContent).toBe("/perfil"));
    },
  );

  it("durante o envio o botao fica aria-busy e nao duplica a requisicao", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { bodies } = renderPage("/entrar", async () => {
      await gate;
      return HttpResponse.json({});
    });
    const user = userEvent.setup();
    await fillValid(user);
    const button = screen.getByRole("button", { name: "Entrar" });
    await user.click(button);
    await waitFor(() => expect(button).toHaveAttribute("aria-busy", "true"));
    await user.click(button);
    await user.click(button);
    release();
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(bodies).toHaveLength(1);
  });
});

describe("S2 Login - credenciais invalidas uniformes (REQ-005, REQ-027, UX-01)", () => {
  it("401 invalid_credentials mostra o Alert uniforme com foco, mantem o email e limpa a senha", async () => {
    renderPage("/entrar", () => problem(401, { code: "invalid_credentials" }));
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Email ou senha inválidos.");
    await waitFor(() => expect(alert).toHaveFocus());
    expect(screen.getByLabelText("Email")).toHaveValue("jogadora@exemplo.com");
    expect(screen.getByLabelText("Senha")).toHaveValue("");
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar");
  });

  it("a mensagem nao diz se a conta existe nem marca um campo especifico", async () => {
    renderPage("/entrar", () =>
      problem(401, { code: "invalid_credentials", detail: "TEXTO-DO-SERVIDOR conta inexistente" }),
    );
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    await screen.findByRole("alert");
    expect(document.body.textContent).not.toContain("TEXTO-DO-SERVIDOR");
    expect(document.body.textContent).not.toMatch(/n[ãa]o existe|inexistente|exclu[ií]da/i);
    expect(screen.getByLabelText("Email")).not.toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Senha")).not.toHaveAttribute("aria-invalid", "true");
  });

  it("uma segunda falha mantem exatamente a mesma estrutura do Alert", async () => {
    renderPage("/entrar", () => problem(401, { code: "invalid_credentials" }));
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));
    const alert = await screen.findByRole("alert");
    // QA-01: o FocusedAlert completa a marcacao (tabindex) e o foco num efeito; mede a estrutura so depois disso.
    await waitFor(() => expect(alert).toHaveFocus());
    const first = alert.outerHTML;

    await fillField(user, screen.getByLabelText("Senha"), "outra-senha-2");
    await user.click(screen.getByRole("button", { name: "Entrar" }));
    await waitFor(() => expect(screen.getByLabelText("Senha")).toHaveValue(""));
    expect(screen.getAllByRole("alert")).toHaveLength(1);
    await waitFor(() => expect(screen.getByRole("alert")).toHaveFocus());
    expect(screen.getByRole("alert").outerHTML).toBe(first);
  });
});

describe("S2 Login - orcamento por conta (SEC-01)", () => {
  it("429 too_many_attempts mostra o alerta com a mensagem aprovada, sem contagem regressiva", async () => {
    renderPage("/entrar", () => problem(429, { code: "too_many_attempts" }));
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Muitas tentativas. Aguarde alguns minutos e tente de novo.");
    expect(alert).not.toHaveTextContent("Email ou senha inválidos.");
    expect(screen.getByLabelText("Email")).toHaveValue("jogadora@exemplo.com");
    expect(screen.getByTestId("location")).toHaveTextContent("/entrar");
  });
});

describe("S2 Login - avisos (REQ-011, REQ-010)", () => {
  // Na carga o aviso nao rouba o foco (A11Y-31, QA-03); em transicao de rota o foco vai ao aviso
  // (coberto em RouteFocus.test.tsx pelo redirect do guard).
  it("aviso=sessao-expirada mostra o texto com role=alert, sem mover o foco na carga", async () => {
    renderPage("/entrar?aviso=sessao-expirada");
    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveTextContent("Sua sessão expirou. Entre novamente."));
    await act(async () => {});
    expect(alert).not.toHaveFocus();
  });

  it.each([
    ["saiu", "Você saiu da sua conta."],
    ["senha-alterada", "Senha alterada. Entre com a nova senha."],
    ["conta-excluida", "Sua conta foi excluída."],
    [
      "google-falhou",
      "Não foi possível concluir o login com Google. Tente de novo ou entre com email e senha.",
    ],
  ])("aviso=%s mostra o texto exato com role=status", async (aviso, texto) => {
    renderPage(`/entrar?aviso=${aviso}`);
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent(texto);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("aviso desconhecido nao mostra nenhum aviso", () => {
    renderPage("/entrar?aviso=qualquer-coisa");
    expect(screen.getByRole("heading", { level: 1, name: "Entrar" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});

// T-165 (RED): aviso de sessao expirada inserido apos a montagem na carga completa (U-03, A11Y-31, REQ-012).
describe("S2 Login - aviso sessao-expirada: carga completa x transicao (U-03, A11Y-31)", () => {
  const EXPIRED_TEXT = "Sua sessão expirou. Entre novamente.";
  const EXPIRED_URL = "/entrar?aviso=sessao-expirada";

  function parse(html: string) {
    const container = document.createElement("div");
    container.innerHTML = html;
    return container;
  }

  function GoToLogin() {
    const navigate = useNavigate();
    return (
      <button type="button" onClick={() => navigate(EXPIRED_URL)}>
        ir para entrar
      </button>
    );
  }

  function renderTransition() {
    setup();
    return render(
      <MemoryRouter initialEntries={["/inicio"]}>
        <SessionProvider baseUrl={BASE}>
          <Routes>
            <Route path="/inicio" element={<GoToLogin />} />
            <Route path="/entrar" element={<LoginPage />} />
          </Routes>
        </SessionProvider>
      </MemoryRouter>,
    );
  }

  it("carga completa: a regiao alert nasce vazia (sem o texto no HTML da montagem)", () => {
    const html = renderToString(
      <MemoryRouter initialEntries={[EXPIRED_URL]}>
        <SessionProvider baseUrl={BASE}>
          <Routes>
            <Route path="/entrar" element={<LoginPage />} />
          </Routes>
        </SessionProvider>
      </MemoryRouter>,
    );
    const container = parse(html);
    const region = container.querySelector('[role="alert"]');
    expect(region).not.toBeNull();
    expect(region?.textContent?.trim()).toBe("");
    expect(container.textContent).not.toContain(EXPIRED_TEXT);
  });

  it("carga completa: o texto aparece depois da montagem, sem foco programatico", async () => {
    const { sessionCalls } = renderPage(EXPIRED_URL);
    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveTextContent(EXPIRED_TEXT));
    expect(alert).not.toHaveFocus();
    expect(screen.getByRole("heading", { level: 1, name: "Entrar" })).not.toHaveFocus();
    // QA-14: espera a leitura inicial da sessao terminar antes do fim do teste.
    await waitFor(() => expect(sessionCalls()).toBeGreaterThan(0));
    await act(async () => {});
  });

  it("transicao de rota: o texto ja esta presente e o aviso recebe o foco", async () => {
    const user = userEvent.setup();
    renderTransition();
    await user.click(screen.getByRole("button", { name: "ir para entrar" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(EXPIRED_TEXT);
    await waitFor(() => expect(alert).toHaveFocus());
    expect(screen.getByRole("heading", { level: 1, name: "Entrar" })).not.toHaveFocus();
    await act(async () => {});
  });

  it("o texto do aviso e o mesmo na carga completa e na transicao", async () => {
    const first = renderPage(EXPIRED_URL);
    const loadAlert = await screen.findByRole("alert");
    await waitFor(() => expect(loadAlert).toHaveTextContent(EXPIRED_TEXT));
    const loadText = loadAlert.textContent?.trim();
    first.unmount();

    const user = userEvent.setup();
    renderTransition();
    await user.click(screen.getByRole("button", { name: "ir para entrar" }));
    const transitionAlert = await screen.findByRole("alert");
    await waitFor(() => expect(transitionAlert).toHaveFocus());
    expect(transitionAlert.textContent?.trim()).toBe(loadText);
    await act(async () => {});
  });
});

describe("S2 Login - a senha nunca vaza (REQ-004, A11Y-05)", () => {
  it("a senha nao aparece no DOM nem no console, apos erro de login", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((method) =>
      vi.spyOn(console, method).mockImplementation(() => undefined),
    );
    renderPage("/entrar", () => problem(401, { code: "invalid_credentials" }));
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Entrar" }));
    await screen.findByRole("alert");

    expect(document.body.innerHTML).not.toContain(PASSWORD);
    expect(document.body.textContent).not.toContain(PASSWORD);
    for (const spy of spies) {
      expect(JSON.stringify(spy.mock.calls)).not.toContain(PASSWORD);
    }
  });
});
