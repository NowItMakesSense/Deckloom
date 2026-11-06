// T-115 (RED): S8 Perfil - leitura, estados, texto literal, formatos, exclusao via query e alertas.
// REQ-013, REQ-016, REQ-017, REQ-044, REQ-045, REQ-046; UX-05, UX-06, UX-07, UX-13; A11Y-35;
// WC-01/02/04/05/08/10/13/17 (so o que e verificavel em jsdom); MOT-05, MOT-15; ART-02.
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { PlayerRoute } from "../../app/guards";
import { SessionProvider } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { ProfilePage } from "./ProfilePage";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const PROFILE_URL = `${BASE}/api/v1/me/profile`;

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  server.resetHandlers();
  vi.restoreAllMocks();
});
afterAll(() => server.close());

interface Fmt {
  id: string;
  name: string;
}
interface ProfileBody {
  displayName: string;
  bio: string | null;
  favoriteTcg: { id: "mtg"; name: string };
  preferredFormats: Fmt[];
  avatar: object;
  hasPassword: boolean;
  hasGoogle: boolean;
}

const PRESET_AVATAR = {
  kind: "preset",
  presetId: "p1",
  imageUrl: "/avatars/presets/p1.svg",
  alt: "Avatar: Dragao",
};

function profile(overrides: Partial<ProfileBody> = {}): ProfileBody {
  return {
    displayName: "Jogadora Teste",
    bio: "Gosto de decks de comandante.",
    favoriteTcg: { id: "mtg", name: "Magic: The Gathering" },
    preferredFormats: [
      { id: "modern", name: "Modern" },
      { id: "commander", name: "Commander" },
    ],
    avatar: PRESET_AVATAR,
    hasPassword: true,
    hasGoogle: false,
    ...overrides,
  };
}

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

type ProfileResponder = () => Response | Promise<Response>;

interface Entry {
  url?: string;
  state?: object;
  hasPassword?: boolean;
}

