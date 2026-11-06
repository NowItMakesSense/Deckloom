// T-119 (RED): S11 Dialog excluir conta - conta Google e confirmacao final.
// REQ-023, REQ-026, REQ-042; UX-14; A11Y-06/11/12; WC-12 (so o que o jsdom verifica).
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { useState } from "react";
import { renderToString } from "react-dom/server";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider, useSession } from "../../app/SessionProvider";
import { messageFor } from "../../shared/messages/messages";
import { stubControlledFrames } from "../../shared/testing/controlledFrames";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { DeleteAccountGoogleDialog } from "./DeleteAccountGoogleDialog";
import { ProfilePage } from "./ProfilePage";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const PROFILE_URL = `${BASE}/api/v1/me/profile`;
const CSRF_URL = `${BASE}/api/v1/csrf`;
const DELETION_URL = `${BASE}/api/v1/me/deletion`;

const CHECK_LABEL = "Entendo que a exclusão é definitiva.";
const NOT_CHECKED = "Marque a caixa para confirmar que entende que a exclusão é definitiva.";
const REAUTH_TEXT = "Para confirmar, entre novamente com o Google.";
const REAUTH = "Reautenticar com o Google";
const DANGER = "Excluir minha conta definitivamente";
const CONFIRM_TEXT = "Você confirmou sua identidade. Excluir sua conta agora?";
const EXPIRED = "Sua confirmação expirou. Reautentique com o Google para excluir a conta.";
const START_URL = /\/api\/v1\/auth\/google\/start\?intent=reauth$/;

vi.setConfig({ testTimeout: 15_000 });

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  vi.unstubAllGlobals();
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

// A reautenticacao e uma navegacao completa do navegador (assign, replace ou href): o fake registra as tres.
function stubBrowserNavigation() {
  const visited: string[] = [];
  const record = (url: string | URL) => {
    visited.push(String(url));
  };
  vi.stubGlobal("location", {
    origin: BASE,
    protocol: "http:",
    host: "localhost",
    hostname: "localhost",
    pathname: "/perfil",
    search: "",
    hash: "",
    assign: record,
    replace: record,
    reload: () => undefined,
    get href() {
      return `${BASE}/perfil`;
    },
    set href(value: string) {
      record(value);
    },
  });
  return visited;
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>;
}

function SessionProbe() {
  const session = useSession();
  return <p data-testid="session">{session.status}</p>;
}

const PROFILE = {
  displayName: "Jogadora Google",
  bio: "Gosto de decks de comandante.",
  favoriteTcg: { id: "mtg", name: "Magic: The Gathering" },
  preferredFormats: [{ id: "commander", name: "Commander" }],
  avatar: { kind: "preset", presetId: "p1", imageUrl: "/avatars/presets/p1.svg", alt: "Avatar" },
  hasPassword: false,
  hasGoogle: true,
};

interface Options {
  respond?: () => Response | Promise<Response>;
}

