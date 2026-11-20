// T-L02 / AC-L02a, AC-L02b, AC-L02f, AC-L02g (REQ-L01, REQ-L02, REQ-L03, REQ-L06, REQ-L11): a landing publica.
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { setThemePreference } from "@/app/theme/theme";
import { expectNoA11yViolations } from "@/shared/testing/expectNoA11yViolations";

import { LandingPage } from "./LandingPage";
import type { StageCapabilities } from "./stage/capabilities";

// O palco real precisa de WebGL: aqui o chunk do three e trocado por um falso que so conta quem o carregou.
const stage = vi.hoisted(() => ({
  chunkLoaded: 0,
  createStage: vi.fn(),
  dispose: vi.fn(),
  loadAssets: vi.fn(),
}));
vi.mock("./stage/engine/createStage", () => {
  stage.chunkLoaded++;
  return { createStage: stage.createStage };
});
vi.mock("./stage/loadAssets", () => ({ loadStageAssets: stage.loadAssets }));

const STATIC_ONLY: StageCapabilities = { webgl: false, reducedMotion: false };
const LIVE: StageCapabilities = { webgl: true, reducedMotion: false };

beforeEach(() => {
  stage.chunkLoaded = 0;
  stage.createStage.mockReset().mockReturnValue({ dispose: stage.dispose });
  stage.dispose.mockReset();
  stage.loadAssets.mockReset().mockResolvedValue({});
});

function renderLanding(capabilities: StageCapabilities = STATIC_ONLY) {
  return render(
    <MemoryRouter>
      <LandingPage capabilities={capabilities} />
    </MemoryRouter>,
  );
}

afterEach(() => act(() => setThemePreference("system")));

const GAMES = [
  "Magic: The Gathering",
  "Pokémon TCG",
  "Yu-Gi-Oh!",
  "One Piece Card Game",
  "Disney Lorcana",
];

describe("landing em /: mensagem, jogos e acesso (AC-L02a)", () => {
  it("tem um unico h1 com a mensagem principal: montar decks com as cartas que ja tem", () => {
    renderLanding();

    const headings = screen.getAllByRole("heading", { level: 1 });
    expect(headings).toHaveLength(1);
    expect(headings[0]).toHaveTextContent("Monte decks com as cartas que você já tem");
  });

  it("nomeia os 5 TCGs do MVP juntos, sem dizer que um vem antes dos outros", () => {
    renderLanding();

    const hero = document.getElementById("hero") as HTMLElement;
    for (const name of [
      "Magic: The Gathering",
      "Pokémon TCG",
      "Yu-Gi-Oh!",
      "One Piece",
      "Lorcana",
    ]) {
      expect(hero.textContent).toContain(name);
    }
    expect(document.body.textContent).not.toMatch(/primeiro|em seguida|depois chega|chega antes/i);
  });

  it("o CTA leva a /cadastro e o link de acesso leva a /entrar", () => {
    renderLanding();

    const nav = screen.getByRole("navigation", { name: "Principal" });
    expect(within(nav).getByRole("link", { name: "Criar conta" })).toHaveAttribute(
      "href",
      "/cadastro",
    );
    expect(within(nav).getByRole("link", { name: "Entrar" })).toHaveAttribute("href", "/entrar");
  });
});

