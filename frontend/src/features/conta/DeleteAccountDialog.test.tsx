// T-118 (RED): S11 Dialog excluir conta - conta com senha.
// REQ-023, REQ-024, REQ-025, REQ-026, REQ-027, REQ-032; UX-08; A11Y-06/11/12; WC-06; WC-12 (so o que o jsdom verifica).
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { useState } from "react";
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider, useSession } from "../../app/SessionProvider";
import { RATE_LIMIT_NOTICE_TEXT } from "../../design-system/RateLimitNotice";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { DeleteAccountDialog } from "./DeleteAccountDialog";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const CSRF_URL = `${BASE}/api/v1/csrf`;
const DELETION_URL = `${BASE}/api/v1/me/deletion`;

const CHECK_LABEL = "Entendo que a exclusão é definitiva.";
const DANGER = "Excluir minha conta definitivamente";
const NOT_CHECKED = "Marque a caixa para confirmar que entende que a exclusão é definitiva.";
const NO_PASSWORD = "Informe sua senha para confirmar.";
const WRONG_PASSWORD = "Senha incorreta. Sua conta não foi excluída.";
const SERVER_ERROR = "Não foi possível excluir sua conta agora. Tente de novo.";
const SHOW = "Mostrar senha";

vi.setConfig({ testTimeout: 15_000 });

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  vi.useRealTimers();
  server.resetHandlers();
});
afterAll(() => server.close());

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function gate() {
  let release: () => void = () => undefined;
  const promise = new Promise<void>((resolve) => {
    release = resolve;
  });
  return { promise, release };
}

function sleep(ms: number) {
  return act(async () => {
    await new Promise<void>((resolve) => setTimeout(resolve, ms));
  });
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>;
}

function SessionProbe() {
  const session = useSession();
  return <p data-testid="session">{session.status}</p>;
}

function BackButton() {
  const navigate = useNavigate();
  return (
    <button type="button" onClick={() => navigate(-1)}>
      Voltar do navegador
    </button>
  );
}

function ProfileHost({ onClosed }: { onClosed: () => void }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <main>
        <h1>Meu perfil</h1>
        <p>Dados do perfil da Jogadora</p>
        <button type="button" onClick={() => setOpen(true)}>
          Excluir conta
        </button>
      </main>
      <DeleteAccountDialog
        open={open}
        onClose={() => {
          setOpen(false);
          onClosed();
        }}
      />
    </>
  );
}

interface Options {
  respond?: () => Response | Promise<Response>;
}

