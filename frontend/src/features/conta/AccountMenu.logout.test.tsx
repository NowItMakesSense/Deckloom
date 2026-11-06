// T-120 (RED): logout pelo AccountMenu e historico sem dados.
// REQ-010, REQ-011; UX-09; MOT-10, MOT-11, MOT-15 (so o efeito funcional verificavel em jsdom).
// Premissa: o AppHeader com AccountMenu ligado a sessao e renderizado por AppRoutes (ver duvidas).
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { useEffect, useRef } from "react";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it } from "vitest";
import { AppRoutes } from "../../app/AppRoutes";
import { SessionProvider } from "../../app/SessionProvider";
import { fillField } from "@/shared/testing/fillField";

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const PROFILE_URL = `${BASE}/api/v1/me/profile`;
const LOGOUT_URL = `${BASE}/api/v1/auth/logout`;
const DELETION_URL = `${BASE}/api/v1/me/deletion`;

const NAME = "Jogadora Teste";
const MENU = `Menu da conta: ${NAME}`;
const PANEL = "[data-account-menu-panel]";

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

const AVATAR = {
  kind: "preset",
  presetId: "p1",
  imageUrl: "/avatars/presets/p1.svg",
  alt: "Avatar: Dragao",
};

const PROFILE = {
  displayName: NAME,
  bio: "Gosto de decks de comandante.",
  favoriteTcg: { id: "mtg", name: "Magic: The Gathering" },
  preferredFormats: [{ id: "commander", name: "Commander" }],
  avatar: AVATAR,
  hasPassword: true,
  hasGoogle: false,
};

function unauthenticated() {
  return HttpResponse.json(
    { code: "unauthenticated" },
    { status: 401, headers: { "Content-Type": "application/problem+json" } },
  );
}

interface Calls {
  session: number;
  profile: number;
  logout: number;
  menuOpenAtNavigation: boolean[];
}

interface Options {
  profile?: () => Response | Promise<Response>;
  logoutFails?: boolean;
}

// Backend falso com estado: a sessao existe ate o logout/exclusao.
function installBackend(options: Options = {}): Calls {
  const calls: Calls = { session: 0, profile: 0, logout: 0, menuOpenAtNavigation: [] };
  let signedIn = true;
  server.use(
    http.get(SESSION_URL, () => {
      calls.session += 1;
      return signedIn
        ? HttpResponse.json({
            displayName: NAME,
            avatar: AVATAR,
            hasPassword: true,
            hasGoogle: false,
          })
        : unauthenticated();
    }),
    http.get(PROFILE_URL, () => {
      calls.profile += 1;
      if (!signedIn) return unauthenticated();
      return options.profile ? options.profile() : HttpResponse.json(PROFILE);
    }),
    http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "t" })),
    http.post(LOGOUT_URL, () => {
      calls.logout += 1;
      if (options.logoutFails) return new HttpResponse(null, { status: 500 });
      signedIn = false;
      return new HttpResponse(null, { status: 204 });
    }),
    http.get(`${BASE}/avatars/*`, () => new HttpResponse(null, { status: 200 })),
    http.get(`${BASE}/api/v1/avatars/*`, () => new HttpResponse(null, { status: 200 })),
    http.get(`${BASE}/api/v1/catalog/favorite-tcgs`, () =>
      HttpResponse.json([{ id: "mtg", name: "Magic: The Gathering" }]),
    ),
    http.get(`${BASE}/api/v1/catalog/formats`, () =>
      HttpResponse.json([{ id: "commander", name: "Commander" }]),
    ),
    http.post(DELETION_URL, () => {
      signedIn = false;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return calls;
}

function Probe({ calls }: { calls: Calls }) {
  const location = useLocation();
  const first = useRef(true);
  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }
    // MOT-11: ao mudar de rota o painel do menu ja deve estar fechado.
    calls.menuOpenAtNavigation.push(document.querySelector(PANEL) !== null);
  }, [location, calls]);
  return <p data-testid="location">{location.pathname + location.search}</p>;
}

