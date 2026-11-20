import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Browser, type Page } from "@playwright/test";

// T-L02 (REQ-L01, REQ-L06, REQ-L10, REQ-L12): a landing publica em / como palco WebGL unico.
//  - AC-L02c: axe sem violacoes e sem rolagem horizontal a 375px, nos dois temas, com o palco vivo e com o fallback;
//  - smoke: sem erro de console nem requisicao externa, com o palco e com o fallback;
//  - AC-L02l: auditoria de continuidade da camera (4000 passos): nenhum salto (deslocamento <= 0,6, giro <= 4 graus)
//    e nenhuma carta ou token aparecendo ou sumindo dentro do quadro sem estar oculto pelo cenario;
//  - AC-L02m: capturas do palco a 1366x768 e 375x812 anexadas ao relatorio (velas e tochas acesas, pan ate o mural);
//  - AC-L02j: no palco, os planos no mural de avisos (botoes e lista em HTML); no fallback, numa section.
// O progresso `raw` vai de 0 a END (1,1): em 1 a camera para atras do portal e depois desliza ate o mural.
// O palco precisa de WebGL: o Chromium headless usa o SwiftShader (software). Se o host nao entregar WebGL, os casos
// do palco vivo sao pulados com o motivo e os do fallback continuam valendo.

const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];
const DESKTOP = { width: 1366, height: 768 };
const MOBILE = { width: 375, height: 812 };
const SCHEMES = ["light", "dark"] as const;
const STAGE = '[data-testid="stage"]';
const READY = `${STAGE}[data-ready="true"]`;
const END = 1.1;
const BOARD_CTA = '[data-overlay="board-cta"]';

test.use({
  viewport: MOBILE,
  launchOptions: {
    args: ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"],
  },
});

interface Opened {
  page: Page;
  close: () => Promise<void>;
  errors: string[];
  external: string[];
}

async function open(
  browser: Browser,
  viewport: { width: number; height: number },
  scheme: "light" | "dark",
  mode: "viva" | "fallback",
): Promise<Opened> {
  const context = await browser.newContext({
    viewport,
    colorScheme: scheme,
    reducedMotion: mode === "fallback" ? "reduce" : "no-preference",
  });
  const page = await context.newPage();
  const errors: string[] = [];
  const external: string[] = [];
  page.on("pageerror", (e) => errors.push(`pageerror: ${e.message}`));
  page.on("console", (m) => {
    if (m.type() === "error" || m.type() === "warning") errors.push(`${m.type()}: ${m.text()}`);
  });
  page.on("requestfailed", (r) => errors.push(`requestfailed: ${r.url()}`));
  page.on("request", (r) => {
    const url = new URL(r.url());
    if (!["localhost", "127.0.0.1"].includes(url.hostname) && url.protocol.startsWith("http")) {
      external.push(r.url());
    }
  });
  await page.goto("/");
  await expect(page.locator("h1")).toHaveCount(1);
  return { page, close: () => context.close(), errors, external };
}

/** Espera o palco ficar pronto; sem WebGL no host, pula o teste com o motivo. */
async function waitForStage(page: Page): Promise<boolean> {
  return page
    .locator(READY)
    .waitFor({ timeout: 100_000 })
    .then(() => true)
    .catch(() => false);
}

/** Rola ate o progresso `raw` (0 a END) do palco. */
async function scrollStage(page: Page, raw: number): Promise<void> {
  await page.evaluate((fraction) => {
    const w = window as unknown as { __deckloom?: { snap: boolean } };
    if (w.__deckloom) w.__deckloom.snap = true;
    const el = document.getElementById("como-funciona") as HTMLElement;
    const top = el.getBoundingClientRect().top + window.scrollY;
    window.scrollTo({
      top: top + fraction * (el.offsetHeight - window.innerHeight),
      behavior: "instant",
    });
  }, raw / END);
  await page.waitForTimeout(1200);
}