describe("landing: estrutura do fallback e dos ganchos (AC-L02f)", () => {
  it("a ordem no fallback: abertura, os quatro ganchos em lista, os cinco jogos em grade e os planos", () => {
    const { container } = renderLanding();

    const ids = [...container.querySelectorAll("main > *, main > div > section")].map(
      (el) => el.id,
    );
    expect(ids).toEqual([
      "hero",
      "como-funciona",
      "quanto-do-deck",
      "meta",
      "lojas",
      "ia",
      "jogos",
      "planos",
    ]);
  });

  it("os quatro ganchos sao cards, na ordem do REQ-L01", () => {
    renderLanding();

    const cards = [...document.querySelectorAll<HTMLElement>("article[data-hook]")];
    expect(
      cards.map((card) => within(card).getByRole("heading", { level: 2 }).textContent),
    ).toEqual([
      "Quanto do deck você já tem",
      "O meta dos torneios contra a sua coleção",
      "Compre só o que falta",
      "IA que explica o porquê de cada carta",
    ]);
  });

  it("os exemplos de Magic mostram os simbolos de mana (locais e decorativos) com nome acessivel", () => {
    const { container } = renderLanding();

    const symbols = container.querySelectorAll(".ms.ms-cost");
    expect(symbols.length).toBeGreaterThan(4);
    expect(screen.getAllByRole("img", { name: /Custo de mana/ }).length).toBeGreaterThan(1);
  });

  it("o conteudo dos cards e texto no HTML: legivel sem a cena (fallback estatico, sem canvas)", () => {
    const { container } = renderLanding();

    expect(container.querySelector("canvas")).toBeNull();
    expect(screen.getByText(/Você já tem/)).toBeInTheDocument();
    expect(screen.getByText("Alerta de preço:")).toBeInTheDocument();
    expect(
      screen.getByText("Taverna das Cartas", { selector: ".shops-example__name" }),
    ).toBeInTheDocument();
  });

  it("um 'Tenho +1' atualiza o numero do deck e a cotacao das lojas na mesma pagina", () => {
    renderLanding();
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("data-have", "47");

    fireEvent.click(screen.getAllByRole("button", { name: /Tenho \+1/ })[0]);

    expect(status).toHaveAttribute("data-have", "48");
    expect(status).toHaveAttribute("data-missing", "12");
    expect(screen.getAllByText("faltam 12 cartas")).toHaveLength(3);
  });
});

describe("landing: entrada por secao", () => {
  it("os cards e os jogos marcam as partes que entram com anime.js; a abertura nunca e escondida (titulo no primeiro quadro)", () => {
    renderLanding();

    const count = (id: string) =>
      document.getElementById(id)?.querySelectorAll("[data-reveal-item]").length ?? 0;
    expect(count("hero")).toBe(0);
    for (const id of ["quanto-do-deck", "meta", "lojas", "ia"]) expect(count(id), id).toBe(4);
    expect(count("jogos")).toBe(7);
    // Sem script (aqui nao ha IntersectionObserver) nada fica escondido.
    for (const el of document.querySelectorAll<HTMLElement>("[data-reveal-item]")) {
      expect(el.style.opacity).toBe("");
    }
  });
});

describe("landing: fragmentos de interface do produto", () => {
  it("os quatro exemplos dos cards sao fragmentos de interface (data-ui), com dados do mock", () => {
    const { container } = renderLanding();

    const fragments = [...container.querySelectorAll("[data-ui]")].map(
      (el) => el.className.split(" ")[0],
    );
    expect(fragments).toEqual([
      "deck-example",
      "meta-example",
      "shops-example",
      "assistant-example",
    ]);
  });
});

describe("landing: recursos 'em breve' sem rota inexistente (AC-L02b)", () => {
  it.each(["Coleção", "Decks", "Sugestões com IA", "Meta", "Lojas"])(
    "%s aparece na carta com o selo 'Em breve'",
    (feature) => {
      renderLanding();

      const type = screen
        .getAllByText(feature)
        .map((el) => el.closest('[data-part="type"]'))
        .find((el): el is HTMLElement => el !== null);
      expect(type, feature).toBeDefined();
      const card = (type as HTMLElement).closest("article") as HTMLElement;
      expect(within(card).getByText("Em breve")).toBeInTheDocument();
    },
  );

  it("nenhum link da pagina leva a rota que nao existe: so /cadastro, /entrar, o inicio e ancoras com alvo", () => {
    const { container } = renderLanding();

    const hrefs = [...container.querySelectorAll("a")].map((a) => a.getAttribute("href") as string);
    for (const href of hrefs) {
      if (href.startsWith("#")) {
        expect(document.getElementById(href.slice(1)), href).not.toBeNull();
      } else {
        expect(["/", "/cadastro", "/entrar"], href).toContain(href);
      }
    }
  });
});

