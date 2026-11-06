// T-107 (RED): S1 Cadastro - formulario, validacao do cliente e sucesso.
// REQ-001, REQ-003, REQ-033, REQ-034, REQ-038, REQ-040, REQ-044; A11Y-02/04/05/22/24/25; ART-02.
import { render, screen, waitFor, within, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { CadastroPage } from "./CadastroPage";
import { fillField } from "@/shared/testing/fillField";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const TERMS_URL = `${BASE}/api/v1/legal/terms`;
const REGISTER_URL = `${BASE}/api/v1/auth/register`;
const TERMS_VERSION = "2026-10-01";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

interface RegisterBody {
  email: string;
  password: string;
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

function baseHandlers() {
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
  );
}

function captureRegister(
  respond: () => Response | Promise<Response> = () =>
    HttpResponse.json(
      {
        displayName: "Jogadora Teste",
        avatar: { kind: "preset", id: "p1" },
        hasPassword: true,
        hasGoogle: false,
        email: "vazou@exemplo.com",
        passwordHash: "HASH-NAO-EXIBIR",
      },
      { status: 201 },
    ),
) {
  const bodies: RegisterBody[] = [];
  server.use(
    http.post(REGISTER_URL, async ({ request }) => {
      bodies.push((await request.json()) as RegisterBody);
      return respond();
    }),
  );
  return bodies;
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname}</p>;
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

function renderPage() {
  baseHandlers();
  return render(
    <MemoryRouter initialEntries={["/cadastro"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/cadastro" element={<CadastroPage />} />
          <Route path="/perfil" element={<PerfilStub />} />
          <Route path="/entrar" element={<p>pagina entrar</p>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
}

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  // QA-04: colar em vez de digitar tecla a tecla - o custo de ~45 teclas em jsdom estourava o prazo de 5 s
  // de cada teste sob carga de CPU; o evento `input` e os handlers do formulario sao os mesmos.
  await fillField(user, "Email", "jogadora@exemplo.com");
  await fillField(user, "Senha", "senha-segura-1");
  await fillField(user, "Nome de exibição", "Jogadora Teste");
  await user.click(screen.getByRole("checkbox", { name: /Termos de Uso/ }));
  await user.click(screen.getByRole("checkbox", { name: /18 anos ou mais/ }));
}

function summaryMessages(): string[] {
  const active = document.activeElement as HTMLElement;
  return within(active)
    .getAllByRole("link")
    .map((link) => link.textContent ?? "");
}

describe("S1 Cadastro - estrutura (REQ-001, A11Y-22, A11Y-24, ART-02)", () => {
  it("existe: renderiza conteudo (garante que o axe nao passa vazio)", () => {
    const { container } = renderPage();
    expect(container).not.toBeEmptyDOMElement();
    expect(screen.getByRole("heading", { level: 1, name: "Criar conta" })).toBeInTheDocument();
  });

  it("tem os campos com rotulos, dicas e autocomplete corretos", () => {
    renderPage();
    const email = screen.getByLabelText("Email");
    expect(email).toHaveAttribute("type", "email");
    expect(email).toHaveAttribute("autocomplete", "email");

    const password = screen.getByLabelText("Senha");
    expect(password).toHaveAttribute("autocomplete", "new-password");
    expect(screen.getByText("de 8 a 128 caracteres.", { exact: false })).toBeInTheDocument();

    const name = screen.getByLabelText("Nome de exibição");
    expect(name).toHaveAttribute("autocomplete", "nickname");
    expect(
      screen.getByText("de 3 a 30 caracteres. Outros Jogadores podem ter o mesmo nome.", {
        exact: false,
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Mostrar senha" })).toBeInTheDocument();
  });

  it("termos e 18+ sao checkboxes; links dos termos abrem em nova aba sem marcar o checkbox", async () => {
    renderPage();
    const user = userEvent.setup();
    const terms = screen.getByRole("checkbox", { name: /Li e aceito os Termos de Uso/ });
    expect(
      screen.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }),
    ).not.toBeChecked();

    const links = await screen.findAllByRole("link", { name: /\(abre em nova aba\)/ });
    expect(links).toHaveLength(2);
    for (const link of links) {
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
    }
    await user.click(links[0]);
    expect(terms).not.toBeChecked();
  });

  it("tem um unico botao primario 'Criar conta', o botao Google e o link Entrar", () => {
    renderPage();
    expect(screen.getByRole("button", { name: "Criar conta" })).toHaveAttribute("type", "submit");
    expect(screen.getByRole("button", { name: /Continuar com Google/ })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Entrar" })).toHaveAttribute("href", "/entrar");
    expect(document.querySelectorAll('button[data-variant="google"]')).toHaveLength(1);
  });

  it("na carga o foco NAO vai ao h1 (QA-03, A11Y-23)", async () => {
    renderPage();
    const h1 = screen.getByRole("heading", { level: 1, name: "Criar conta" });
    await act(async () => {});
    expect(h1).not.toHaveFocus();
  });

  it("nao ha violacoes de acessibilidade (axe)", async () => {
    const { container } = renderPage();
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("S1 Cadastro - validacao do cliente (REQ-003, REQ-038, REQ-040, REQ-033, REQ-034)", () => {
  it("envio vazio mostra o ErrorSummary com foco e as mensagens exatas, sem chamar o servidor", async () => {
    const bodies = captureRegister();
    renderPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([
      "Informe seu email.",
      "Informe uma senha.",
      "O nome de exibição precisa ter de 3 a 30 caracteres.",
      "Aceite os Termos de Uso e a Política de Privacidade para criar a conta.",
      "Confirme que você tem 18 anos ou mais para criar a conta.",
    ]);
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    expect(bodies).toHaveLength(0);
  });

  it.each([
    [
      "email invalido",
      "sem-arroba",
      "senha-segura-1",
      "Jogadora",
      "Informe um email válido, como nome@exemplo.com.",
    ],
    [
      "senha curta",
      "a@b.com",
      "1234567",
      "Jogadora",
      "A senha precisa ter pelo menos 8 caracteres.",
    ],
    [
      "senha longa",
      "a@b.com",
      "x".repeat(129),
      "Jogadora",
      "A senha pode ter no máximo 128 caracteres.",
    ],
    [
      "nome curto",
      "a@b.com",
      "senha-segura-1",
      "ab",
      "O nome de exibição precisa ter de 3 a 30 caracteres.",
    ],
    [
      "nome longo",
      "a@b.com",
      "senha-segura-1",
      "n".repeat(31),
      "O nome de exibição precisa ter de 3 a 30 caracteres.",
    ],
    [
      "nome com controle",
      "a@b.com",
      "senha-segura-1",
      "Jo\u0007gadora",
      "O nome de exibição não pode ter quebras de linha nem caracteres de controle.",
    ],
  ])("%s: mensagem exata no resumo e no campo", async (_caso, email, senha, nome, mensagem) => {
    const bodies = captureRegister();
    renderPage();
    const user = userEvent.setup();
    await fillField(user, screen.getByLabelText("Email"), email);
    await user.click(screen.getByLabelText("Senha"));
    await user.paste(senha);
    await user.click(screen.getByLabelText("Nome de exibição"));
    await user.paste(nome);
    await user.click(screen.getByRole("checkbox", { name: /Termos de Uso/ }));
    await user.click(screen.getByRole("checkbox", { name: /18 anos ou mais/ }));
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([mensagem]);
    expect(screen.getAllByText(mensagem).length).toBeGreaterThanOrEqual(2);
    expect(bodies).toHaveLength(0);
  });

  it("alterar foco ou campos nao envia o formulario (A11Y-25)", async () => {
    const bodies = captureRegister();
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.tab();
    await user.click(screen.getByLabelText("Email"));
    await user.click(screen.getByRole("checkbox", { name: /18 anos ou mais/ }));
    expect(bodies).toHaveLength(0);
    expect(screen.getByTestId("location")).toHaveTextContent("/cadastro");
  });

  it("clicar em um item do resumo move o foco ao campo", async () => {
    renderPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Criar conta" }));
    await user.click(await screen.findByRole("link", { name: "Informe seu email." }));
    expect(screen.getByLabelText("Email")).toHaveFocus();
  });
});

describe("S1 Cadastro - envio e sucesso (REQ-001, REQ-033, REQ-034, REQ-044)", () => {
  it("envia o corpo do contrato com termsVersion lida de /legal/terms e adultDeclaration", async () => {
    const bodies = captureRegister();
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({
      email: "jogadora@exemplo.com",
      password: "senha-segura-1",
      displayName: "Jogadora Teste",
      acceptTerms: true,
      termsVersion: TERMS_VERSION,
      adultDeclaration: true,
    });
  });

  it("durante o envio o botao fica em loading e nao duplica a requisicao", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const bodies = captureRegister(async () => {
      await gate;
      return HttpResponse.json(
        { displayName: "x", avatar: null, hasPassword: true, hasGoogle: false },
        { status: 201 },
      );
    });
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    const button = screen.getByRole("button", { name: "Criar conta" });
    await user.click(button);
    await waitFor(() => expect(button).toHaveAttribute("aria-busy", "true"));
    await user.click(button);
    await user.click(button);
    release();
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(bodies).toHaveLength(1);
  });

  it("sucesso leva a /perfil com 'Conta criada. Bem-vindo(a) ao Deckloom!' em role=status", async () => {
    captureRegister();
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Conta criada. Bem-vindo(a) ao Deckloom!");
  });

  it("a resposta de sucesso nao e lida com email nem hash: nada disso aparece na tela", async () => {
    captureRegister();
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(document.body.textContent).not.toContain("vazou@exemplo.com");
    expect(document.body.textContent).not.toContain("HASH-NAO-EXIBIR");
    expect(document.body.textContent).not.toContain("senha-segura-1");
  });
});

describe("S1 Cadastro - erros de campo vindos do servidor (REQ-002, REQ-003, REQ-038, REQ-040)", () => {
  it("409 email_unavailable mostra a mensagem no campo email, preserva dados e limpa a senha", async () => {
    captureRegister(() =>
      problem(409, { code: "email_unavailable", errors: { email: ["unavailable"] } }),
    );
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    const message = "Não foi possível usar este email. Tente outro ou entre na sua conta.";
    await waitFor(() => expect(screen.getAllByText(message).length).toBeGreaterThan(0));
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Email")).toHaveValue("jogadora@exemplo.com");
    expect(screen.getByLabelText("Nome de exibição")).toHaveValue("Jogadora Teste");
    expect(screen.getByLabelText("Senha")).toHaveValue("");
    expect(screen.getByTestId("location")).toHaveTextContent("/cadastro");
  });

  it("400 validation_failed mapeia errors por campo, com textos da spec e nunca texto do servidor", async () => {
    captureRegister(() =>
      problem(400, {
        code: "validation_failed",
        detail: "TEXTO-DO-SERVIDOR",
        errors: {
          email: ["invalid_format"],
          password: ["too_short"],
          displayName: ["invalid_characters"],
          acceptTerms: ["version_mismatch"],
          adultDeclaration: ["required"],
        },
      }),
    );
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([
      "Informe um email válido, como nome@exemplo.com.",
      "A senha precisa ter pelo menos 8 caracteres.",
      "O nome de exibição não pode ter quebras de linha nem caracteres de controle.",
      "Os Termos de Uso ou a Política de Privacidade foram atualizados. Revise e aceite novamente.",
      "Confirme que você tem 18 anos ou mais para criar a conta.",
    ]);
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Senha")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Senha")).toHaveValue("");
    expect(document.body.textContent).not.toContain("TEXTO-DO-SERVIDOR");
  });

  it("400 too_long na senha usa a mensagem de 128 caracteres (REQ-038)", async () => {
    captureRegister(() =>
      problem(400, { code: "validation_failed", errors: { password: ["too_long"] } }),
    );
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));
    await waitFor(() =>
      expect(summaryMessages()).toEqual(["A senha pode ter no máximo 128 caracteres."]),
    );
  });

  it("400 too_common na senha mostra a mensagem de senha comum junto ao campo (REQ-038)", async () => {
    captureRegister(() =>
      problem(400, { code: "validation_failed", errors: { password: ["too_common"] } }),
    );
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));
    const message = "Essa senha é muito comum. Escolha uma senha menos previsível.";
    await waitFor(() => expect(summaryMessages()).toEqual([message]));
    expect(screen.getByLabelText("Senha")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByLabelText("Email")).toHaveValue("jogadora@exemplo.com");
    expect(screen.getByTestId("location")).toHaveTextContent("/cadastro");
  });
});