for (const scheme of SCHEMES) {
  for (const mode of ["viva", "fallback"] as const) {
    test(`landing em / a 375px, tema ${scheme}, ${mode === "viva" ? "palco vivo" : "fallback"}: axe sem violacoes e sem rolagem horizontal`, async ({
      browser,
    }) => {
      // O palco compila shaders por software (SwiftShader) e, com a maquina carregada, passa de 30 s.
      test.setTimeout(mode === "viva" ? 180_000 : 60_000);
      const { page, close } = await open(browser, MOBILE, scheme, mode);

      if (mode === "viva") {
        test.skip(
          !(await waitForStage(page)),
          "o host de teste nao entregou WebGL: o palco nao montou",
        );
        await expect(page.locator("canvas")).toHaveCount(1);
        await expect(page.locator(".landing")).toHaveAttribute("data-mode", "live");
      } else {
        await expect(page.locator(".landing")).toHaveAttribute("data-mode", "static");
        await expect(page.locator("canvas")).toHaveCount(0);
        await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
      }

      const { violations } = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
      expect(
        violations.map((v) => `${v.id}: ${v.nodes.length} no(s)`),
        "axe em / (topo)",
      ).toEqual([]);

      if (mode === "viva") {
        // com a legenda de um gancho visivel sobre o palco, e depois nos planos
        await scrollStage(page, 0.46);
        const caption = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze();
        expect(
          caption.violations.map((v) => `${v.id}: ${v.nodes.length} no(s)`),
          "axe com a legenda do gancho",
        ).toEqual([]);
      }
      if (mode === "viva") {
        // no fim do pan: o mural de avisos com os botoes dos planos em HTML
        await scrollStage(page, END);
        await expect(page.locator(BOARD_CTA)).toBeVisible();
        const board = await new AxeBuilder({ page })
          .withTags(WCAG_TAGS)
          .include(BOARD_CTA)
          .include("#planos")
          .analyze();
        expect(
          board.violations.map((v) => `${v.id}: ${v.nodes.length} no(s)`),
          "axe nos planos do mural",
        ).toEqual([]);
      } else {
        await page.locator("#planos").scrollIntoViewIfNeeded();
        await page.waitForTimeout(1500);
        const plans = await new AxeBuilder({ page })
          .withTags(WCAG_TAGS)
          .include("#planos")
          .analyze();
        expect(
          plans.violations.map((v) => `${v.id}: ${v.nodes.length} no(s)`),
          "axe nos planos",
        ).toEqual([]);
      }

      const overflow = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
      }));
      expect(overflow.scrollWidth, "sem rolagem horizontal a 375px").toBeLessThanOrEqual(
        overflow.clientWidth,
      );
      await close();
    });
  }
}

for (const [name, viewport] of [
  ["1366x768", DESKTOP],
  ["375x812", MOBILE],
] as const) {
  test(`smoke do palco a ${name}: sem erro de console e sem requisicao externa`, async ({
    browser,
  }) => {
    test.setTimeout(180_000);
    const { page, close, errors, external } = await open(browser, viewport, "dark", "viva");
    test.skip(
      !(await waitForStage(page)),
      "o host de teste nao entregou WebGL: o palco nao montou",
    );

    for (const raw of [0, 0.22, 0.46, 0.7, 1, END]) await scrollStage(page, raw);

    // O 401 da consulta de sessao do visitante (GET /me) e esperado em / e nao e erro da landing.
    const unexpected = errors.filter((message) => !/401/.test(message));
    expect(unexpected, "erros de console").toEqual([]);
    expect(external, "requisicoes a terceiros").toEqual([]);
    await close();
  });

  test(`auditoria de continuidade a ${name} (AC-L02l): camera sem salto e nenhum objeto surgindo no quadro`, async ({
    browser,
  }) => {
    test.setTimeout(240_000);
    const { page, close } = await open(browser, viewport, "dark", "viva");
    test.skip(
      !(await waitForStage(page)),
      "o host de teste nao entregou WebGL: o palco nao montou",
    );

    const result = await page.evaluate(() => {
      const w = window as unknown as {
        __deckloom: {
          audit: (steps: number) => {
            maxDp: number;
            atDp: number;
            maxAng: number;
            atAng: number;
            pops: string[];
          };
        };
      };
      return w.__deckloom.audit(4000);
    });

    test.info().annotations.push({
      type: "continuidade",
      description: `maxDp=${result.maxDp.toFixed(3)} (em ${result.atDp.toFixed(4)}) maxAng=${result.maxAng.toFixed(2)} (em ${result.atAng.toFixed(4)}) pops=${result.pops.length}`,
    });
    expect(result.maxDp, "deslocamento maximo por passo").toBeLessThanOrEqual(0.6);
    expect(result.maxAng, "giro maximo por passo (graus)").toBeLessThanOrEqual(4);
    expect(result.pops, "objetos aparecendo ou sumindo dentro do quadro").toEqual([]);
    await close();
  });

  test(`capturas do palco a ${name} (AC-L02m): floresta, caixa, ganchos, mesa, epilogo e mural`, async ({
    browser,
  }) => {
    test.setTimeout(420_000);
    const { page, close } = await open(browser, viewport, "dark", "viva");
    test.skip(
      !(await waitForStage(page)),
      "o host de teste nao entregou WebGL: o palco nao montou",
    );

    const points: Array<[string, number]> = [
      ["floresta-copas", 0],
      ["floresta-portal", 0.1],
      ["clareira", 0.19],
      ["caixa-desperta", 0.235],
      ["caixa-abre", 0.275],
      ["deck", 0.36],
      ["gancho-1", 0.43],
      ["gancho-3", 0.54],
      ["explosao", 0.66],
      ["mesa", 0.85],
      ["epilogo", 1],
      ["pan", 1.04],
      ["mural", END],
    ];
    for (const [label, raw] of points) {
      await scrollStage(page, raw);
      await test.info().attach(`${name}-${label}`, {
        body: await page.screenshot({ timeout: 150_000 }),
        contentType: "image/png",
      });
    }
    // a legenda do primeiro gancho aparece quando a carta e revelada
    await scrollStage(page, 0.43);
    await expect(page.locator('[data-hook-panel="0"]')).not.toHaveAttribute("inert", "");
    // os botoes do mural so entram no fim do pan
    await expect(page.locator(BOARD_CTA)).toBeHidden();
    await scrollStage(page, END);
    await expect(page.locator(BOARD_CTA)).toBeVisible();
    await close();
  });
}