function renderDialog(options: Options = {}) {
  const bodies: unknown[] = [];
  let deleted = false;
  const closed = { count: 0 };
  server.use(
    http.get(SESSION_URL, () =>
      deleted
        ? problem(401, { code: "unauthenticated" })
        : HttpResponse.json({
            displayName: "Jogadora Teste",
            avatar: null,
            hasPassword: true,
            hasGoogle: false,
          }),
    ),
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.post(DELETION_URL, async ({ request }) => {
      bodies.push(await request.json());
      if (options.respond) return options.respond();
      deleted = true;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  const view = render(
    <MemoryRouter initialEntries={["/inicio", "/perfil"]} initialIndex={1}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route
            path="/perfil"
            element={
              <ProfileHost
                onClosed={() => {
                  closed.count += 1;
                }}
              />
            }
          />
          <Route path="/inicio" element={<p>pagina inicio</p>} />
          <Route path="/entrar" element={<p>pagina entrar</p>} />
        </Routes>
        <SessionProbe />
        <LocationProbe />
        <BackButton />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...view, bodies, closed };
}

type User = ReturnType<typeof userEvent.setup>;

async function openDialog(user: User) {
  await waitFor(() => expect(screen.getByTestId("session")).toHaveTextContent("player"));
  await user.click(screen.getByRole("button", { name: "Excluir conta" }));
  return screen.findByRole("alertdialog", { name: "Excluir conta" });
}

function passwordField(dialog: HTMLElement): HTMLInputElement {
  return within(dialog).getByLabelText("Senha") as HTMLInputElement;
}

async function fillAndSubmit(
  user: User,
  dialog: HTMLElement,
  { check = true, password = "senha-segura-1" } = {},
) {
  if (check) await user.click(within(dialog).getByRole("checkbox", { name: CHECK_LABEL }));
  if (password) {
    await user.click(passwordField(dialog));
    await user.paste(password);
  }
  await user.click(within(dialog).getByRole("button", { name: DANGER }));
}

describe("S11 Excluir conta (senha) - abertura e foco (REQ-023, A11Y-06)", () => {
  it("abre como alertdialog com aviso de exclusao imediata, irreversivel, dados e decks, caixa, senha e botoes", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    expect(dialog).toHaveAttribute("role", "alertdialog");
    expect(dialog).toHaveTextContent(/imediata/i);
    expect(dialog).toHaveTextContent(/não pode ser desfeita/i);
    expect(dialog).toHaveTextContent(/dados pessoais/i);
    expect(dialog).toHaveTextContent(/decks/i);
    expect(within(dialog).getByRole("checkbox", { name: CHECK_LABEL })).not.toBeChecked();
    const password = passwordField(dialog);
    expect(password).toHaveAttribute("type", "password");
    expect(password).toHaveAttribute("autocomplete", "current-password");
    expect(within(dialog).getByRole("button", { name: SHOW })).toHaveAttribute(
      "aria-pressed",
      "false",
    );
    expect(within(dialog).getByRole("button", { name: "Cancelar" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: DANGER })).toBeInTheDocument();
  });

  it("o botao de perigo nunca e o foco inicial (ART-02)", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    const danger = within(dialog).getByRole("button", { name: DANGER });
    expect(dialog.contains(document.activeElement)).toBe(true);
    expect(document.activeElement).not.toBe(danger);
  });

  it("nao tem violacoes de acessibilidade", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    await expectNoA11yViolations(dialog);
  });

  it("'Mostrar senha' (nome fixo, aria-pressed) revela e esconde a senha de 128 caracteres sem alterar o valor (WC-06)", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    const long = "a".repeat(128);
    await user.click(passwordField(dialog));
    await user.paste(long);
    const toggle = within(dialog).getByRole("button", { name: SHOW });
    await user.click(toggle);
    expect(passwordField(dialog)).toHaveAttribute("type", "text");
    expect(passwordField(dialog)).toHaveValue(long);
    expect(within(dialog).getByRole("button", { name: SHOW })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    await user.click(within(dialog).getByRole("button", { name: SHOW }));
    expect(passwordField(dialog)).toHaveAttribute("type", "password");
    expect(within(dialog).getByRole("button", { name: SHOW })).toHaveAttribute(
      "aria-pressed",
      "false",
    );
  });
});

describe("S11 Excluir conta (senha) - cancelar sem excluir (A11Y-11/12)", () => {
  it("Cancelar fecha sem enviar nada e devolve o foco a 'Excluir conta'", async () => {
    const user = userEvent.setup();
    const { bodies, closed } = renderDialog();
    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(closed.count).toBe(1);
    expect(bodies).toHaveLength(0);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Excluir conta" })).toHaveFocus(),
    );
    expect(screen.getByTestId("session")).toHaveTextContent("player");
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
  });

  it("Esc fecha sem enviar nada e devolve o foco a 'Excluir conta'", async () => {
    const user = userEvent.setup();
    const { bodies, closed } = renderDialog();
    await openDialog(user);
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(closed.count).toBe(1);
    expect(bodies).toHaveLength(0);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Excluir conta" })).toHaveFocus(),
    );
  });

  it("sem cache: reabrir comeca limpo (caixa desmarcada, senha vazia, sem erro)", async () => {
    const user = userEvent.setup();
    renderDialog();
    let dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("checkbox", { name: CHECK_LABEL }));
    await user.click(passwordField(dialog));
    await user.paste("segredo");
    await user.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    dialog = await openDialog(user);
    expect(within(dialog).getByRole("checkbox", { name: CHECK_LABEL })).not.toBeChecked();
    expect(passwordField(dialog)).toHaveValue("");
  });
});