function mockApi(options: Options) {
  const bodies: unknown[] = [];
  let deleted = false;
  server.use(
    http.get(SESSION_URL, () =>
      deleted
        ? problem(401, { code: "unauthenticated" })
        : HttpResponse.json({
            displayName: "Jogadora Google",
            avatar: null,
            hasPassword: false,
            hasGoogle: true,
          }),
    ),
    http.get(PROFILE_URL, () => HttpResponse.json(PROFILE)),
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.post(DELETION_URL, async ({ request }) => {
      bodies.push(await request.json());
      if (options.respond) return options.respond();
      deleted = true;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return bodies;
}

function Host({ onClosed }: { onClosed: () => void }) {
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
      <DeleteAccountGoogleDialog
        open={open}
        onClose={() => {
          setOpen(false);
          onClosed();
        }}
      />
    </>
  );
}

function renderDialog(options: Options = {}) {
  const bodies = mockApi(options);
  const visited = stubBrowserNavigation();
  const closed = { count: 0 };
  const view = render(
    <MemoryRouter initialEntries={["/perfil"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route
            path="/perfil"
            element={
              <Host
                onClosed={() => {
                  closed.count += 1;
                }}
              />
            }
          />
          <Route path="/entrar" element={<p>pagina entrar</p>} />
        </Routes>
        <SessionProbe />
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...view, bodies, visited, closed };
}

// Retorno do Google (S3 -> /perfil?exclusao=confirmar): a tela real do perfil abre "Confirmar exclusao".
function renderReturn(options: Options = {}) {
  const bodies = mockApi(options);
  const visited = stubBrowserNavigation();
  const view = render(
    <MemoryRouter initialEntries={["/perfil?exclusao=confirmar"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/perfil" element={<ProfilePage />} />
          <Route path="/entrar" element={<p>pagina entrar</p>} />
        </Routes>
        <SessionProbe />
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...view, bodies, visited };
}

type User = ReturnType<typeof userEvent.setup>;

async function openFirst(user: User) {
  await waitFor(() => expect(screen.getByTestId("session")).toHaveTextContent("player"));
  await user.click(screen.getByRole("button", { name: "Excluir conta" }));
  return screen.findByRole("alertdialog", { name: "Excluir conta" });
}

function findConfirm() {
  return screen.findByRole("alertdialog", { name: "Confirmar exclusão" });
}

describe("S11 Excluir conta (Google) - primeiro dialog (REQ-023, A11Y-06)", () => {
  it("abre como alertdialog com aviso, caixa, texto de reautenticacao, Cancelar e Reautenticar, sem senha", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openFirst(user);
    expect(dialog).toHaveAttribute("role", "alertdialog");
    expect(dialog).toHaveTextContent(/imediata/i);
    expect(dialog).toHaveTextContent(/não pode ser desfeita/i);
    expect(dialog).toHaveTextContent(/dados pessoais/i);
    expect(dialog).toHaveTextContent(/decks/i);
    expect(within(dialog).getByRole("checkbox", { name: CHECK_LABEL })).not.toBeChecked();
    expect(within(dialog).getByText(REAUTH_TEXT)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Cancelar" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: REAUTH })).toBeInTheDocument();
    expect(within(dialog).queryByLabelText("Senha")).toBeNull();
  });

  it("o botao Reautenticar nunca e o foco inicial (ART-02)", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openFirst(user);
    expect(dialog.contains(document.activeElement)).toBe(true);
    expect(document.activeElement).not.toBe(within(dialog).getByRole("button", { name: REAUTH }));
  });

  it("nao tem violacoes de acessibilidade", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openFirst(user);
    await expectNoA11yViolations(dialog);
  });
});

describe("S11 Excluir conta (Google) - reautenticar (REQ-023, REQ-042)", () => {
  it("caixa desmarcada: mostra a mensagem no dialog, foca a caixa e nao navega", async () => {
    const user = userEvent.setup();
    const { visited, bodies } = renderDialog();
    const dialog = await openFirst(user);
    await user.click(within(dialog).getByRole("button", { name: REAUTH }));
    const message = await within(dialog).findByText(NOT_CHECKED);
    const box = within(dialog).getByRole("checkbox", { name: CHECK_LABEL });
    expect(message.id).not.toBe("");
    expect(box.getAttribute("aria-describedby") ?? "").toContain(message.id);
    await waitFor(() => expect(box).toHaveFocus());
    expect(visited).toHaveLength(0);
    expect(bodies).toHaveLength(0);
    expect(screen.queryByRole("alertdialog")).not.toBeNull();
  });

  it("caixa marcada: navega para start?intent=reauth, sem returnTo e sem chamar /me/deletion", async () => {
    const user = userEvent.setup();
    const { visited, bodies } = renderDialog();
    const dialog = await openFirst(user);
    await user.click(within(dialog).getByRole("checkbox", { name: CHECK_LABEL }));
    await user.click(within(dialog).getByRole("button", { name: REAUTH }));
    await waitFor(() => expect(visited).toHaveLength(1));
    expect(visited[0]).toMatch(START_URL);
    expect(visited[0]).not.toContain("returnTo");
    expect(bodies).toHaveLength(0);
  });
});

describe("S11 Excluir conta (Google) - cancelar sem excluir (A11Y-11/12)", () => {
  it("Cancelar fecha sem navegar nem enviar e devolve o foco a 'Excluir conta'", async () => {
    const user = userEvent.setup();
    const { visited, bodies, closed } = renderDialog();
    const dialog = await openFirst(user);
    await user.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(closed.count).toBe(1);
    expect(visited).toHaveLength(0);
    expect(bodies).toHaveLength(0);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Excluir conta" })).toHaveFocus(),
    );
    expect(screen.getByTestId("session")).toHaveTextContent("player");
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
  });

  it("Esc fecha sem navegar nem enviar e devolve o foco a 'Excluir conta'", async () => {
    const user = userEvent.setup();
    const { visited, bodies, closed } = renderDialog();
    await openFirst(user);
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(closed.count).toBe(1);
    expect(visited).toHaveLength(0);
    expect(bodies).toHaveLength(0);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Excluir conta" })).toHaveFocus(),
    );
  });

  it("sem cache: reabrir comeca com a caixa desmarcada e sem erro", async () => {
    const user = userEvent.setup();
    renderDialog();
    let dialog = await openFirst(user);
    await user.click(within(dialog).getByRole("button", { name: REAUTH }));
    await within(dialog).findByText(NOT_CHECKED);
    await user.click(within(dialog).getByRole("checkbox", { name: CHECK_LABEL }));
    await user.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    dialog = await openFirst(user);
    expect(within(dialog).getByRole("checkbox", { name: CHECK_LABEL })).not.toBeChecked();
    expect(within(dialog).queryByText(NOT_CHECKED)).toBeNull();
  });
});

describe("S11 Confirmar exclusao (retorno do Google) - abertura (REQ-023, REQ-042)", () => {
  it("/perfil?exclusao=confirmar abre o alertdialog com o texto, Cancelar e o botao de perigo", async () => {
    renderReturn();
    const dialog = await findConfirm();
    expect(dialog).toHaveAttribute("role", "alertdialog");
    expect(within(dialog).getByText(CONFIRM_TEXT)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Cancelar" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: DANGER })).toBeInTheDocument();
  });

  it("o foco inicial e Cancelar, nunca o botao de perigo (ART-02)", async () => {
    renderReturn();
    const dialog = await findConfirm();
    await waitFor(() =>
      expect(within(dialog).getByRole("button", { name: "Cancelar" })).toHaveFocus(),
    );
    expect(document.activeElement).not.toBe(within(dialog).getByRole("button", { name: DANGER }));
  });

  it("nao tem violacoes de acessibilidade", async () => {
    renderReturn();
    const dialog = await findConfirm();
    await expectNoA11yViolations(dialog);
  });
});

describe("S11 Confirmar exclusao - cancelar (A11Y-11/12)", () => {
  it("Cancelar fecha, nao envia nada e o perfil segue inalterado", async () => {
    const user = userEvent.setup();
    const { bodies } = renderReturn();
    const dialog = await findConfirm();
    await user.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(bodies).toHaveLength(0);
    expect(screen.getByTestId("session")).toHaveTextContent("player");
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
    expect(await screen.findByText("Jogadora Google")).toBeInTheDocument();
  });

  it("Esc fecha, nao envia nada e o perfil segue inalterado", async () => {
    const user = userEvent.setup();
    const { bodies } = renderReturn();
    await findConfirm();
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(bodies).toHaveLength(0);
    expect(screen.getByTestId("session")).toHaveTextContent("player");
    expect(await screen.findByText("Jogadora Google")).toBeInTheDocument();
  });
});

describe("S11 Confirmar exclusao - envio e sucesso (REQ-023, UX-08)", () => {
  it("o botao de perigo envia POST /me/deletion com {confirm: true}, sem senha", async () => {
    const user = userEvent.setup();
    const { bodies } = renderReturn();
    const dialog = await findConfirm();
    await user.click(within(dialog).getByRole("button", { name: DANGER }));
    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({ confirm: true });
  });

  it("204: vai a /entrar?aviso=conta-excluida e a sessao fica sem Jogador", async () => {
    const user = userEvent.setup();
    renderReturn();
    const dialog = await findConfirm();
    await user.click(within(dialog).getByRole("button", { name: DANGER }));
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("/entrar?aviso=conta-excluida"),
    );
    await waitFor(() => expect(screen.getByTestId("session")).toHaveTextContent("guest"));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(screen.getByText("pagina entrar")).toBeInTheDocument();
  });

  it("durante o envio o Esc nao fecha e o botao de perigo fica em loading sem duplo envio (WC-12)", async () => {
    const release = gate();
    const user = userEvent.setup();
    const { bodies } = renderReturn({
      respond: async () => {
        await release.promise;
        return new HttpResponse(null, { status: 204 });
      },
    });
    const dialog = await findConfirm();
    const danger = within(dialog).getByRole("button", { name: DANGER });
    await user.click(danger);
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
});

describe("S11 Confirmar exclusao - erro de rede ou servidor", () => {
  const SERVER_ERROR = "Não foi possível excluir sua conta agora. Tente de novo.";
  const failures: [string, () => Response][] = [
    ["500", () => problem(500, { code: "server_error" })],
    ["400", () => problem(400, { code: "validation_failed" })],
    ["falha de rede", () => HttpResponse.error()],
  ];

  it.each(failures)(
    "%s: alerta generico no dialog de confirmacao, dialog aberto e sessao mantida",
    async (_name, respond) => {
      const user = userEvent.setup();
      renderReturn({ respond });
      const dialog = await findConfirm();
      await user.click(within(dialog).getByRole("button", { name: DANGER }));
      const alert = await within(dialog).findByRole("alert");
      expect(alert).toHaveTextContent(SERVER_ERROR);
      expect(screen.queryByRole("alertdialog", { name: "Confirmar exclusão" })).not.toBeNull();
      expect(screen.getByTestId("session")).toHaveTextContent("player");
      expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
      expect(within(dialog).getByRole("button", { name: DANGER })).toBeEnabled();
    },
  );
});

describe("S11 Confirmar exclusao - reautenticacao expirada (REQ-042, UX-14)", () => {
  it("403 reauth_expired: mostra o texto exato e volta ao primeiro dialog com a conta intacta", async () => {
    const user = userEvent.setup();
    renderReturn({ respond: () => problem(403, { code: "reauth_expired" }) });
    const confirm = await findConfirm();
    await user.click(within(confirm).getByRole("button", { name: DANGER }));
    const first = await screen.findByRole("alertdialog", { name: "Excluir conta" });
    expect(await within(first).findByText(EXPIRED)).toBeInTheDocument();
    // R-20: o aviso e anunciado pelo leitor de tela (role="alert").
    expect(within(first).getByRole("alert")).toHaveTextContent(EXPIRED);
    expect(within(first).getByText(REAUTH_TEXT)).toBeInTheDocument();
    expect(within(first).getByRole("button", { name: REAUTH })).toBeInTheDocument();
    expect(within(first).getByRole("checkbox", { name: CHECK_LABEL })).not.toBeChecked();
    expect(screen.queryByRole("alertdialog", { name: "Confirmar exclusão" })).toBeNull();
    expect(screen.getByTestId("session")).toHaveTextContent("player");
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
    expect(screen.getByTestId("location")).not.toHaveTextContent("aviso");
    await waitFor(() =>
      expect(within(first).getByRole("checkbox", { name: CHECK_LABEL })).toHaveFocus(),
    );
  });

  it("403 reauth_required: mesmo retorno ao primeiro dialog, com a mensagem de messages.ts e a conta intacta", async () => {
    const user = userEvent.setup();
    renderReturn({ respond: () => problem(403, { code: "reauth_required" }) });
    const confirm = await findConfirm();
    await user.click(within(confirm).getByRole("button", { name: DANGER }));
    const first = await screen.findByRole("alertdialog", { name: "Excluir conta" });
    expect(await within(first).findByText(messageFor("reauth_required"))).toBeInTheDocument();
    expect(within(first).getByRole("button", { name: REAUTH })).toBeInTheDocument();
    expect(within(first).getByRole("checkbox", { name: CHECK_LABEL })).not.toBeChecked();
    expect(screen.queryByRole("alertdialog", { name: "Confirmar exclusão" })).toBeNull();
    expect(screen.getByTestId("session")).toHaveTextContent("player");
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
    expect(screen.getByTestId("location")).not.toHaveTextContent("aviso");
  });

  it("depois do 403 e possivel reautenticar de novo (caixa + Reautenticar navega para start?intent=reauth)", async () => {
    const user = userEvent.setup();
    const { visited } = renderReturn({ respond: () => problem(403, { code: "reauth_expired" }) });
    const confirm = await findConfirm();
    await user.click(within(confirm).getByRole("button", { name: DANGER }));
    const first = await screen.findByRole("alertdialog", { name: "Excluir conta" });
    await user.click(within(first).getByRole("checkbox", { name: CHECK_LABEL }));
    await user.click(within(first).getByRole("button", { name: REAUTH }));
    await waitFor(() => expect(visited).toHaveLength(1));
    expect(visited[0]).toMatch(START_URL);
  });
});

// T-156 (R-43, A11Y-31): o aviso fica ligado a caixa por aria-describedby e entra no DOM apos a montagem.
describe("S11 Excluir conta (Google) - aviso de reautenticacao (R-43, A11Y-31, REQ-042)", () => {
  function renderWithNotice() {
    const user = userEvent.setup();
    render(<DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />);
    return user;
  }

  it("a caixa de confirmacao e descrita pelo aviso (aria-describedby inclui o id da regiao alert)", async () => {
    renderWithNotice();
    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveTextContent(EXPIRED));
    expect(alert.id).not.toBe("");
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    expect((box.getAttribute("aria-describedby") ?? "").split(/\s+/)).toContain(alert.id);
    expect(box).toHaveAccessibleDescription(EXPIRED);
  });

  it("com a caixa desmarcada no envio, aria-describedby inclui o aviso e o erro", async () => {
    const user = renderWithNotice();
    await user.click(screen.getByRole("button", { name: REAUTH }));
    const alert = await screen.findByRole("alert");
    const error = await screen.findByText(NOT_CHECKED);
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    const ids = (box.getAttribute("aria-describedby") ?? "").split(/\s+/);
    expect(ids).toContain(alert.id);
    expect(ids).toContain(error.id);
  });

  it("sem aviso, a caixa nao ganha aria-describedby", () => {
    render(<DeleteAccountGoogleDialog open onClose={() => {}} />);
    expect(screen.getByRole("checkbox", { name: CHECK_LABEL })).not.toHaveAttribute(
      "aria-describedby",
    );
  });

  it("o texto do aviso nao esta no primeiro render: a regiao alert nasce vazia e recebe o texto num efeito", () => {
    // renderToString nao roda efeitos: mostra o que existe na montagem.
    const html = renderToString(
      <DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />,
    );
    const container = document.createElement("div");
    container.innerHTML = html;
    const region = container.querySelector('[role="alert"]');
    expect(region).not.toBeNull();
    expect(region?.textContent?.trim()).toBe("");
    expect(container.textContent).not.toContain(EXPIRED);
  });
});

// T-164 (R-46, U-01): o aviso usa o primitivo Alert announceAfterMount e a descricao lista [erro, aviso].
describe("S11 Excluir conta (Google) - primitivo e ordem da descricao (R-46, U-01, A11Y-31)", () => {
  const REQUIRED = messageFor("reauth_required");

  it("o aviso e renderizado pelo primitivo Alert (variante error), nao por um <p> avulso", async () => {
    render(<DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />);
    const region = await screen.findByRole("alert");
    await waitFor(() => expect(region).toHaveTextContent(EXPIRED));
    expect(region.classList.contains("alert")).toBe(true);
    expect(region.classList.contains("alert--error")).toBe(true);
  });

  it("trocar o aviso (reauth_expired -> reauth_required) sem remontar mostra o texto novo", async () => {
    const { rerender } = render(
      <DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />,
    );
    const region = await screen.findByRole("alert");
    await waitFor(() => expect(region).toHaveTextContent(EXPIRED));
    rerender(<DeleteAccountGoogleDialog open onClose={() => {}} notice={REQUIRED} />);
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(REQUIRED));
    expect(screen.getByRole("alert")).toBe(region);
    expect(screen.getByRole("alert")).not.toHaveTextContent(EXPIRED);
  });

  it("aviso presente e caixa desmarcada no envio: a descricao comeca pelo erro e termina com o aviso", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />);
    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveTextContent(EXPIRED));
    await user.click(screen.getByRole("button", { name: REAUTH }));
    const error = await screen.findByText(NOT_CHECKED);
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    await waitFor(() => expect(box).toHaveAccessibleDescription(`${NOT_CHECKED} ${EXPIRED}`));
    expect((box.getAttribute("aria-describedby") ?? "").split(/\s+/)).toEqual([error.id, alert.id]);
  });

  it("sem erro, a descricao e so o aviso", async () => {
    render(<DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />);
    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveTextContent(EXPIRED));
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    await waitFor(() => expect(box).toHaveAccessibleDescription(EXPIRED));
    expect((box.getAttribute("aria-describedby") ?? "").split(/\s+/)).toEqual([alert.id]);
  });

  it("sem aviso, a descricao e so o erro", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountGoogleDialog open onClose={() => {}} />);
    await user.click(screen.getByRole("button", { name: REAUTH }));
    const error = await screen.findByText(NOT_CHECKED);
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    await waitFor(() => expect(box).toHaveAccessibleDescription(NOT_CHECKED));
    expect((box.getAttribute("aria-describedby") ?? "").split(/\s+/)).toEqual([error.id]);
  });
});

