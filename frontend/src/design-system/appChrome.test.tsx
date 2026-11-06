import { render as rtlRender, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

import { expectNoA11yViolations } from "../shared/testing/expectNoA11yViolations";
import { AccountMenu } from "./AccountMenu";
import { AppHeader } from "./AppHeader";
import { SkipLink } from "./SkipLink";

// Link, AppHeader e AccountMenu usam o router (R-19): todo render ganha um MemoryRouter.
const render = (ui: ReactElement) => rtlRender(ui, { wrapper: MemoryRouter });

// T-102 (habilitadora): A11Y-23, A11Y-30, A11Y-33, UX-07; MOT-11, WC-03, A11Y-38, ART-01.
// Props nao fixadas pela spec (decisao do test): AccountMenu {displayName, avatar, onSignOut};
// AppHeader {player?, onSignOut?}; SkipLink {targetId = "conteudo"}.
// Lacunas: layout real (320px, ocultar nome < 480px, reticencias) e animacao nao existem em
// jsdom; verificados so por varredura do CSS. Fixtures WC-01 ficam para captura/teste de layout.

const styles = import.meta.glob("./*.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

const avatar = { src: "/avatars/preset-1.png", label: "Raposa" };
const NAME = "Bia Souza";
const SKIP = /pular para o conte/i;

function Page({ player }: { player?: { displayName: string; avatar: typeof avatar } }) {
  return (
    <div>
      <SkipLink />
      <AppHeader player={player} onSignOut={() => {}} />
      <main id="conteudo" tabIndex={-1}>
        <h1>Conteudo</h1>
        <button type="button">Acao</button>
      </main>
    </div>
  );
}

describe("SkipLink (A11Y-23)", () => {
  it("existe e aponta para o main", () => {
    render(<Page />);
    expect(screen.getByRole("link", { name: SKIP })).toHaveAttribute("href", "#conteudo");
  });

  it("e o primeiro elemento focavel da pagina", async () => {
    render(<Page player={{ displayName: NAME, avatar }} />);
    await userEvent.tab();
    expect(document.activeElement).toBe(screen.getByRole("link", { name: SKIP }));
  });

  it("ativar o link leva o foco ao main", async () => {
    render(<Page />);
    await userEvent.tab();
    await userEvent.keyboard("{Enter}");
    expect(document.activeElement).toBe(screen.getByRole("main"));
  });

  it("fica visivel ao receber foco (CSS de :focus)", () => {
    const css = styles["./SkipLink.css"];
    expect(css).toBeDefined();
    expect(css).toMatch(/\.skip-link:focus/);
  });

  it("passa no axe", async () => {
    const { container } = render(<Page />);
    expect(screen.getByRole("link", { name: SKIP })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("AppHeader (A11Y-30)", () => {
  it("visitante: landmark banner com wordmark, Entrar e Criar conta, sem menu da conta", () => {
    render(<AppHeader onSignOut={() => {}} />);
    const header = screen.getByRole("banner");
    expect(within(header).getByText("Deckloom")).toBeInTheDocument();
    expect(within(header).getByRole("link", { name: "Entrar" })).toHaveAttribute("href");
    expect(within(header).getByRole("link", { name: "Criar conta" })).toHaveAttribute("href");
    expect(screen.queryByRole("button", { name: /menu da conta/i })).not.toBeInTheDocument();
  });

  it("Jogador: wordmark e botao do menu, sem Entrar/Criar conta", () => {
    render(<AppHeader player={{ displayName: NAME, avatar }} onSignOut={() => {}} />);
    const header = screen.getByRole("banner");
    expect(within(header).getByText("Deckloom")).toBeInTheDocument();
    expect(within(header).getByRole("button", { name: /menu da conta/i })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Entrar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Criar conta" })).not.toBeInTheDocument();
  });

  it("avatar do header atualiza quando a prop muda, sem recarregar (UX-07)", () => {
    const { rerender } = render(
      <AppHeader player={{ displayName: NAME, avatar }} onSignOut={() => {}} />,
    );
    const novo = { src: "/avatars/preset-2.png", label: "Lobo" };
    rerender(<AppHeader player={{ displayName: NAME, avatar: novo }} onSignOut={() => {}} />);
    expect(document.querySelector("header img")).toHaveAttribute("src", novo.src);
  });

  it("oculta o nome abaixo de 480px (CSS)", () => {
    const css = styles["./AppHeader.css"];
    expect(css).toBeDefined();
    expect(css).toMatch(/max-width:\s*(479(\.98)?|480)px/);
  });

  // T-171 / U-09: a 320px com texto a 200% o cabecalho reflui em vez de estourar a largura.
  // jsdom nao calcula layout; a medida real fica no E2E (account-deletion.spec.ts).
  it("o cabecalho e as acoes permitem quebra de linha (CSS, U-09)", () => {
    const css = styles["./AppHeader.css"];
    const bodyOf = (selector: string) =>
      [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
        .filter((m) => m[1].trim() === selector)
        .map((m) => m[2])
        .join("\n");
    expect(bodyOf(".app-header")).toMatch(/flex-wrap:\s*wrap\s*;/);
    expect(bodyOf(".app-header__actions")).toMatch(/flex-wrap:\s*wrap\s*;/);
  });

  // U-09: o painel do menu tem min-width em rem, que cresce com o texto; a 320px/200% (14rem = 448px) passava da largura
  // da tela e cortava o painel a esquerda. min-width nunca pode ser maior que o max-width do proprio painel.
  // U-14 (T-175): a folga e de uma margem de cada lado (2 * --space-4), para o painel nao encostar na borda da tela.
  it("o painel do menu limita o min-width a largura da tela (CSS, U-09)", () => {
    const css = styles["./AccountMenu.css"];
    const body = [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
      .filter((m) => m[1].trim() === ".account-menu__panel")
      .map((m) => m[2])
      .join("\n");
    expect(body).toMatch(
      /min-width:\s*min\(\s*14rem\s*,\s*calc\(100vw\s*-\s*2\s*\*\s*var\(--space-4\)\)\s*\)\s*;/,
    );
  });

  it("passa no axe (visitante e Jogador)", async () => {
    const visitor = render(<AppHeader onSignOut={() => {}} />);
    expect(screen.getByRole("banner")).toBeInTheDocument();
    await expectNoA11yViolations(visitor.container);
    visitor.unmount();
    const player = render(
      <AppHeader player={{ displayName: NAME, avatar }} onSignOut={() => {}} />,
    );
    expect(screen.getByRole("button", { name: /menu da conta/i })).toBeInTheDocument();
    await expectNoA11yViolations(player.container);
  });
});

describe("AccountMenu (A11Y-33)", () => {
  function setup(onSignOut = vi.fn()) {
    const utils = render(
      <div>
        <AccountMenu displayName={NAME} avatar={avatar} onSignOut={onSignOut} />
        <button type="button">Fora</button>
      </div>,
    );
    const trigger = screen.getByRole("button", { name: `Menu da conta: ${NAME}` });
    return { ...utils, trigger, onSignOut };
  }

  it("botao com nome completo no aria-label, aria-expanded=false, sem role=menu", () => {
    const { trigger } = setup();
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(trigger).not.toHaveAttribute("aria-haspopup", "menu");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Meu perfil" })).not.toBeInTheDocument();
  });

  it("abre com Meu perfil e Sair, aria-expanded=true e foco no primeiro item", async () => {
    const { trigger } = setup();
    await userEvent.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    const perfil = screen.getByRole("link", { name: "Meu perfil" });
    expect(screen.getByRole("button", { name: "Sair" })).toBeInTheDocument();
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(document.activeElement).toBe(perfil);
  });

  it("mostra o nome completo no painel", async () => {
    const { trigger } = setup();
    await userEvent.click(trigger);
    const panel = screen
      .getByRole("link", { name: "Meu perfil" })
      .closest("[data-account-menu-panel]");
    expect(panel).not.toBeNull();
    expect(within(panel as HTMLElement).getByText(NAME)).toBeInTheDocument();
  });

  it("Escape fecha e devolve o foco ao botao", async () => {
    const { trigger } = setup();
    await userEvent.click(trigger);
    await userEvent.keyboard("{Escape}");
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("link", { name: "Meu perfil" })).not.toBeInTheDocument();
    expect(document.activeElement).toBe(trigger);
  });

  it("clique fora fecha", async () => {
    const { trigger } = setup();
    await userEvent.click(trigger);
    await userEvent.click(screen.getByRole("button", { name: "Fora" }));
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("button", { name: "Sair" })).not.toBeInTheDocument();
  });

  it("Sair dispara a acao e fecha o menu", async () => {
    const { trigger, onSignOut } = setup();
    await userEvent.click(trigger);
    await userEvent.click(screen.getByRole("button", { name: "Sair" }));
    expect(onSignOut).toHaveBeenCalledTimes(1);
    expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  it("CSS: borda 1px, sombra no painel, MOT-11 scale 0.97 e movimento reduzido", () => {
    const css = styles["./AccountMenu.css"];
    expect(css).toBeDefined();
    expect(css).toMatch(/border:\s*1px/);
    expect(css).toMatch(/box-shadow/);
    expect(css).toMatch(/scale\(0\.97\)/);
    expect(css).toMatch(/prefers-reduced-motion/);
  });

  it("passa no axe fechado e aberto", async () => {
    const { container, trigger } = setup();
    expect(trigger).toBeInTheDocument();
    await expectNoA11yViolations(container);
    await userEvent.click(trigger);
    expect(screen.getByRole("link", { name: "Meu perfil" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});
