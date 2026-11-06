// T-116 (RED): S9 Editar perfil.
// REQ-014, REQ-015, REQ-016, REQ-017, REQ-037, REQ-040, REQ-046; UX-13; U13.
// WC-01/05/08/13/18 e ART-02/ART-05 (so o que e verificavel em jsdom); MOT-15.
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { PlayerRoute } from "../../app/guards";
import { SessionProvider } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { EditProfilePage } from "./EditProfilePage";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const CSRF_URL = `${BASE}/api/v1/csrf`;
const PROFILE_URL = `${BASE}/api/v1/me/profile`;
const FORMATS_URL = `${BASE}/api/v1/catalog/formats`;
const TCGS_URL = `${BASE}/api/v1/catalog/favorite-tcgs`;
const PRESETS_URL = `${BASE}/api/v1/catalog/avatar-presets`;

const NAME_LENGTH = "O nome de exibição precisa ter de 3 a 30 caracteres.";
const NAME_CONTROL = "O nome de exibição não pode ter quebras de linha nem caracteres de controle.";
const BIO_TOO_LONG = "A bio pode ter no máximo 280 caracteres.";
const BIO_CONTROL = "A bio não pode ter caracteres de controle.";
const TCG_INVALID = "Escolha um TCG favorito válido.";
const FORMATS_INVALID = "Escolha apenas formatos da lista.";
const NO_FORMATS = "Nenhum formato disponível no momento.";
const SAVE_ERROR = "Não foi possível salvar. Tente de novo.";

vi.setConfig({ testTimeout: 15_000 });

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  server.resetHandlers();
});
afterAll(() => server.close());

const PRESET_AVATAR = {
  kind: "preset",
  presetId: "p1",
  imageUrl: "/avatars/presets/p1.svg",
  alt: "Avatar: Dragao",
};

interface Fmt {
  id: string;
  name: string;
}

function profile(overrides: Record<string, unknown> = {}) {
  return {
    displayName: "Jogadora Teste",
    bio: "Gosto de decks de comandante.",
    favoriteTcg: { id: "mtg", name: "Magic: The Gathering" },
    preferredFormats: [{ id: "modern", name: "Modern" }] as Fmt[],
    avatar: PRESET_AVATAR,
    hasPassword: true,
    hasGoogle: false,
    ...overrides,
  };
}

const CATALOG: Fmt[] = [
  { id: "standard", name: "Standard" },
  { id: "modern", name: "Modern" },
  { id: "pioneer", name: "Pioneer" },
  { id: "commander", name: "Commander" },
  { id: "pauper", name: "Pauper" },
];

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function LocationProbe() {
  const location = useLocation();
  const state = location.state as { message?: string } | null;
  return (
    <>
      <p data-testid="location">{location.pathname}</p>
      <p data-testid="search">{location.search}</p>
      <p data-testid="flash">{state?.message ?? ""}</p>
    </>
  );
}

interface PutBody {
  displayName: string;
  bio: string | null;
  favoriteTcg: string;
  formatIds: string[];
}

interface Options {
  load?: () => Response | Promise<Response>;
  catalog?: () => Response | Promise<Response>;
  put?: () => Response | Promise<Response>;
}

function renderPage(options: Options = {}) {
  const puts: PutBody[] = [];
  const requests: string[] = [];
  server.use(
    http.get(SESSION_URL, () =>
      HttpResponse.json({
        displayName: "Jogadora Teste",
        avatar: PRESET_AVATAR,
        hasPassword: true,
        hasGoogle: false,
      }),
    ),
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(PROFILE_URL, () => {
      requests.push("GET /me/profile");
      return options.load ? options.load() : HttpResponse.json(profile());
    }),
    http.get(FORMATS_URL, () => (options.catalog ? options.catalog() : HttpResponse.json(CATALOG))),
    http.get(TCGS_URL, () => HttpResponse.json([{ id: "mtg", name: "Magic: The Gathering" }])),
    http.get(PRESETS_URL, () =>
      HttpResponse.json([
        { id: "p1", name: "Dragao", imageUrl: "/avatars/presets/p1.svg", isDefault: true },
      ]),
    ),
    http.put(PROFILE_URL, async ({ request }) => {
      puts.push((await request.json()) as PutBody);
      return options.put ? options.put() : HttpResponse.json(profile());
    }),
  );
  const view = render(
    <MemoryRouter initialEntries={["/perfil/editar"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route element={<PlayerRoute />}>
            <Route path="/perfil/editar" element={<EditProfilePage />} />
            <Route path="/perfil" element={<main>pagina perfil</main>} />
          </Route>
          <Route path="/entrar" element={<main>pagina entrar</main>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...view, puts, requests };
}

async function loaded() {
  return screen.findByLabelText("Nome de exibição", {}, { timeout: 3000 });
}

function bioField(): HTMLTextAreaElement {
  return screen.getByLabelText(/^Bio/) as HTMLTextAreaElement;
}

async function replaceText(
  user: ReturnType<typeof userEvent.setup>,
  el: HTMLElement,
  text: string,
) {
  await user.clear(el);
  if (text === "") return;
  await user.click(el);
  await user.paste(text);
}

async function save(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "Salvar alterações" }));
}