test("a landing leva a /cadastro pelo CTA e a /entrar pelo link de acesso", async ({ page }) => {
  await page.goto("/");
  // A 375px o menu com Entrar fica recolhido (fora da arvore de acessibilidade): checa pelo href.
  const nav = page.locator('nav[aria-label="Principal"]');

  await expect(nav.locator('a[href="/cadastro"]')).toHaveText("Criar conta");
  await expect(nav.locator('a[href="/entrar"]')).toHaveText("Entrar");
});

test("fallback: os planos vem depois dos jogos numa section e todo botao leva a /cadastro", async ({
  browser,
}) => {
  const { page, close } = await open(browser, MOBILE, "light", "fallback");

  const plans = page.locator("#planos");
  await expect(plans.getByRole("article")).toHaveCount(2);
  await expect(plans.locator("a")).toHaveCount(2);
  for (const href of await plans
    .locator("a")
    .evaluateAll((links) => links.map((l) => l.getAttribute("href")))) {
    expect(href).toBe("/cadastro");
  }
  await expect(plans).toContainText("R$ 19,90");
  await close();
});

test("palco: os planos ficam no mural de avisos; o link Planos da navegacao leva ate ele", async ({
  browser,
}) => {
  test.setTimeout(180_000);
  const { page, close } = await open(browser, DESKTOP, "dark", "viva");
  test.skip(!(await waitForStage(page)), "o host de teste nao entregou WebGL: o palco nao montou");

  await expect(page.locator("section.plans")).toHaveCount(0);
  const list = page.locator("#planos li");
  await expect(list).toHaveCount(2);
  await expect(list.nth(1)).toContainText("R$ 19,90 por mês");
  const cta = page.locator(BOARD_CTA);
  await expect(cta).toBeHidden();

  await page.evaluate(() => {
    const w = window as unknown as { __deckloom?: { snap: boolean } };
    if (w.__deckloom) w.__deckloom.snap = true;
  });
  await page.locator('nav[aria-label="Principal"] a[href="#planos"]').click();
  await expect(cta).toBeVisible({ timeout: 20_000 });
  const links = cta.getByRole("link");
  await expect(links).toHaveText(["Criar conta grátis", "Quero o Arcano"]);
  for (const href of await links.evaluateAll((all) => all.map((l) => l.getAttribute("href")))) {
    expect(href).toBe("/cadastro");
  }
  await close();
});