function BackButton() {
  const navigate = useNavigate();
  return (
    <button type="button" onClick={() => navigate(-1)}>
      voltar-do-navegador
    </button>
  );
}

function renderApp(calls: Calls, entries: string[] = ["/perfil"]) {
  return render(
    <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
      <SessionProvider baseUrl={BASE}>
        <AppRoutes />
        <Probe calls={calls} />
        <BackButton />
      </SessionProvider>
    </MemoryRouter>,
  );
}

function location(): string {
  return screen.getByTestId("location").textContent ?? "";
}

async function openMenu(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: MENU }, { timeout: 3000 }));
}

async function signOut(user: ReturnType<typeof userEvent.setup>) {
  await openMenu(user);
  await user.click(screen.getByRole("button", { name: "Sair" }));
}

describe("AccountMenu - Sair (REQ-010)", () => {
  it("chama POST /auth/logout e vai a /entrar?aviso=saiu com 'Você saiu da sua conta.' em role=status", async () => {
    const user = userEvent.setup();
    const calls = installBackend();
    renderApp(calls);

    await signOut(user);

    await waitFor(() => expect(location()).toBe("/entrar?aviso=saiu"));
    expect(calls.logout).toBe(1);
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Você saiu da sua conta.");
  });

  it("nao mostra dados do Jogador depois do logout (sem cache de perfil)", async () => {
    const user = userEvent.setup();
    const calls = installBackend();
    renderApp(calls);
    await screen.findByText(PROFILE.bio);

    await signOut(user);

    await waitFor(() => expect(location()).toBe("/entrar?aviso=saiu"));
    expect(screen.queryByText(PROFILE.bio)).not.toBeInTheDocument();
    expect(screen.queryByText(NAME)).not.toBeInTheDocument();
  });
});

describe("AccountMenu - Sair com falha (REQ-010)", () => {
  it("se POST /auth/logout falha (500), limpa a sessao local e vai a /entrar?aviso=saiu", async () => {
    const user = userEvent.setup();
    const calls = installBackend({ logoutFails: true });
    renderApp(calls);
    await screen.findByText(PROFILE.bio);

    await signOut(user);

    await waitFor(() => expect(location()).toBe("/entrar?aviso=saiu"));
    expect(calls.logout).toBe(1);
    expect(screen.queryByRole("button", { name: MENU })).not.toBeInTheDocument();
    expect(screen.queryByText(PROFILE.bio)).not.toBeInTheDocument();
    expect(await screen.findByRole("status")).toHaveTextContent("Você saiu da sua conta.");
  });
});

describe("AccountMenu - MOT-11 e MOT-15", () => {
  it("MOT-11: o painel do menu ja esta fechado quando a navegacao acontece", async () => {
    const user = userEvent.setup();
    const calls = installBackend();
    renderApp(calls);

    await signOut(user);

    await waitFor(() => expect(location()).toBe("/entrar?aviso=saiu"));
    // QA-13: o Probe registra num useEffect, depois do render que mostra a nova rota; espera o registro.
    await waitFor(() => expect(calls.menuOpenAtNavigation.length).toBeGreaterThan(0));
    expect(calls.menuOpenAtNavigation.every((open) => !open)).toBe(true);
  });

  it("MOT-15: avatar e nome somem do header na hora e o visitante ve Entrar / Criar conta", async () => {
    const user = userEvent.setup();
    const calls = installBackend();
    renderApp(calls);

    await signOut(user);

    await screen.findByRole("status");
    expect(screen.queryByRole("button", { name: MENU })).not.toBeInTheDocument();
    expect(screen.queryByText(NAME)).not.toBeInTheDocument();
    const nav = screen.getByRole("navigation", { name: "Acesso" });
    expect(within(nav).getByRole("link", { name: "Entrar" })).toBeInTheDocument();
    expect(within(nav).getByRole("link", { name: "Criar conta" })).toBeInTheDocument();
  });
});