// Escopo no resumo de erros (.error-summary): o Alert de erro de servidor tambem tem role=alert.
async function errorSummary(): Promise<HTMLElement> {
  return waitFor(() => {
    const el = document.querySelector<HTMLElement>(".error-summary");
    expect(el, "ErrorSummary ausente").not.toBeNull();
    return el as HTMLElement;
  });
}

function describedText(el: HTMLElement): string {
  return (el.getAttribute("aria-describedby") ?? "")
    .split(" ")
    .map((id) => document.getElementById(id)?.textContent ?? "")
    .join(" ");
}

describe("S9 Editar perfil - carregamento (WC-13, REQ-014)", () => {
  it("mostra o h1 e um Skeleton aria-busy enquanto o perfil carrega", async () => {
    // A requisicao e segurada e liberada no fim: nenhuma requisicao orfa vaza para o teste seguinte.
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { requests, unmount } = renderPage({
      load: async () => {
        await gate;
        return HttpResponse.json(profile());
      },
    });
    expect(
      await screen.findByRole("heading", { level: 1, name: "Editar perfil" }),
    ).toBeInTheDocument();
    expect(document.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(screen.queryByLabelText("Nome de exibição")).not.toBeInTheDocument();
    await waitFor(() => expect(requests).toEqual(["GET /me/profile"]));
    unmount();
    release();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  });

  it("erro ao carregar: mensagem, 'Tentar de novo' e h1 mantido; tentar de novo recupera", async () => {
    let calls = 0;
    renderPage({
      load: () => {
        calls += 1;
        return calls === 1 ? problem(500, { code: "unknown_error" }) : HttpResponse.json(profile());
      },
    });
    expect(await screen.findByText("Não foi possível carregar seu perfil.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Editar perfil" })).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole("button", { name: "Tentar de novo" }));
    expect(await loaded()).toHaveValue("Jogadora Teste");
  });

  it("401 leva a S2 com aviso de sessao expirada", async () => {
    renderPage({ load: () => problem(401, { code: "unauthenticated" }) });
    await screen.findByText("pagina entrar");
    expect(screen.getByTestId("search").textContent).toContain("aviso=sessao-expirada");
  });

  it("carrega do servidor a cada entrada (sem cache de perfil)", async () => {
    const first = renderPage();
    await loaded();
    first.unmount();
    const second = renderPage();
    await loaded();
    expect(second.requests).toEqual(["GET /me/profile"]);
  });
});

