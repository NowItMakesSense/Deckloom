import { act, fireEvent, render as rtlRender, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

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
  it("visitante: landmark banner com a logo, Entrar e Criar conta, sem menu da conta", () => {
    render(<AppHeader onSignOut={() => {}} />);
    const header = screen.getByRole("banner");
    expect(within(header).getByRole("link", { name: "Deckloom" })).toBeInTheDocument();
    expect(within(header).getByRole("link", { name: "Entrar" })).toHaveAttribute("href");
    expect(within(header).getByRole("link", { name: "Criar conta" })).toHaveAttribute("href");
    expect(screen.queryByRole("button", { name: /menu da conta/i })).not.toBeInTheDocument();
  });

  it("Jogador: logo e botao do menu, sem Entrar/Criar conta", () => {
    render(<AppHeader player={{ displayName: NAME, avatar }} onSignOut={() => {}} />);
    const header = screen.getByRole("banner");
    expect(within(header).getByRole("link", { name: "Deckloom" })).toBeInTheDocument();
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
    expect(document.querySelector("header .account-menu img")).toHaveAttribute("src", novo.src);
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

// T-L01C tentativa 2 / REQ-L03 v5: o cabecalho das telas da conta e a mesma navegacao "dynamic island" da landing
// (design-system 1.13): pilula centralizada em vidro, logo propria, acoes com icone e troca de tema.
describe("AppHeader: pilula da navegacao (REQ-L03 v5)", () => {
  const LOGO_LIGHT = "/brand/deckloom/deckloom-logo.svg";
  const LOGO_DARK = "/brand/deckloom/deckloom-logo-on-dark.svg";
  const SYMBOL = "/brand/deckloom/deckloom-symbol.svg";

  it("a logo do Deckloom (simbolo + nome, arquivo local) leva ao inicio", () => {
    render(<AppHeader onSignOut={() => {}} />);
    const header = screen.getByRole("banner");
    const logo = within(header).getByRole("img", { name: "Deckloom" });
    expect(logo.getAttribute("src")).toBe(LOGO_LIGHT);
    expect(within(header).getByRole("link", { name: "Deckloom" })).toHaveAttribute("href", "/");
  });

  it("usa a logo para fundo escuro no tema escuro e so o simbolo no celular (picture)", () => {
    const { container } = render(<AppHeader theme="dark" onSignOut={() => {}} />);
    expect(screen.getByRole("img", { name: "Deckloom" }).getAttribute("src")).toBe(LOGO_DARK);
    const source = container.querySelector("picture source") as HTMLSourceElement;
    expect(source.getAttribute("srcset")).toBe(SYMBOL);
    expect(source.getAttribute("media")).toMatch(/max-width/);
  });

  it("visitante: Entrar e Criar conta tem icone decorativo", () => {
    render(<AppHeader onSignOut={() => {}} />);
    for (const name of ["Entrar", "Criar conta"]) {
      const link = screen.getByRole("link", { name });
      const icon = link.querySelector("svg");
      expect(icon, name).not.toBeNull();
      expect(icon!.closest("[aria-hidden='true']"), name).not.toBeNull();
    }
  });

  it("Criar conta e o botao primario e Entrar o secundario (classes de acao)", () => {
    render(<AppHeader onSignOut={() => {}} />);
    expect(screen.getByRole("link", { name: "Criar conta" })).toHaveClass("app-header__cta");
    expect(screen.getByRole("link", { name: "Entrar" })).toHaveClass("app-header__link");
  });

  it("troca de tema: um so botao com o nome da acao, que chama onToggleTheme", async () => {
    const onToggleTheme = vi.fn();
    render(<AppHeader theme="light" onToggleTheme={onToggleTheme} onSignOut={() => {}} />);
    const buttons = screen.getAllByRole("button", { name: /tema/i });
    expect(buttons).toHaveLength(1);
    expect(buttons[0]).toHaveAccessibleName("Usar tema escuro");
    await userEvent.click(buttons[0]);
    expect(onToggleTheme).toHaveBeenCalledTimes(1);
  });

  it("no tema escuro o botao oferece o tema claro", () => {
    render(<AppHeader theme="dark" onToggleTheme={() => {}} onSignOut={() => {}} />);
    expect(screen.getByRole("button", { name: /tema/i })).toHaveAccessibleName("Usar tema claro");
  });

  it("Jogador: o menu da conta convive com a troca de tema; carregando, so a troca de tema", () => {
    const player = render(
      <AppHeader
        player={{ displayName: NAME, avatar }}
        theme="light"
        onToggleTheme={() => {}}
        onSignOut={() => {}}
      />,
    );
    expect(screen.getByRole("button", { name: /menu da conta/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /tema/i })).toBeInTheDocument();
    player.unmount();
    render(<AppHeader loading theme="light" onToggleTheme={() => {}} onSignOut={() => {}} />);
    expect(screen.getByRole("button", { name: /tema/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /menu da conta/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Entrar" })).not.toBeInTheDocument();
  });

  it("passa no axe com a troca de tema (visitante e Jogador)", async () => {
    const visitor = render(<AppHeader theme="dark" onToggleTheme={() => {}} />);
    await expectNoA11yViolations(visitor.container);
    visitor.unmount();
    const player = render(
      <AppHeader
        player={{ displayName: NAME, avatar }}
        theme="light"
        onToggleTheme={() => {}}
        onSignOut={() => {}}
      />,
    );
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

// T-L01C tentativa 4 (decisao do usuario): no celular o cabecalho da conta fica numa linha so, no padrao da LandingNav.
// O visitante ve a logo, Criar conta, o tema e um botao de menu (aria-expanded, aria-controls) que guarda o Entrar; o
// menu fecha com Esc (foco volta ao botao), ao escolher o link e ao tocar fora. No desktop o Entrar segue a vista.
describe("AppHeader: menu do visitante no celular (padrao da LandingNav)", () => {
  // Escondido no desktop (CSS), o jsdom nao calcula o nome do botao: ele e buscado pelo vinculo com o menu.
  function menuToggle(): HTMLButtonElement | null {
    return document.querySelector<HTMLButtonElement>("header.app-header button[aria-controls]");
  }

  function renderVisitor() {
    return render(
      <div>
        <AppHeader theme="light" onToggleTheme={() => {}} onSignOut={() => {}} />
        <button type="button">Fora</button>
      </div>,
    );
  }

  it("o botao de menu comeca recolhido, chama-se Menu e controla o menu que guarda o Entrar", () => {
    const { container } = renderVisitor();
    const toggle = menuToggle();
    expect(toggle).not.toBeNull();
    expect(toggle).toHaveAttribute("aria-label", "Menu");
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const menu = container.querySelector(`#${CSS.escape(toggle!.getAttribute("aria-controls")!)}`);
    expect(menu).not.toBeNull();
    expect(menu).toHaveAttribute("data-open", "false");
    expect(within(menu as HTMLElement).getByRole("link", { name: "Entrar" })).toHaveAttribute(
      "href",
      "/entrar",
    );
    // Criar conta fica fora do menu (sempre a vista, como na landing).
    expect((menu as HTMLElement).contains(screen.getByRole("link", { name: "Criar conta" }))).toBe(
      false,
    );
  });

  it("alterna aria-expanded e data-open, com o icone de fechar quando aberto", () => {
    const { container } = renderVisitor();
    const toggle = menuToggle()!;
    const menu = container.querySelector(`#${CSS.escape(toggle.getAttribute("aria-controls")!)}`)!;
    const iconBefore = toggle.innerHTML;
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(menu).toHaveAttribute("data-open", "true");
    expect(toggle).toHaveAttribute("aria-label", "Menu");
    expect(toggle.innerHTML).not.toBe(iconBefore);
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(menu).toHaveAttribute("data-open", "false");
  });

  it("Esc fecha o menu e devolve o foco ao botao; com o menu fechado nao faz nada", () => {
    renderVisitor();
    const toggle = menuToggle()!;
    const focus = vi.spyOn(toggle, "focus");
    fireEvent.keyDown(document.body, { key: "Escape" });
    expect(focus).not.toHaveBeenCalled();
    fireEvent.click(toggle);
    fireEvent.keyDown(screen.getByRole("link", { name: "Entrar" }), { key: "Escape" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(focus).toHaveBeenCalledTimes(1);
  });

  it("fecha ao escolher o Entrar e ao tocar fora do cabecalho, mas nao ao tocar dentro dele", () => {
    renderVisitor();
    const toggle = menuToggle()!;
    fireEvent.click(toggle);
    fireEvent.click(screen.getByRole("link", { name: "Entrar" }));
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(toggle);
    fireEvent.pointerDown(screen.getByRole("link", { name: "Criar conta" }));
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    fireEvent.pointerDown(screen.getByRole("button", { name: "Fora" }));
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });

  it("ordem no documento: logo, Entrar, Criar conta, tema e por ultimo o botao de menu", () => {
    renderVisitor();
    const header = screen.getByRole("banner");
    const order = [...header.querySelectorAll("a, button")].map(
      (el) =>
        el.getAttribute("aria-label") ??
        (el.textContent?.trim() || el.querySelector("img")?.getAttribute("alt")),
    );
    expect(order).toEqual(["Deckloom", "Entrar", "Criar conta", "Usar tema escuro", "Menu"]);
  });

  it("Jogador e sessao carregando: sem o botao de menu do visitante", () => {
    const player = render(
      <AppHeader
        player={{ displayName: NAME, avatar }}
        theme="light"
        onToggleTheme={() => {}}
        onSignOut={() => {}}
      />,
    );
    expect(menuToggle()).toBeNull();
    player.unmount();
    render(<AppHeader loading theme="light" onToggleTheme={() => {}} onSignOut={() => {}} />);
    expect(menuToggle()).toBeNull();
  });

  it("passa no axe com o menu aberto", async () => {
    const { container } = renderVisitor();
    fireEvent.click(menuToggle()!);
    await expectNoA11yViolations(container);
  });
});

// T-L01C tentativa 4: a pilula fica fixa no topo; a altura medida vira --app-header-block-size no documento, usada no
// scroll-padding para um campo focado nunca ficar sob a pilula (inclusive com texto a 200%, quando ela cresce).
describe("AppHeader: altura publicada para o scroll-padding", () => {
  const VAR = "--app-header-block-size";
  let observed: ResizeObserverCallback | null = null;
  const OriginalResizeObserver = globalThis.ResizeObserver;

  beforeEach(() => {
    observed = null;
    globalThis.ResizeObserver = class {
      constructor(callback: ResizeObserverCallback) {
        observed = callback;
      }
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver;
  });

  afterEach(() => {
    globalThis.ResizeObserver = OriginalResizeObserver;
    document.documentElement.style.removeProperty(VAR);
  });

  it("publica a altura da pilula ao medir e remove ao desmontar", () => {
    const { unmount } = render(<AppHeader onSignOut={() => {}} />);
    const header = screen.getByRole("banner");
    vi.spyOn(header, "getBoundingClientRect").mockReturnValue({ height: 132 } as DOMRect);
    act(() => observed?.([], {} as ResizeObserver));
    expect(document.documentElement.style.getPropertyValue(VAR)).toBe("132px");
    unmount();
    expect(document.documentElement.style.getPropertyValue(VAR)).toBe("");
  });
});

// T-L01C tentativa 4: o Chromium nao rola no foco um campo que esta so em parte sob a pilula fixa (o scroll-padding
// so vale quando o campo esta fora da janela). Ao receber foco sob a pilula, o campo e trazido com
// scrollIntoView({ block: "nearest" }), que respeita o scroll-padding. Dentro da pilula e de dialogos, nada muda.
describe("AppHeader: campo focado nunca fica sob a pilula fixa", () => {
  function setup() {
    render(
      <div>
        <AppHeader onSignOut={() => {}} />
        <main>
          <input aria-label="Campo" />
          <div role="dialog" aria-label="Dialogo">
            <button type="button">No dialogo</button>
          </div>
        </main>
      </div>,
    );
    const header = screen.getByRole("banner");
    vi.spyOn(header, "getBoundingClientRect").mockReturnValue({ top: 12, bottom: 82 } as DOMRect);
    return header;
  }

  function placeAt(el: HTMLElement, top: number) {
    vi.spyOn(el, "getBoundingClientRect").mockReturnValue({ top, bottom: top + 44 } as DOMRect);
    const scroll = vi.fn();
    el.scrollIntoView = scroll;
    return scroll;
  }

  it("campo focado com o topo sob a pilula e trazido para baixo dela", () => {
    setup();
    const field = screen.getByRole("textbox", { name: "Campo" });
    const scroll = placeAt(field, 72);
    fireEvent.focusIn(field);
    expect(scroll).toHaveBeenCalledWith({ block: "nearest" });
  });

  it("campo focado abaixo da pilula nao rola", () => {
    setup();
    const field = screen.getByRole("textbox", { name: "Campo" });
    const scroll = placeAt(field, 120);
    fireEvent.focusIn(field);
    expect(scroll).not.toHaveBeenCalled();
  });

  it("foco dentro da pilula ou de um dialogo nao rola a pagina", () => {
    setup();
    const inDialog = screen.getByRole("button", { name: "No dialogo" });
    const dialogScroll = placeAt(inDialog, 20);
    fireEvent.focusIn(inDialog);
    const inHeader = screen.getByRole("link", { name: "Criar conta" });
    const headerScroll = placeAt(inHeader, 20);
    fireEvent.focusIn(inHeader);
    expect(dialogScroll).not.toHaveBeenCalled();
    expect(headerScroll).not.toHaveBeenCalled();
  });
});