function renderPage(
  respond: ProfileResponder = () => HttpResponse.json(profile()),
  entry: Entry = {},
) {
  const requests: string[] = [];
  server.use(
    http.get(SESSION_URL, () =>
      HttpResponse.json({
        displayName: "Jogadora Teste",
        avatar: PRESET_AVATAR,
        hasPassword: entry.hasPassword ?? true,
        hasGoogle: entry.hasPassword === false,
      }),
    ),
    http.get(PROFILE_URL, () => {
      requests.push("GET /me/profile");
      return respond();
    }),
  );
  const view = render(
    <MemoryRouter
      initialEntries={[
        {
          pathname: "/perfil",
          search: "",
          state: entry.state,
          ...(entry.url ? parse(entry.url) : {}),
        },
      ]}
    >
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route element={<PlayerRoute />}>
            <Route path="/perfil" element={<ProfilePage />} />
          </Route>
          <Route path="/entrar" element={<main>pagina entrar</main>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...view, requests };
}

function parse(url: string): { pathname: string; search: string } {
  const [pathname, search = ""] = url.split("?");
  return { pathname, search: search ? `?${search}` : "" };
}

async function loaded() {
  return screen.findByText("Jogadora Teste", { selector: "*" }, { timeout: 3000 });
}

function byNameAnyRole(name: string): HTMLElement {
  const found = [
    ...screen.queryAllByRole("button", { name }),
    ...screen.queryAllByRole("link", { name }),
  ];
  if (found.length !== 1)
    throw new Error(`esperava exatamente um controle "${name}", achei ${found.length}`);
  return found[0];
}

describe("S8 Perfil - carregamento (WC-13, REQ-013)", () => {
  it("mostra o h1 e um Skeleton aria-busy enquanto carrega, sem 'Carregando...' no inicio", async () => {
    renderPage(() => new Promise<Response>(() => undefined));
    expect(
      await screen.findByRole("heading", { level: 1, name: "Meu perfil" }),
    ).toBeInTheDocument();
    expect(document.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(screen.queryByText("Carregando...")).not.toBeInTheDocument();
  });

  it("mostra 'Carregando...' depois de 2 s de espera", async () => {
    renderPage(() => new Promise<Response>(() => undefined));
    await screen.findByRole("heading", { level: 1, name: "Meu perfil" });
    expect(await screen.findByText("Carregando...", {}, { timeout: 3500 })).toBeInTheDocument();
  });

  it("o Skeleton some quando o perfil chega", async () => {
    renderPage();
    await loaded();
    expect(document.querySelector('[aria-busy="true"]')).toBeNull();
  });
});

describe("S8 Perfil - dados (REQ-013, REQ-016, REQ-017, UX-06)", () => {
  it("mostra nome, Magic: The Gathering, Formatos como Tags e a bio", async () => {
    const { container } = renderPage();
    await loaded();
    expect(screen.getByRole("heading", { level: 1, name: "Meu perfil" })).toBeInTheDocument();
    expect(screen.getByText("Magic: The Gathering")).toBeInTheDocument();
    const tags = [...container.querySelectorAll(".tag")].map((tag) => tag.textContent);
    expect(tags).toEqual(["Modern", "Commander"]);
    expect(screen.getByText("Gosto de decks de comandante.")).toBeInTheDocument();
  });

  it("o avatar tem 96 px e usa a imagem do perfil (UX-07)", async () => {
    const { container } = renderPage();
    await loaded();
    const img = container.querySelector<HTMLImageElement>('img[src="/avatars/presets/p1.svg"]');
    expect(img).not.toBeNull();
    expect(img).toHaveAttribute("width", "96");
    expect(img).toHaveAttribute("height", "96");
  });

  it("avatar que nao carrega cai no fallback com a inicial do nome (WC-10)", async () => {
    const { container } = renderPage();
    await loaded();
    const img = container.querySelector<HTMLImageElement>('img[src="/avatars/presets/p1.svg"]');
    fireEvent.error(img as HTMLImageElement);
    await waitFor(() => expect(container.querySelector(".avatar--fallback")).not.toBeNull());
    expect(container.querySelector(".avatar--fallback")).toHaveTextContent("J");
  });

  it("opcionais ausentes mantem 'Nenhuma bio.' e 'Nenhum formato selecionado.' (WC-17)", async () => {
    const { container } = renderPage(() =>
      HttpResponse.json(profile({ bio: null, preferredFormats: [] })),
    );
    await loaded();
    expect(screen.getByText("Nenhuma bio.")).toBeInTheDocument();
    expect(screen.getByText("Nenhum formato selecionado.")).toBeInTheDocument();
    expect(container.querySelectorAll(".tag")).toHaveLength(0);
    expect(screen.getByText("Magic: The Gathering")).toBeInTheDocument();
  });

  it.each([[0], [1], [50]])("renderiza %i Formatos como Tags (WC-08)", async (count) => {
    const formats = Array.from({ length: count }, (_, i) => ({
      id: `f${i}`,
      name: `Formato ${i}`,
    }));
    const { container } = renderPage(() =>
      HttpResponse.json(profile({ preferredFormats: formats })),
    );
    await loaded();
    expect(container.querySelectorAll(".tag")).toHaveLength(count);
    if (count === 0) expect(screen.getByText("Nenhum formato selecionado.")).toBeInTheDocument();
  });

  it("Formato com nome de 60 caracteres aparece inteiro (WC-08)", async () => {
    const name = "F".repeat(60);
    renderPage(() => HttpResponse.json(profile({ preferredFormats: [{ id: "x", name }] })));
    await loaded();
    expect(screen.getByText(name)).toBeInTheDocument();
  });

  it("nome e bio tem dir=auto (WC-02)", async () => {
    renderPage();
    await loaded();
    expect(screen.getByText("Jogadora Teste", { selector: "[dir]" })).toHaveAttribute(
      "dir",
      "auto",
    );
    expect(screen.getByText("Gosto de decks de comandante.")).toHaveAttribute("dir", "auto");
  });
});

describe("S8 Perfil - Formatos ativos e inativos (REQ-046, UX-13)", () => {
  it("Formato inativado nao aparece e reaparece quando reativado", async () => {
    const first = renderPage(() =>
      HttpResponse.json(profile({ preferredFormats: [{ id: "modern", name: "Modern" }] })),
    );
    await loaded();
    expect(screen.queryByText("Legacy")).not.toBeInTheDocument();
    first.unmount();

    renderPage(() =>
      HttpResponse.json(
        profile({
          preferredFormats: [
            { id: "modern", name: "Modern" },
            { id: "legacy", name: "Legacy" },
          ],
        }),
      ),
    );
    await loaded();
    expect(screen.getByText("Legacy")).toBeInTheDocument();
  });
});

describe("S8 Perfil - texto literal (A11Y-35, WC-05, REQ-016)", () => {
  const nasty = '<b>x</b> &amp; **x** <img src=x onerror="alert(1)">';

  it("nome e bio com HTML/markdown aparecem literalmente como texto", async () => {
    const { container } = renderPage(() =>
      HttpResponse.json(profile({ displayName: nasty, bio: nasty })),
    );
    await screen.findAllByText(nasty, { exact: false }, { timeout: 3000 });
    expect(screen.getAllByText(nasty, { exact: false }).length).toBeGreaterThanOrEqual(2);
    expect(container.querySelector("main b")).toBeNull();
    expect(container.querySelector('img[src="x"]')).toBeNull();
  });

  it("quebras de linha da bio sao preservadas", async () => {
    const { container } = renderPage(() => HttpResponse.json(profile({ bio: "linha 1\nlinha 2" })));
    await loaded();
    const bio = screen.getByText(/linha 1/);
    const preserved =
      bio.textContent?.includes("linha 1\nlinha 2") || bio.innerHTML.includes("<br");
    expect(preserved).toBe(true);
    expect(container.querySelector("main")).not.toBeNull();
  });
});

describe("S8 Perfil - bio longa (WC-04, MOT-15)", () => {
  const manyLines = Array.from({ length: 280 }, (_, i) => `l${i}`).join("\n");
  const oneWord = "a".repeat(280);

  it.each([
    ["280 quebras de linha", manyLines, "l279"],
    ["uma palavra de 280 caracteres", oneWord, oneWord],
  ])("%s: 'Mostrar tudo' com aria-expanded e texto completo alcancavel", async (_n, bio, tail) => {
    renderPage(() => HttpResponse.json(profile({ bio })));
    await loaded();
    const toggle = screen.getByRole("button", { name: "Mostrar tudo" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(document.body.textContent).toContain(tail);
    await userEvent.setup().click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(document.body.textContent).toContain(tail);
  });

  it("bio curta nao tem 'Mostrar tudo'", async () => {
    renderPage();
    await loaded();
    expect(screen.queryByRole("button", { name: "Mostrar tudo" })).not.toBeInTheDocument();
  });

  it("recolher/expandir nao anima height por estilo inline (MOT-15)", async () => {
    const { container } = renderPage(() => HttpResponse.json(profile({ bio: manyLines })));
    await loaded();
    await userEvent.setup().click(screen.getByRole("button", { name: "Mostrar tudo" }));
    for (const el of container.querySelectorAll<HTMLElement>("[style]")) {
      expect(el.style.height).toBe("");
      expect(el.style.transition).not.toMatch(/height/);
    }
  });
});

describe("S8 Perfil - email e hash nunca aparecem (UX-05, REQ-045)", () => {
  it("a tela nao mostra email nem hash, mesmo que a resposta os traga por engano", async () => {
    const leaky = {
      ...profile(),
      email: "jogadora@exemplo.com",
      passwordHash: "$argon2id$v=19$abc",
    };
    renderPage(() => HttpResponse.json(leaky));
    await loaded();
    expect(document.body.textContent).not.toContain("jogadora@exemplo.com");
    expect(document.body.textContent).not.toContain("argon2");
    expect(document.body.innerHTML).not.toMatch(/[\w.]+@[\w.]+\.\w+/);
  });

  it("a unica leitura e GET /me/profile, sem identificador de jogador nem email na URL", async () => {
    const { requests } = renderPage();
    await loaded();
    expect(requests).toEqual(["GET /me/profile"]);
  });
});

describe("S8 Perfil - erros de carga (REQ-013)", () => {
  it("erro de carga mostra a mensagem e 'Tentar de novo', mantendo o h1; tentar de novo recupera", async () => {
    let calls = 0;
    renderPage(() => {
      calls += 1;
      return calls === 1 ? problem(500, { code: "unknown_error" }) : HttpResponse.json(profile());
    });
    expect(await screen.findByText("Não foi possível carregar seu perfil.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Meu perfil" })).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole("button", { name: "Tentar de novo" }));
    expect(await screen.findByText("Magic: The Gathering")).toBeInTheDocument();
    expect(screen.queryByText("Não foi possível carregar seu perfil.")).not.toBeInTheDocument();
  });

  it("401 leva a S2 com aviso de sessao expirada", async () => {
    renderPage(() => problem(401, { code: "unauthenticated" }));
    await screen.findByText("pagina entrar");
    expect(screen.getByTestId("location").textContent).toContain("aviso=sessao-expirada");
  });
});

describe("S8 Perfil - exclusao via query (REQ-044)", () => {
  it("?exclusao=confirmar abre a confirmacao final e remove o parametro da URL", async () => {
    renderPage(undefined, { url: "/perfil?exclusao=confirmar" });
    const dialog = await screen.findByRole(
      "alertdialog",
      { name: "Confirmar exclusão" },
      { timeout: 3000 },
    );
    expect(dialog).toHaveTextContent("Você confirmou sua identidade. Excluir sua conta agora?");
    expect(
      within(dialog).getByRole("button", { name: "Excluir minha conta definitivamente" }),
    ).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("location").textContent).toBe("/perfil"));
  });

  it("?exclusao=falhou mostra a mensagem e remove o parametro da URL", async () => {
    renderPage(undefined, { url: "/perfil?exclusao=falhou" });
    expect(
      await screen.findByText(
        "Não foi possível confirmar sua identidade. Sua conta não foi excluída.",
        {},
        { timeout: 3000 },
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("location").textContent).toBe("/perfil"));
  });

  it("sem parametro nao ha dialog nem mensagem de falha", async () => {
    renderPage();
    await loaded();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(screen.queryByText(/Não foi possível confirmar sua identidade/)).not.toBeInTheDocument();
  });
});