describe("S9 Editar perfil - campos (REQ-014, REQ-017, REQ-037)", () => {
  it("preenche nome, bio e TCG favorito com os valores atuais; email nao aparece", async () => {
    renderPage();
    expect(await loaded()).toHaveValue("Jogadora Teste");
    expect(bioField()).toHaveValue("Gosto de decks de comandante.");
    expect(screen.getByLabelText("TCG favorito")).toHaveValue("mtg");
    expect(document.body.innerHTML).not.toMatch(/[\w.]+@[\w.]+\.\w+/);
  });

  it("o nome tem autocomplete=nickname e a dica 'de 3 a 30 caracteres'", async () => {
    renderPage();
    const name = await loaded();
    expect(name).toHaveAttribute("autocomplete", "nickname");
    expect(describedText(name)).toMatch(/de 3 a 30 caracteres/);
  });

  it("o Select de TCG tem so 'Magic: The Gathering' (REQ-017)", async () => {
    renderPage();
    await loaded();
    const options = within(screen.getByLabelText("TCG favorito")).getAllByRole("option");
    expect(options.map((o) => o.textContent)).toEqual(["Magic: The Gathering"]);
  });

  it("as opcoes do TCG vem de GET /catalog/favorite-tcgs", async () => {
    const calls: string[] = [];
    server.events.on("request:start", ({ request }) => {
      calls.push(new URL(request.url).pathname);
    });
    renderPage();
    await loaded();
    await waitFor(() => expect(calls).toContain("/api/v1/catalog/favorite-tcgs"));
    server.events.removeAllListeners();
  });

  it("mostra o avatar de 96 px e o botao 'Alterar avatar'", async () => {
    const { container } = renderPage();
    await loaded();
    const img = container.querySelector<HTMLImageElement>('img[src="/avatars/presets/p1.svg"]');
    expect(img).not.toBeNull();
    expect(img).toHaveAttribute("width", "96");
    expect(screen.getByRole("button", { name: "Alterar avatar" })).toBeInTheDocument();
  });

  it("Formatos vem de GET /catalog/formats como caixas em fieldset; o salvo vem marcado", async () => {
    renderPage();
    await loaded();
    const group = await screen.findByRole("group", { name: /Formatos preferidos/ });
    const boxes = within(group).getAllByRole("checkbox");
    expect(boxes.map((b) => (b as HTMLInputElement).labels?.[0]?.textContent)).toEqual([
      "Standard",
      "Modern",
      "Pioneer",
      "Commander",
      "Pauper",
    ]);
    expect(within(group).getByRole("checkbox", { name: "Modern" })).toBeChecked();
    expect(within(group).getByRole("checkbox", { name: "Commander" })).not.toBeChecked();
  });

  it("Formato novo no catalogo aparece sem mudanca de codigo (REQ-037)", async () => {
    renderPage({ catalog: () => HttpResponse.json([...CATALOG, { id: "brawl", name: "Brawl" }]) });
    await loaded();
    expect(await screen.findByRole("checkbox", { name: "Brawl" })).toBeInTheDocument();
  });
});