// T-167 (RED, U-04): a caixa so recebe foco quando as regioes de aria-describedby ja contem o texto.
describe("S11 Excluir conta (Google) - foco da caixa espera o aviso anunciado (U-04, A11Y-31)", () => {
  const REQUIRED = messageFor("reauth_required");

  interface FocusCapture {
    describedBy: string;
    text: string;
  }

  // Captura no instante do evento focus da caixa (fase de captura no document).
  function captureBoxFocus() {
    const captures: FocusCapture[] = [];
    const listener = (event: Event) => {
      const target = event.target;
      if (!(target instanceof HTMLInputElement) || target.type !== "checkbox") return;
      const describedBy = target.getAttribute("aria-describedby") ?? "";
      const text = describedBy
        .split(/\s+/)
        .filter(Boolean)
        .map((id) => document.getElementById(id)?.textContent ?? "")
        .join(" ")
        .trim();
      captures.push({ describedBy, text });
    };
    document.addEventListener("focus", listener, true);
    return { captures, stop: () => document.removeEventListener("focus", listener, true) };
  }

  it("com aviso, a caixa nao recebe foco antes do anuncio e, ao receber, a regiao descrita ja tem o texto", async () => {
    const frames = stubControlledFrames();
    const probe = captureBoxFocus();
    render(<DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />);
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    expect(box).not.toHaveFocus();
    expect(probe.captures).toHaveLength(0);
    frames.flush();
    await waitFor(() => expect(box).toHaveFocus());
    probe.stop();
    expect(probe.captures).toHaveLength(1);
    expect(probe.captures[0].describedBy).not.toBe("");
    expect(probe.captures[0].text).toContain(EXPIRED);
  });

  it("trocar o aviso sem remontar: todo foco na caixa ve o texto do aviso corrente, nunca regiao vazia", async () => {
    const frames = stubControlledFrames();
    const probe = captureBoxFocus();
    const { rerender } = render(
      <DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />,
    );
    frames.flush();
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    await waitFor(() => expect(box).toHaveFocus());
    screen.getByRole("button", { name: "Cancelar" }).focus();
    rerender(<DeleteAccountGoogleDialog open onClose={() => {}} notice={REQUIRED} />);
    frames.flush();
    probe.stop();
    expect(probe.captures).toHaveLength(2);
    expect(probe.captures[0].text).toContain(EXPIRED);
    expect(probe.captures[1].text).toContain(REQUIRED);
    expect(probe.captures[1].text).not.toContain(EXPIRED);
  });

  // R-53b: aviso -> sem aviso -> aviso. O proximo aviso nasce vazio e a caixa so recebe foco depois de anunciado.
  it("aviso, sem aviso e aviso de novo sem remontar: o foco da caixa ve o texto do aviso novo", async () => {
    const frames = stubControlledFrames();
    const probe = captureBoxFocus();
    const { rerender } = render(
      <DeleteAccountGoogleDialog open onClose={() => {}} notice={EXPIRED} />,
    );
    frames.flush();
    const box = screen.getByRole("checkbox", { name: CHECK_LABEL });
    await waitFor(() => expect(box).toHaveFocus());
    screen.getByRole("button", { name: "Cancelar" }).focus();
    rerender(<DeleteAccountGoogleDialog open onClose={() => {}} />);
    frames.flush();
    rerender(<DeleteAccountGoogleDialog open onClose={() => {}} notice={REQUIRED} />);
    frames.flush();
    await waitFor(() => expect(box).toHaveFocus());
    probe.stop();
    expect(probe.captures).toHaveLength(2);
    expect(probe.captures[1].text).toContain(REQUIRED);
    expect(probe.captures[1].text).not.toContain(EXPIRED);
  });
});