describe("S8 Perfil - 'Excluir conta' escolhe o ramo por hasPassword (T-119, REQ-023)", () => {
  it("hasPassword true abre o dialog de senha", async () => {
    const user = userEvent.setup();
    renderPage(() => HttpResponse.json(profile({ hasPassword: true })), { hasPassword: true });
    await loaded();
    await user.click(screen.getByRole("button", { name: "Excluir conta" }));
    const dialog = await screen.findByRole("alertdialog", { name: "Excluir conta" });
    expect(within(dialog).getByLabelText("Senha")).toBeInTheDocument();
    expect(
      within(dialog).getByRole("button", { name: "Excluir minha conta definitivamente" }),
    ).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: "Reautenticar com o Google" })).toBeNull();
  });

  it("hasPassword false abre o dialog de reautenticacao Google", async () => {
    const user = userEvent.setup();
    renderPage(() => HttpResponse.json(profile({ hasPassword: false, hasGoogle: true })), {
      hasPassword: false,
    });
    await loaded();
    await user.click(screen.getByRole("button", { name: "Excluir conta" }));
    const dialog = await screen.findByRole("alertdialog", { name: "Excluir conta" });
    expect(
      within(dialog).getByRole("button", { name: "Reautenticar com o Google" }),
    ).toBeInTheDocument();
    expect(within(dialog).queryByLabelText("Senha")).toBeNull();
  });
});