describe("S9 Editar perfil - catalogo de Formatos (WC-08, REQ-016)", () => {
  it("catalogo carregando mostra um Skeleton no lugar das caixas", async () => {
    renderPage({ catalog: () => new Promise<Response>(() => undefined) });
    await loaded();
    const group = screen.getByRole("group", { name: /Formatos preferidos/ });
    expect(group.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(within(group).queryAllByRole("checkbox")).toHaveLength(0);
  });

  it("catalogo vazio mostra 'Nenhum formato disponível no momento.' e o salvar continua possivel", async () => {
    const { puts } = renderPage({ catalog: () => HttpResponse.json([]) });
    await loaded();
    expect(await screen.findByText(NO_FORMATS)).toBeInTheDocument();
    await save(userEvent.setup());
    await waitFor(() => expect(puts).toHaveLength(1));
  });

  it("falha do catalogo mostra a mesma mensagem e o salvar continua possivel", async () => {
    const { puts } = renderPage({ catalog: () => problem(500, { code: "unknown_error" }) });
    await loaded();
    expect(await screen.findByText(NO_FORMATS)).toBeInTheDocument();
    expect(screen.queryByText(SAVE_ERROR)).not.toBeInTheDocument();
    await save(userEvent.setup());
    await waitFor(() => expect(puts).toHaveLength(1));
    await screen.findByText("pagina perfil");
  });

  // Decisao a confirmar com o usuario: reenviar a selecao ja carregada do perfil, nunca [].
  it("falha do catalogo nao envia formatIds vazio quando o perfil tinha formatos ativos", async () => {
    const { puts } = renderPage({ catalog: () => problem(500, { code: "unknown_error" }) });
    await loaded();
    await screen.findByText(NO_FORMATS);
    await save(userEvent.setup());
    await waitFor(() => expect(puts).toHaveLength(1));
    expect(puts[0].formatIds).toEqual(["modern"]);
  });
});

describe("S9 Editar perfil - salvar (REQ-014, REQ-016, REQ-046, UX-13)", () => {
  it("sucesso: PUT /me/profile com os campos do contrato e vai a S8 com 'Perfil atualizado.'", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    await replaceText(user, await loaded(), "Novo Nome");
    await replaceText(user, bioField(), "Nova bio");
    await screen.findByRole("checkbox", { name: "Commander" });
    await user.click(screen.getByRole("checkbox", { name: "Commander" }));
    await save(user);

    await screen.findByText("pagina perfil");
    expect(puts).toEqual([
      {
        displayName: "Novo Nome",
        bio: "Nova bio",
        favoriteTcg: "mtg",
        formatIds: ["modern", "commander"],
      },
    ]);
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil");
    expect(screen.getByTestId("flash")).toHaveTextContent("Perfil atualizado.");
  });

  it("bio e Formatos vazios sao aceitos (REQ-016)", async () => {
    const { puts } = renderPage({
      load: () => HttpResponse.json(profile({ bio: null, preferredFormats: [] })),
    });
    await loaded();
    await screen.findByRole("checkbox", { name: "Modern" });
    await save(userEvent.setup());
    await screen.findByText("pagina perfil");
    expect(puts).toHaveLength(1);
    expect(puts[0].bio ?? "").toBe("");
    expect(puts[0].formatIds).toEqual([]);
  });

  it("quebra de linha na bio e aceita e enviada", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    await loaded();
    await replaceText(user, bioField(), "linha 1\nlinha 2");
    await save(user);
    await screen.findByText("pagina perfil");
    expect(puts[0].bio).toBe("linha 1\nlinha 2");
  });

  it("nome repetido e aceito: nenhum erro de unicidade (REQ-037)", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    await replaceText(user, await loaded(), "Jogadora Repetida");
    await save(user);
    await screen.findByText("pagina perfil");
    expect(puts[0].displayName).toBe("Jogadora Repetida");
  });

  it("Formato inativo ja salvo nao aparece e salvar funciona sem removê-lo (UX-13, REQ-046)", async () => {
    const { puts } = renderPage({
      load: () =>
        HttpResponse.json(
          profile({
            preferredFormats: [
              { id: "modern", name: "Modern" },
              { id: "legacy", name: "Legacy" },
            ],
          }),
        ),
    });
    await loaded();
    await screen.findByRole("checkbox", { name: "Modern" });
    expect(screen.queryByRole("checkbox", { name: "Legacy" })).not.toBeInTheDocument();
    expect(screen.queryByText("Legacy")).not.toBeInTheDocument();
    await save(userEvent.setup());
    await screen.findByText("pagina perfil");
    expect(puts[0].formatIds).not.toContain("legacy");
    expect(puts[0].formatIds).toContain("modern");
  });

  it("marcacao digitada permanece literal (WC-05)", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    const nasty = "<b>x</b> **y**";
    const name = await loaded();
    await replaceText(user, name, nasty);
    expect(name).toHaveValue(nasty);
    await save(user);
    await screen.findByText("pagina perfil");
    expect(puts[0].displayName).toBe(nasty);
  });
});