describe("landing: o que ela nao tem (AC-L02g)", () => {
  it("o hero nao tem cartao de criar conta: Criar conta so existe na navegacao", () => {
    renderLanding();

    const hero = document.getElementById("hero") as HTMLElement;
    expect(within(hero).queryByRole("link", { name: /criar conta/i })).toBeNull();
    expect(screen.getAllByRole("link", { name: /criar conta/i })).toHaveLength(1);
  });

  it("nao tem faixa final de CTA nem bloco de previa do app", () => {
    renderLanding();

    const footer = screen.getByRole("contentinfo");
    expect(within(footer).queryByRole("link", { name: /criar conta/i })).toBeNull();
    expect(document.body.textContent).not.toMatch(
      /pr[eé]via|pré-visualiza|comece agora|crie sua conta/i,
    );
  });

  it("o preco do produto aparece so na secao de planos (a cotacao de lojas e exemplo de carta)", () => {
    renderLanding();

    const plans = document.getElementById("planos") as HTMLElement;
    expect(plans).toHaveTextContent("R$ 19,90");
    const outside = [...document.querySelectorAll("main > *, main > div, footer")]
      .filter((el) => el.id !== "planos")
      .map((el) => el.textContent)
      .join(" ");
    expect(outside).not.toMatch(/gr[aá]tis|gratuito|por m[eê]s|Arcano/i);
  });
});