describe("S8 Perfil - acoes e zona de perigo (ART-02)", () => {
  it("tem Editar perfil, Alterar avatar e Excluir conta; Excluir conta fica na zona de perigo", async () => {
    renderPage();
    await loaded();
    expect(byNameAnyRole("Editar perfil")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Alterar avatar" })).toBeInTheDocument();
    const del = screen.getByRole("button", { name: "Excluir conta" });
    const zone = del.closest("section, [role=group], [role=region]");
    expect(zone).not.toBeNull();
    expect(zone).toHaveTextContent("Zona de perigo");
    expect(zone).not.toContainElement(byNameAnyRole("Editar perfil"));
  });

  it("'Editar perfil' e o unico controle preenchido (primario)", async () => {
    renderPage();
    await loaded();
    const filled = document.querySelectorAll('[data-variant="primary"]');
    expect(filled).toHaveLength(1);
    expect(filled[0]).toHaveTextContent("Editar perfil");
  });
});

describe("S8 Perfil - 'Alterar avatar' abre o dialogo da S10 (T-125)", () => {
  it("abre o dialogo e, apos escolher um preset, o avatar do perfil muda sem recarregar", async () => {
    const user = userEvent.setup();
    const golem = {
      kind: "preset",
      presetId: "p2",
      imageUrl: "/avatars/presets/p2.svg",
      alt: "Avatar: Golem",
    };
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "csrf-token" })),
      http.get(`${BASE}/api/v1/catalog/avatar-presets`, () =>
        HttpResponse.json([
          { id: "p1", label: "Dragao", imageUrl: "/avatars/presets/p1.svg" },
          { id: "p2", label: "Golem", imageUrl: "/avatars/presets/p2.svg" },
        ]),
      ),
      http.put(`${BASE}/api/v1/me/avatar/preset`, () => HttpResponse.json({ avatar: golem })),
    );
    const { container, requests } = renderPage();
    await loaded();
    expect(container.querySelector('main img[src="/avatars/presets/p1.svg"]')).not.toBeNull();

    await user.click(screen.getByRole("button", { name: "Alterar avatar" }));
    const dialog = await screen.findByRole("dialog", { name: "Alterar avatar" });
    await user.click(await within(dialog).findByRole("radio", { name: "Golem" }));
    await user.click(within(dialog).getByRole("button", { name: "Usar este avatar" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(
      container.querySelector('.profile__identity img[src="/avatars/presets/p2.svg"]'),
    ).not.toBeNull();
    expect(
      container.querySelector('.profile__identity img[src="/avatars/presets/p1.svg"]'),
    ).toBeNull();
    expect(requests).toEqual(["GET /me/profile"]);
  });
});

describe("S8 Perfil - alerta de sucesso (MOT-05)", () => {
  it("mostra a mensagem vinda de salvar/criar/vincular/avatar com role=status", async () => {
    renderPage(undefined, {
      state: { message: "Google vinculado. Da próxima vez você pode entrar com o Google." },
    });
    await loaded();
    const status = screen.getByRole("status");
    expect(status).toHaveTextContent(
      "Google vinculado. Da próxima vez você pode entrar com o Google.",
    );
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("sem mensagem de origem nao mostra alerta de sucesso", async () => {
    renderPage();
    await loaded();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});

describe("S8 Perfil - acessibilidade", () => {
  it("nao ha violacoes de acessibilidade (axe)", async () => {
    const { container } = renderPage();
    await loaded();
    await expectNoA11yViolations(container);
  });
});