describe("S9 Editar perfil - validacao por campo (REQ-015, REQ-040, WC-01)", () => {
  it("nome com menos de 3 caracteres: erro no campo e no resumo, sem PUT, dados mantidos", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    const name = await loaded();
    await replaceText(user, name, "ab");
    await replaceText(user, bioField(), "bio mantida");
    await save(user);

    const summary = await errorSummary();
    expect(within(summary).getByRole("link", { name: NAME_LENGTH })).toBeInTheDocument();
    expect(name).toHaveAttribute("aria-invalid", "true");
    expect(describedText(name)).toContain(NAME_LENGTH);
    expect(name).toHaveValue("ab");
    expect(bioField()).toHaveValue("bio mantida");
    expect(puts).toHaveLength(0);
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil/editar");
  });

  it("nome longo (200 caracteres) e rejeitado com a mensagem de tamanho e o texto mantido (WC-01)", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    const long = "N".repeat(200);
    const name = await loaded();
    await replaceText(user, name, long);
    await save(user);
    expect(await screen.findAllByText(NAME_LENGTH)).not.toHaveLength(0);
    expect(name).toHaveValue(long);
    expect(puts).toHaveLength(0);
  });

  it("nome com 30 elementos de texto (grapheme clusters) e aceito", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    const name = await loaded();
    await replaceText(user, name, "👨‍👩‍👧".repeat(30));
    await save(user);
    await screen.findByText("pagina perfil");
    expect(puts).toHaveLength(1);
  });

  it("erros do servidor sao traduzidos nos textos exatos, por campo e no resumo", async () => {
    const { puts } = renderPage({
      put: () =>
        problem(400, {
          code: "validation_failed",
          errors: {
            displayName: ["invalid_characters"],
            bio: ["invalid_characters"],
            favoriteTcg: ["invalid"],
            formatIds: ["invalid"],
          },
        }),
    });
    const user = userEvent.setup();
    const name = await loaded();
    await screen.findByRole("checkbox", { name: "Modern" });
    await save(user);

    const summary = await errorSummary();
    const messages = within(summary)
      .getAllByRole("link")
      .map((l) => l.textContent);
    expect(messages).toEqual([NAME_CONTROL, BIO_CONTROL, TCG_INVALID, FORMATS_INVALID]);
    expect(describedText(name)).toContain(NAME_CONTROL);
    expect(describedText(bioField())).toContain(BIO_CONTROL);
    expect(describedText(screen.getByLabelText("TCG favorito"))).toContain(TCG_INVALID);
    expect(screen.getByRole("group", { name: /Formatos preferidos/ })).toHaveTextContent(
      FORMATS_INVALID,
    );
    expect(puts).toHaveLength(1);
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil/editar");
  });

  it("servidor: nome com tamanho invalido e bio longa demais", async () => {
    renderPage({
      put: () =>
        problem(400, {
          code: "validation_failed",
          errors: { displayName: ["length"], bio: ["too_long"] },
        }),
    });
    await loaded();
    await save(userEvent.setup());
    const summary = await errorSummary();
    expect(
      within(summary)
        .getAllByRole("link")
        .map((l) => l.textContent),
    ).toEqual([NAME_LENGTH, BIO_TOO_LONG]);
  });

  it("o link do resumo leva o foco ao campo com erro", async () => {
    renderPage({
      put: () => problem(400, { code: "validation_failed", errors: { displayName: ["length"] } }),
    });
    const name = await loaded();
    const user = userEvent.setup();
    await save(user);
    const summary = await errorSummary();
    await user.click(within(summary).getByRole("link", { name: NAME_LENGTH }));
    expect(name).toHaveFocus();
  });
});

describe("S9 Editar perfil - contador da bio (WC-18, ART-05, MOT-15)", () => {
  it("o contador e ligado ao campo por aria-describedby e mostra 'N de 280'", async () => {
    renderPage({ load: () => HttpResponse.json(profile({ bio: "abc" })) });
    await loaded();
    expect(describedText(bioField())).toContain("3 de 280");
    const user = userEvent.setup();
    await replaceText(user, bioField(), "");
    expect(describedText(bioField())).toContain("0 de 280");
  });

  it("conta elementos de texto (grapheme clusters): 280 emojis compostos nao passam do limite", async () => {
    renderPage();
    const user = userEvent.setup();
    await loaded();
    await replaceText(user, bioField(), "👨‍👩‍👧".repeat(280));
    expect(describedText(bioField())).toContain("280 de 280");
    expect(describedText(bioField())).not.toMatch(/NaN|undefined/);
    expect(bioField()).not.toHaveAttribute("aria-invalid", "true");
  });

  it("281 de 280 vira erro, nao trunca a colagem e o envio e bloqueado com a mensagem da bio", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    await loaded();
    const text = "a".repeat(281);
    await replaceText(user, bioField(), text);
    expect(bioField()).toHaveValue(text);
    expect(describedText(bioField())).toContain("281 de 280");
    expect(bioField()).toHaveAttribute("aria-invalid", "true");
    await save(user);
    expect(await screen.findAllByText(BIO_TOO_LONG)).not.toHaveLength(0);
    expect(puts).toHaveLength(0);
  });

  it("o contador nao usa aria-live nem anima (MOT-15)", async () => {
    renderPage();
    await loaded();
    const counterHost = document.getElementById(
      (bioField().getAttribute("aria-describedby") ?? "").split(" ").pop() ?? "",
    );
    expect(counterHost).not.toBeNull();
    expect(counterHost?.closest("[aria-live]")).toBeNull();
    expect(counterHost?.getAttribute("style") ?? "").not.toMatch(/transition|animation/);
  });
});