describe("UX-09 - 'Voltar' do navegador sem dados (REQ-011)", () => {
  it("apos logout, Voltar nao mostra o perfil com dados e cai no login", async () => {
    const user = userEvent.setup();
    const calls = installBackend();
    renderApp(calls, ["/entrar", "/perfil"]);
    await screen.findByText(PROFILE.bio);
    const profileRequests = calls.profile;

    await signOut(user);
    await waitFor(() => expect(location()).toBe("/entrar?aviso=saiu"));
    await user.click(screen.getByRole("button", { name: "voltar-do-navegador" }));

    // replace: a entrada do perfil foi substituida, entao Voltar chega ao /entrar anterior.
    await waitFor(() => expect(location()).toBe("/entrar"));
    expect(screen.queryByText(PROFILE.bio)).not.toBeInTheDocument();
    expect(screen.queryByText(NAME)).not.toBeInTheDocument();
    expect(calls.profile).toBe(profileRequests);
  });

  it("apos logout, Voltar para a edicao de perfil tambem nao mostra dados", async () => {
    const user = userEvent.setup();
    const calls = installBackend();
    renderApp(calls, ["/entrar", "/perfil/editar"]);
    await screen.findByRole("button", { name: MENU }, { timeout: 3000 });

    await signOut(user);
    await waitFor(() => expect(location()).toBe("/entrar?aviso=saiu"));
    await user.click(screen.getByRole("button", { name: "voltar-do-navegador" }));

    await waitFor(() => expect(location().startsWith("/entrar")).toBe(true));
    expect(screen.queryByDisplayValue(NAME)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: MENU })).not.toBeInTheDocument();
  });

  it("apos excluir a conta, Voltar nao mostra o perfil com dados e cai no login", async () => {
    const user = userEvent.setup();
    const calls = installBackend();
    renderApp(calls, ["/entrar", "/perfil"]);
    await openMenu(user); // garante que o header do Jogador existiu antes da exclusao
    await user.keyboard("{Escape}");
    await user.click(await screen.findByRole("button", { name: "Excluir conta" }));
    const dialog = await screen.findByRole("alertdialog", { name: "Excluir conta" });
    await user.click(within(dialog).getByLabelText("Entendo que a exclusão é definitiva."));
    await fillField(user, within(dialog).getByLabelText("Senha"), "Senha#Valida1");
    await user.click(
      within(dialog).getByRole("button", { name: "Excluir minha conta definitivamente" }),
    );
    await waitFor(() => expect(location()).toBe("/entrar?aviso=conta-excluida"));
    const profileRequests = calls.profile;

    await user.click(screen.getByRole("button", { name: "voltar-do-navegador" }));

    await waitFor(() => expect(location().startsWith("/entrar")).toBe(true));
    expect(screen.queryByText(PROFILE.bio)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: MENU })).not.toBeInTheDocument();
    expect(calls.profile).toBe(profileRequests);
  });
});

describe("401 posterior limpa o estado (REQ-011)", () => {
  it("um 401 numa chamada autenticada deixa a sessao guest, sem dados nem menu, e vai ao login", async () => {
    const calls = installBackend({
      profile: async () => {
        await delay(150);
        return unauthenticated();
      },
    });
    renderApp(calls);

    // O header do Jogador existe enquanto o perfil ainda carrega...
    await screen.findByRole("button", { name: MENU }, { timeout: 3000 });
    // ...e some quando o 401 chega.
    await waitFor(() => expect(location().startsWith("/entrar")).toBe(true), { timeout: 3000 });
    expect(location()).toContain("aviso=sessao-expirada");
    expect(screen.queryByRole("button", { name: MENU })).not.toBeInTheDocument();
    expect(screen.queryByText(NAME)).not.toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Acesso" })).toBeInTheDocument();
  });
});