describe("S11 Excluir conta (senha) - validacao do cliente (REQ-024, REQ-025)", () => {
  it("caixa desmarcada: mostra a mensagem no dialog e nao envia", async () => {
    const user = userEvent.setup();
    const { bodies } = renderDialog();
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog, { check: false });
    const message = await within(dialog).findByText(NOT_CHECKED);
    const box = within(dialog).getByRole("checkbox", { name: CHECK_LABEL });
    expect(box.getAttribute("aria-describedby") ?? "").toContain(message.id);
    expect(message.id).not.toBe("");
    await waitFor(() => expect(box).toHaveFocus());
    expect(bodies).toHaveLength(0);
    expect(screen.queryByRole("alertdialog")).not.toBeNull();
  });

  it("senha vazia: mostra a mensagem no dialog e nao envia", async () => {
    const user = userEvent.setup();
    const { bodies } = renderDialog();
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog, { password: "" });
    const message = await within(dialog).findByText(NO_PASSWORD);
    expect(message.id).not.toBe("");
    expect(passwordField(dialog).getAttribute("aria-describedby") ?? "").toContain(message.id);
    await waitFor(() => expect(passwordField(dialog)).toHaveFocus());
    expect(bodies).toHaveLength(0);
  });

  it("corrigir o campo e reenviar limpa a mensagem e exclui", async () => {
    const user = userEvent.setup();
    const { bodies } = renderDialog();
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog, { password: "" });
    await within(dialog).findByText(NO_PASSWORD);
    await user.click(passwordField(dialog));
    await user.paste("senha-segura-1");
    await user.click(within(dialog).getByRole("button", { name: DANGER }));
    await waitFor(() => expect(bodies).toHaveLength(1));
  });
});

describe("S11 Excluir conta (senha) - envio e sucesso (REQ-023, REQ-027, UX-08)", () => {
  it("envia POST /me/deletion com confirm e a senha exata, sem trim", async () => {
    const user = userEvent.setup();
    const { bodies } = renderDialog();
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog, { password: " senha com espacos " });
    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({ confirm: true, password: " senha com espacos " });
  });

  it("sucesso: vai a /entrar?aviso=conta-excluida e a sessao fica sem Jogador", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog);
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=conta-excluida"),
    );
    await waitFor(() => expect(screen.getByTestId("session")).toHaveTextContent("guest"));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(screen.getByText("pagina entrar")).toBeInTheDocument();
  });

  it("UX-08: 'Voltar' depois da exclusao nao volta ao perfil (entrada substituida no historico)", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog);
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=conta-excluida"),
    );
    await user.click(screen.getByRole("button", { name: "Voltar do navegador" }));
    expect(screen.getByTestId("location")).not.toHaveTextContent("/perfil");
    expect(screen.queryByText("Dados do perfil da Jogadora")).toBeNull();
  });
});

describe("S11 Excluir conta (senha) - senha incorreta (REQ-026, REQ-032)", () => {
  it("401 incorrect_password: mensagem exata no dialog, dialog aberto e conta segue existente", async () => {
    const user = userEvent.setup();
    renderDialog({ respond: () => problem(401, { code: "incorrect_password" }) });
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog, { password: "errada" });
    const alert = await within(dialog).findByRole("alert");
    expect(alert).toHaveTextContent(WRONG_PASSWORD);
    expect(passwordField(dialog)).toHaveValue("errada");
    await waitFor(() => expect(passwordField(dialog)).toHaveFocus());
    expect(screen.queryByRole("alertdialog")).not.toBeNull();
    expect(screen.getByTestId("session")).toHaveTextContent("player");
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
    expect(screen.getByTestId("location")).not.toHaveTextContent("aviso");
    expect(within(dialog).getByRole("button", { name: DANGER })).toBeEnabled();
  });

  it("a mensagem de senha incorreta nao revela a senha enviada nem fala de bloqueio", async () => {
    const user = userEvent.setup();
    renderDialog({ respond: () => problem(401, { code: "incorrect_password" }) });
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog, { password: "errada-123" });
    await within(dialog).findByText(WRONG_PASSWORD);
    expect(dialog.textContent).not.toMatch(/errada-123|bloquead|muitas tentativas/i);
  });
});

describe("S11 Excluir conta (senha) - orcamento por conta (SEC-01)", () => {
  it("429 too_many_attempts: alerta com a mensagem aprovada, dialog aberto e conta segue existente", async () => {
    const user = userEvent.setup();
    renderDialog({ respond: () => problem(429, { code: "too_many_attempts" }) });
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog);
    const alert = await within(dialog).findByRole("alert");
    expect(alert).toHaveTextContent("Muitas tentativas. Aguarde alguns minutos e tente de novo.");
    expect(dialog.textContent).not.toContain(SERVER_ERROR);
    expect(dialog.textContent).not.toContain(WRONG_PASSWORD);
    expect(screen.queryByRole("alertdialog")).not.toBeNull();
    expect(screen.getByTestId("session")).toHaveTextContent("player");
  });
});