describe("S9 Editar perfil - erro de servidor e envio (REQ-014)", () => {
  it("500: Alert 'Não foi possível salvar. Tente de novo.' e os dados digitados ficam preservados", async () => {
    renderPage({ put: () => problem(500, { code: "unknown_error" }) });
    const user = userEvent.setup();
    await replaceText(user, await loaded(), "Outro Nome");
    await replaceText(user, bioField(), "outra bio");
    await screen.findByRole("checkbox", { name: "Commander" });
    await user.click(screen.getByRole("checkbox", { name: "Commander" }));
    await save(user);

    expect(await screen.findByText(SAVE_ERROR)).toBeInTheDocument();
    expect(screen.getByLabelText("Nome de exibição")).toHaveValue("Outro Nome");
    expect(bioField()).toHaveValue("outra bio");
    expect(screen.getByRole("checkbox", { name: "Commander" })).toBeChecked();
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil/editar");
  });

  it("durante o envio o botao fica em loading e nao envia duas vezes", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { puts } = renderPage({
      put: async () => {
        await gate;
        return HttpResponse.json(profile());
      },
    });
    const user = userEvent.setup();
    await loaded();
    await save(user);
    await waitFor(() => expect(puts).toHaveLength(1));
    const button = screen.getByRole("button", { name: /Salvar alterações/ });
    expect(button).toHaveAttribute("aria-busy", "true");
    await user.click(button);
    expect(puts).toHaveLength(1);
    release();
    await screen.findByText("pagina perfil");
  });
});

describe("S9 Editar perfil - navegacao (U13, REQ-014)", () => {
  it("Cancelar volta a S8 sem confirmacao de descarte e sem enviar nada", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    await replaceText(user, await loaded(), "Mudei de ideia");
    const cancel = [
      ...screen.queryAllByRole("button", { name: "Cancelar" }),
      ...screen.queryAllByRole("link", { name: "Cancelar" }),
    ];
    expect(cancel).toHaveLength(1);
    await user.click(cancel[0]);
    await screen.findByText("pagina perfil");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(puts).toHaveLength(0);
    expect(screen.getByTestId("flash")).toHaveTextContent("");
  });

  it("'Alterar avatar' abre S10 sem enviar nem descartar os demais campos", async () => {
    const { puts } = renderPage();
    const user = userEvent.setup();
    const name = await loaded();
    await replaceText(user, name, "Nome Pendente");
    await user.click(screen.getByRole("button", { name: "Alterar avatar" }));
    expect(await screen.findByRole("dialog", { name: "Alterar avatar" })).toBeInTheDocument();
    expect(puts).toHaveLength(0);
    expect(screen.getByTestId("location")).toHaveTextContent("/perfil/editar");
    expect(name).toHaveValue("Nome Pendente");
  });
});

describe("S9 Editar perfil - hierarquia de botoes (ART-02)", () => {
  it("'Salvar alterações' e o unico controle preenchido (primario)", async () => {
    renderPage();
    await loaded();
    const filled = document.querySelectorAll('[data-variant="primary"]');
    expect(filled).toHaveLength(1);
    expect(filled[0]).toHaveTextContent("Salvar alterações");
  });
});

describe("S9 Editar perfil - acessibilidade", () => {
  it("nao ha violacoes de acessibilidade (axe) com o formulario carregado", async () => {
    const { container } = renderPage();
    await loaded();
    await screen.findByRole("checkbox", { name: "Standard" });
    await expectNoA11yViolations(container);
  });

  it("nao ha violacoes de acessibilidade (axe) com erros por campo", async () => {
    const { container } = renderPage();
    const user = userEvent.setup();
    await replaceText(user, await loaded(), "ab");
    await save(user);
    await errorSummary();
    await expectNoA11yViolations(container);
  });
});