describe("landing: marca (AC-L02g)", () => {
  it("a secao de jogos mostra os cinco logos oficiais locais, cada um com o nome do jogo como alternativa", () => {
    renderLanding();

    const section = document.getElementById("jogos") as HTMLElement;
    const logos = within(section).getAllByRole("img");
    expect(logos.map((img) => img.getAttribute("alt"))).toEqual(GAMES);
    for (const logo of logos)
      expect(logo.getAttribute("src")).toMatch(/^\/brand\/games\/[a-z-]+\.(svg|png|webp)$/);
  });

  it("o rodape tem a logo do Deckloom para fundo escuro e a nota de marcas", () => {
    renderLanding();

    const footer = screen.getByRole("contentinfo");
    expect(within(footer).getByRole("img", { name: "Deckloom" }).getAttribute("src")).toBe(
      "/brand/deckloom/deckloom-logo-on-dark.svg",
    );
    expect(footer).toHaveTextContent(/são marcas de seus titulares/);
    expect(footer).toHaveTextContent(/não é afiliado nem endossado/);
    for (const game of GAMES) expect(footer.textContent).toContain(game);
  });

  it("a navegacao tem a logo do Deckloom", () => {
    renderLanding();

    const nav = screen.getByRole("navigation", { name: "Principal" });
    expect(within(nav).getByRole("img", { name: "Deckloom" })).toBeInTheDocument();
  });

  it("ha um unico controle de tema na pagina inteira", () => {
    renderLanding();

    expect(screen.getAllByRole("button", { name: /tema/i })).toHaveLength(1);
  });

  it("nenhuma URL externa: nem em atributos nem em texto", () => {
    const { container } = renderLanding();

    // xmlns="http://www.w3.org/2000/svg" e um identificador de namespace do SVG, nao uma requisicao.
    const html = container.innerHTML.replaceAll('xmlns="http://www.w3.org/2000/svg"', "");
    expect(html).not.toMatch(/https?:\/\/|\/\/[a-z0-9.-]+\.[a-z]{2,}\//i);
  });
});

describe("landing: acessibilidade", () => {
  it("axe sem violacoes no fallback", async () => {
    const { container } = renderLanding();

    await expectNoA11yViolations(container);
  }, 30_000); // axe sobre a pagina inteira em jsdom passa de 5s com a maquina carregada

  it("o conteudo principal e o alvo do link 'Pular para o conteudo'", () => {
    renderLanding();

    const main = screen.getByRole("main");
    expect(main).toHaveAttribute("id", "conteudo");
    expect(main).toHaveAttribute("tabindex", "-1");
  });
});

describe("landing: um so acesso por link", () => {
  it("Entrar e Criar conta aparecem uma vez cada (so na navegacao); os planos levam a /cadastro com outro texto", () => {
    renderLanding();

    expect(screen.getAllByRole("link", { name: "Entrar" })).toHaveLength(1);
    expect(screen.getAllByRole("link", { name: "Criar conta" })).toHaveLength(1);
  });
});

describe("landing: planos depois dos cinco jogos (AC-L02j)", () => {
  it("a section de planos vem depois da dos jogos", () => {
    renderLanding();

    const jogos = document.getElementById("jogos") as HTMLElement;
    const planos = document.getElementById("planos") as HTMLElement;
    expect(jogos.compareDocumentPosition(planos) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe("landing: o palco WebGL so entra com WebGL e sem prefers-reduced-motion (AC-L02d)", () => {
  it.each([
    ["sem WebGL", { webgl: false, reducedMotion: false }],
    ["com prefers-reduced-motion", { webgl: true, reducedMotion: true }],
    ["sem WebGL e com prefers-reduced-motion", { webgl: false, reducedMotion: true }],
  ])(
    "%s: o three nunca e carregado e aparecem a clareira, os ganchos em lista e os jogos em grade",
    async (_name, capabilities) => {
      const { container } = renderLanding(capabilities);
      await act(() => Promise.resolve());

      expect(stage.chunkLoaded).toBe(0);
      expect(container.querySelector("canvas")).toBeNull();
      expect(screen.getByTestId("static-clearing")).toBeInTheDocument();
      expect(document.querySelectorAll("article[data-hook]")).toHaveLength(4);
      expect(document.getElementById("jogos")).not.toBeNull();
      expect(screen.getByRole("heading", { level: 1 })).not.toHaveClass("landing-sr-only");
    },
  );

  it("com WebGL e sem reducao de movimento carrega o chunk do palco sob demanda e o monta uma vez", async () => {
    renderLanding(LIVE);
    expect(stage.chunkLoaded).toBe(0);

    await waitFor(() => expect(stage.createStage).toHaveBeenCalledTimes(1));

    expect(stage.chunkLoaded).toBe(1);
    const options = stage.createStage.mock.calls[0][0];
    expect(options.hooks.map((h: { title: string }) => h.title)).toEqual([
      "Quanto do deck você já tem",
      "O meta dos torneios contra a sua coleção",
      "Compre só o que falta",
      "IA que explica o porquê de cada carta",
    ]);
    expect(options.scroller).toBe(document.getElementById("como-funciona"));
    await waitFor(() => expect(screen.getByTestId("stage")).toHaveAttribute("data-ready", "true"));
  });

  it("na versao animada o titulo principal existe em HTML mas fica oculto, sem painel de titulo", async () => {
    renderLanding(LIVE);
    await waitFor(() => expect(stage.createStage).toHaveBeenCalled());

    const h1 = screen.getByRole("heading", { level: 1 });
    expect(h1).toHaveClass("landing-sr-only");
    expect(h1).toHaveTextContent("Monte decks com as cartas que você já tem");
    expect(document.querySelector(".landing-hero__panel")).toBeNull();
  });

  it("no palco os quatro ganchos sao cards em HTML, os cinco jogos tem os logos e os planos ficam no mural, no fim", async () => {
    renderLanding(LIVE);
    await waitFor(() => expect(stage.createStage).toHaveBeenCalled());

    const frame = screen.getByTestId("stage");
    expect(frame.querySelectorAll("article[data-hook]")).toHaveLength(4);
    expect(
      within(frame).getAllByRole("img", { name: /Magic|Pokémon|Yu-Gi-Oh|One Piece|Lorcana/ }),
    ).toHaveLength(5);
    // no palco os planos moram no mural de avisos: a section do fallback nao existe
    expect(document.querySelector("section.plans")).toBeNull();
    const stageSection = document.getElementById("como-funciona") as HTMLElement;
    const planos = document.getElementById("planos") as HTMLElement;
    expect(stageSection.contains(planos)).toBe(true);
    const games = within(frame).getByRole("heading", { name: "Os cinco jogos" });
    expect(games.compareDocumentPosition(planos) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("no palco a lista dos planos e HTML para leitor de tela e os botoes do mural levam a /cadastro (AC-L02j)", async () => {
    renderLanding(LIVE);
    await waitFor(() => expect(stage.createStage).toHaveBeenCalled());

    const planos = document.getElementById("planos") as HTMLElement;
    expect(within(planos).getByRole("heading", { level: 2 })).toHaveTextContent(/Planos/);
    const items = within(planos)
      .getAllByRole("listitem")
      .map((li) => li.textContent);
    expect(items).toHaveLength(2);
    expect(items[0]).toMatch(/Grátis.*R\$ 0.*até 5 decks/);
    expect(items[1]).toMatch(/Arcano.*R\$ 19,90 por mês/);
    expect(items[1]).toMatch(/sugestões com IA.*decks ilimitados.*alertas de preço.*sem anúncios/);

    const cta = document.querySelector('[data-overlay="board-cta"]') as HTMLElement;
    expect(cta).toHaveAttribute("role", "group");
    const label = document.getElementById(cta.getAttribute("aria-labelledby") as string);
    expect(label).toHaveTextContent(/Planos/);
    const links = within(cta).getAllByRole("link", { hidden: true });
    expect(links.map((a) => a.textContent)).toEqual(["Criar conta grátis", "Quero o Arcano"]);
    for (const a of links) expect(a).toHaveAttribute("href", "/cadastro");
    expect(cta).toHaveTextContent("A assinatura ainda não é cobrada");
    // ate o pan terminar os botoes ficam fora do foco
    expect(cta).toHaveAttribute("inert");
  });

  it("no palco o link 'Planos' da navegacao leva ao fim do palco (o mural)", async () => {
    renderLanding(LIVE);
    await waitFor(() => expect(stage.createStage).toHaveBeenCalled());

    const nav = screen.getByRole("navigation", { name: "Principal" });
    const link = within(nav).getAllByRole("link", { name: "Planos", hidden: true })[0];
    const target = document.getElementById(link.getAttribute("href")!.slice(1)) as HTMLElement;
    expect(target).toBe(document.getElementById("planos"));
    expect(target).toHaveClass("stage__plans");
  });

  it("no palco nenhum link leva a rota inexistente e as ancoras tem alvo", async () => {
    const { container } = renderLanding(LIVE);
    await waitFor(() => expect(stage.createStage).toHaveBeenCalled());

    for (const a of container.querySelectorAll("a")) {
      const href = a.getAttribute("href") as string;
      if (href.startsWith("#")) expect(document.getElementById(href.slice(1)), href).not.toBeNull();
      else expect(["/", "/cadastro", "/entrar"], href).toContain(href);
    }
  });

  it("axe sem violacoes com o palco (planos do mural incluidos), nos dois temas", async () => {
    for (const theme of ["light", "dark"] as const) {
      act(() => setThemePreference(theme));
      const { container, unmount } = renderLanding(LIVE);
      await waitFor(() => expect(stage.createStage).toHaveBeenCalled());
      await expectNoA11yViolations(container);
      unmount();
    }
  }, 60_000);

  it("as legendas invisiveis saem da ordem de foco (inert) logo no primeiro quadro", async () => {
    renderLanding(LIVE);
    await waitFor(() => expect(stage.createStage).toHaveBeenCalled());

    for (const panel of document.querySelectorAll("[data-hook-panel]")) {
      expect(panel.hasAttribute("inert")).toBe(true);
    }
  });

  it("ao desmontar libera o palco", async () => {
    const { unmount } = renderLanding(LIVE);
    await waitFor(() => expect(stage.createStage).toHaveBeenCalled());

    unmount();

    expect(stage.dispose).toHaveBeenCalledTimes(1);
  });

  it("se o palco falhar ao montar a pagina cai para o fallback estatico", async () => {
    stage.createStage.mockImplementation(() => {
      throw new Error("WebGL indisponivel");
    });
    const { container } = renderLanding(LIVE);

    await waitFor(() =>
      expect(container.querySelector(".landing")).toHaveAttribute("data-mode", "static"),
    );
    expect(screen.getByTestId("static-clearing")).toBeInTheDocument();
    expect(document.querySelectorAll("article[data-hook]")).toHaveLength(4);
    expect(screen.getByRole("heading", { level: 1 })).not.toHaveClass("landing-sr-only");
  });
});