describe("S11 Excluir conta (senha) - erro de rede ou servidor", () => {
  it.each([500, 503])(
    "%i: alerta generico no dialog, dialog aberto e sessao mantida",
    async (status) => {
      const user = userEvent.setup();
      renderDialog({ respond: () => problem(status, { code: "server_error" }) });
      const dialog = await openDialog(user);
      await fillAndSubmit(user, dialog);
      const alert = await within(dialog).findByRole("alert");
      expect(alert).toHaveTextContent(SERVER_ERROR);
      expect(screen.queryByRole("alertdialog")).not.toBeNull();
      expect(screen.getByTestId("session")).toHaveTextContent("player");
      expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
      expect(within(dialog).getByRole("button", { name: DANGER })).toBeEnabled();
    },
  );

  it("falha de rede: mesmo alerta generico", async () => {
    const user = userEvent.setup();
    renderDialog({ respond: () => HttpResponse.error() });
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(SERVER_ERROR);
    expect(screen.getByTestId("session")).toHaveTextContent("player");
  });
});

describe("S11 Excluir conta (senha) - enviando (Esc nao fecha) e resposta lenta (WC-12)", () => {
  it("durante o envio o Esc nao fecha e o botao de perigo fica em loading sem duplo envio", async () => {
    const release = gate();
    const user = userEvent.setup();
    const { bodies } = renderDialog({
      respond: async () => {
        await release.promise;
        return new HttpResponse(null, { status: 204 });
      },
    });
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog);
    const danger = within(dialog).getByRole("button", { name: DANGER });
    await waitFor(() => expect(danger).toHaveAttribute("aria-busy", "true"));
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).not.toBeNull();
    fireEvent.click(danger);
    release.release();
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=conta-excluida"),
    );
    expect(bodies).toHaveLength(1);
  });

  it("durante o envio Cancelar nao fecha o dialog", async () => {
    const release = gate();
    const user = userEvent.setup();
    const { closed } = renderDialog({
      respond: async () => {
        await release.promise;
        return new HttpResponse(null, { status: 204 });
      },
    });
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog);
    await waitFor(() =>
      expect(within(dialog).getByRole("button", { name: DANGER })).toHaveAttribute(
        "aria-busy",
        "true",
      ),
    );
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    expect(closed.count).toBe(0);
    expect(screen.queryByRole("alertdialog")).not.toBeNull();
    release.release();
  });

  it("antes de 2 s o RateLimitNotice esta vazio; depois de 2 s anuncia o atraso, sem texto de bloqueio", async () => {
    const release = gate();
    const user = userEvent.setup();
    renderDialog({
      respond: async () => {
        await release.promise;
        return new HttpResponse(null, { status: 204 });
      },
    });
    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("checkbox", { name: CHECK_LABEL }));
    await user.click(passwordField(dialog));
    await user.paste("senha-segura-1");
    fireEvent.click(within(dialog).getByRole("button", { name: DANGER }));
    const region = dialog.querySelector<HTMLElement>(".rate-limit-notice");
    expect(region, "RateLimitNotice ausente do dialog").not.toBeNull();
    const notice = region as HTMLElement;
    expect(notice).toHaveAttribute("role", "status");
    await sleep(1_900);
    expect(notice).toBeEmptyDOMElement();
    await waitFor(() => expect(notice).toHaveTextContent(RATE_LIMIT_NOTICE_TEXT), {
      timeout: 1_100,
    });
    expect(notice.textContent).not.toMatch(/bloquead|bloqueio|muitas tentativas|conta foi/i);
    release.release();
  });

  it("enquanto envia, a senha fica somente leitura", async () => {
    const release = gate();
    const user = userEvent.setup();
    renderDialog({
      respond: async () => {
        await release.promise;
        return new HttpResponse(null, { status: 204 });
      },
    });
    const dialog = await openDialog(user);
    await fillAndSubmit(user, dialog);
    await waitFor(() => expect(passwordField(dialog)).toHaveAttribute("readonly"));
    release.release();
  });
});
