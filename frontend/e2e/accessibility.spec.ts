import { settledAxe } from "./support/axe";
import { expect, test } from "@playwright/test";
import type { Browser, BrowserContextOptions, Page } from "@playwright/test";

// T-127 (RED): varredura de acessibilidade, teclado, responsivo e direcao de arte nas telas S1 a S11 e nos dialogs
// S10 e S11 (A11Y-07, A11Y-10, A11Y-18, A11Y-19, A11Y-32, A11Y-37, A11Y-38; ART-02, ART-08; WC-14; UX-10).
//
// Fora do E2E (lista manual para o qa): leitor de tela (A11Y-15 a A11Y-17, A11Y-31, A11Y-34), legibilidade a 200%
// (A11Y-19, visual), ART-09 (`inspect.mjs`), no maximo 4 matizes por tela (ART-02, parte visual) e A11Y-39.

const API_ORIGIN = "http://127.0.0.1:5099";
const PASSWORD = "e2e-senha-segura-123";
const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];
const MIN_TARGET = 44;

type Fixtures = { email: string; displayName: string; resetLink: string; storageState: string };
type ScreenId = "S1" | "S2" | "S3" | "S4" | "S5" | "S6" | "S7" | "S8" | "S9" | "S10" | "S11";
type Screen = {
  id: ScreenId;
  player: boolean;
  dialog: boolean;
  open: (page: Page, fx: Fixtures) => Promise<void>;
};

let fixtures: Fixtures;

function uniqueSuffix() {
  return `${Date.now()}${Math.floor(Math.random() * 1000)}`;
}

async function setGoogleIdentity(page: Page, subject: string, email: string, name = "E2E Google") {
  const response = await page.request.post(`${API_ORIGIN}/__test/google/identity`, {
    data: { subject, email, name, emailVerified: true, outcome: "approve" },
  });
  expect(response.status(), "stub Google: POST /__test/google/identity").toBe(204);
}

async function readyHeading(page: Page) {
  await expect(page.locator("main h1")).toBeVisible();
}

const SCREENS: Screen[] = [
  {
    id: "S1",
    player: false,
    dialog: false,
    open: async (page) => {
      await page.goto("/cadastro");
      await readyHeading(page);
    },
  },
  {
    id: "S2",
    player: false,
    dialog: false,
    open: async (page) => {
      await page.goto("/entrar");
      await readyHeading(page);
    },
  },
  {
    id: "S3",
    player: false,
    dialog: false,
    open: async (page) => {
      // Estado "processando": a leitura da sessao nunca responde, entao a tela nao redireciona.
      await page.route("**/api/v1/auth/session", () => new Promise<void>(() => undefined));
      await page.goto("/entrar/google/retorno?resultado=ok");
      await expect(
        page.getByRole("heading", { name: "Concluindo o login com Google" }),
      ).toBeVisible();
    },
  },
  {
    id: "S4",
    player: false,
    dialog: false,
    open: async (page) => {
      const suffix = uniqueSuffix();
      await setGoogleIdentity(page, `e2e-sub-a11y4-${suffix}`, `e2e-a11y4-${suffix}@example.test`);
      await page.goto("/entrar");
      await page.getByRole("button", { name: "Continuar com Google" }).click();
      await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
      await readyHeading(page);
    },
  },
  {
    id: "S5",
    player: false,
    dialog: false,
    open: async (page, fx) => {
      await setGoogleIdentity(page, `e2e-sub-a11y5-${uniqueSuffix()}`, fx.email);
      await page.goto("/entrar");
      await page.getByRole("button", { name: "Continuar com Google" }).click();
      await expect(page).toHaveURL(/\/entrar\/google\/vincular$/);
      await readyHeading(page);
    },
  },
  {
    id: "S6",
    player: false,
    dialog: false,
    open: async (page) => {
      await page.goto("/esqueci-senha");
      await readyHeading(page);
    },
  },
  {
    id: "S7",
    player: false,
    dialog: false,
    open: async (page, fx) => {
      await page.goto(fx.resetLink);
      await readyHeading(page);
      await expect(page.getByLabel("Nova senha")).toBeVisible();
    },
  },
  {
    id: "S8",
    player: true,
    dialog: false,
    open: async (page) => {
      await page.goto("/perfil");
      await expect(page.getByRole("heading", { name: "Meu perfil" })).toBeVisible();
      // QA-21: o <h1> aparece antes do perfil carregar; a tela so esta pronta com as acoes visiveis.
      await expect(page.getByRole("button", { name: "Excluir conta" })).toBeVisible();
    },
  },
  {
    id: "S9",
    player: true,
    dialog: false,
    open: async (page) => {
      await page.goto("/perfil/editar");
      await readyHeading(page);
      await expect(page.getByLabel("Nome de exibição")).toBeVisible();
    },
  },
  {
    id: "S10",
    player: true,
    dialog: true,
    open: async (page) => {
      await page.goto("/perfil/editar");
      await expect(page.getByLabel("Nome de exibição")).toBeVisible();
      await page.getByRole("button", { name: "Alterar avatar" }).click();
      await expect(page.getByRole("dialog", { name: "Alterar avatar" })).toBeVisible();
    },
  },
  {
    id: "S11",
    player: true,
    dialog: true,
    open: async (page) => {
      await page.goto("/perfil");
      await expect(page.getByRole("heading", { name: "Meu perfil" })).toBeVisible();
      await page.getByRole("button", { name: "Excluir conta" }).click();
      await expect(page.getByRole("alertdialog", { name: "Excluir conta" })).toBeVisible();
    },
  },
];

// Auxiliares executados no navegador (injetados em toda navegacao). Mantidos em JS puro dentro de uma string.
const BROWSER_HELPERS = String.raw`
window.__e2e = (() => {
  const INTERACTIVE = 'a[href], button, input:not([type="hidden"]), select, textarea, summary, [role="button"], [role="link"], [tabindex]';
  const scope = () => document.querySelector('[role="dialog"], [role="alertdialog"], dialog[open]') || document.body;
  const visible = (el) => {
    if (!el.checkVisibility({ checkVisibilityCSS: true })) return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };
  const describe = (el) => {
    const text = (el.getAttribute('aria-label') || el.textContent || el.getAttribute('name') || '').trim().replace(/\s+/g, ' ').slice(0, 40);
    return el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + (el.classList.length ? '.' + [...el.classList].join('.') : '') + ' "' + text + '"';
  };
  const tabbables = () => {
    const all = [...scope().querySelectorAll(INTERACTIVE)].filter((el) => !el.disabled && el.tabIndex >= 0 && !el.closest('[inert]') && visible(el));
    return all.filter((el) => {
      if (el.type !== 'radio') return true;
      const group = all.filter((o) => o.type === 'radio' && o.name === el.name && o.form === el.form);
      const checked = group.find((o) => o.checked);
      return checked ? el === checked : el === group[0];
    });
  };
  const targets = () => {
    const seen = new Set();
    const out = [];
    for (const el of scope().querySelectorAll(INTERACTIVE)) {
      if (el.disabled || el.closest('[inert]') || !visible(el)) continue;
      const box = el.type === 'checkbox' || el.type === 'radio' ? (el.labels && el.labels[0]) || el.closest('label') || el : el;
      if (seen.has(box)) continue;
      const r = box.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) continue;
      if (r.right <= 0 || r.bottom <= 0 || r.left < -100) continue;
      seen.add(box);
      out.push({ el: box, r });
    }
    return out;
  };
  const lineBoxes = (el, fallback) => {
    const rects = [...el.getClientRects()].filter((r) => r.width > 1 && r.height > 1);
    return rects.length ? rects : [fallback];
  };
  const NATIVELY_INTERACTIVE = 'a[href], button, input, select, textarea, summary, [role="button"], [role="link"], [role="radio"], [role="checkbox"], [role="switch"], [role="tab"], [role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"], [role="option"], [role="combobox"], [role="textbox"], [role="slider"], [role="spinbutton"], [role="treeitem"]';
  const isProgrammaticFocusOnly = (el) => el.getAttribute('tabindex') === '-1' && !el.matches(NATIVELY_INTERACTIVE);
  const isInlineLink = (el) => el.tagName === 'A' && getComputedStyle(el).display === 'inline' && el.parentElement && (el.parentElement.textContent || '').trim().length > (el.textContent || '').trim().length + 1;
  return {
    describe,
    tabbableList: () => tabbables().map(describe),
    positiveTabindex: () => [...document.querySelectorAll('[tabindex]')].filter((el) => el.tabIndex > 0).map(describe),
    activeIndex: () => {
      const list = tabbables();
      const el = document.activeElement;
      return { index: list.indexOf(el), what: el ? describe(el) : 'nenhum' };
    },
    activeLabel: () => {
      const el = document.activeElement;
      if (!el) return '';
      const label = el.labels && el.labels[0] ? el.labels[0].textContent : '';
      return ((el.getAttribute('aria-label') || label || el.textContent || el.getAttribute('placeholder') || '') + '').trim().replace(/\s+/g, ' ');
    },
    insideScope: () => scope() === document.body || scope().contains(document.activeElement),
    overflowX: () => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }),
    overlaps: () => {
      const list = targets();
      const out = [];
      for (let i = 0; i < list.length; i++) {
        for (let j = i + 1; j < list.length; j++) {
          const a = list[i], b = list[j];
          if (a.el.contains(b.el) || b.el.contains(a.el)) continue;
          // QA-19: compara as caixas de linha (getClientRects), nao a caixa envolvente: um link inline quebrado
          // em duas linhas tem uma envolvente que cobre o vazio ao lado de cada linha e cruza links vizinhos sem tocar neles.
          const hit = lineBoxes(a.el, a.r).some((ra) => lineBoxes(b.el, b.r).some((rb) => {
            const w = Math.min(ra.right, rb.right) - Math.max(ra.left, rb.left);
            const h = Math.min(ra.bottom, rb.bottom) - Math.max(ra.top, rb.top);
            return w > 1 && h > 1;
          }));
          if (hit) out.push(describe(a.el) + ' x ' + describe(b.el));
        }
      }
      return out;
    },
    // T-175: o <h1> com tabindex=-1 (foco por programa, A11Y-34) tem 32px de altura pela spec (24/32); nao e alvo de ponteiro.
    smallTargets: (min) => targets().filter(({ el, r }) => !isInlineLink(el) && !isProgrammaticFocusOnly(el) && (r.width < min - 0.5 || r.height < min - 0.5)).map(({ el, r }) => describe(el) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height)),
    // skipTextFields: um <input> de texto de linha unica rola o proprio texto por dentro (design.md A.8.5, 'Enorme'); so os
    // tipos de texto sao dispensados. Outro <input> (type=file) nao oferece como rolar e continua sendo examinado (R-63).
    clipped: (skipTextFields) => {
      const out = [];
      const TEXT_TYPES = ['text', 'email', 'password', 'search', 'tel', 'url'];
      for (const el of document.querySelectorAll('body *')) {
        if (skipTextFields && el.tagName === 'INPUT' && TEXT_TYPES.includes(el.type)) continue;
        if (['SVG', 'IMG', 'PATH', 'CIRCLE', 'SCRIPT', 'STYLE'].includes(el.tagName.toUpperCase())) continue;
        const r = el.getBoundingClientRect();
        if (r.width < 4 || r.height < 4 || !el.checkVisibility({ checkVisibilityCSS: true })) continue;
        const cs = getComputedStyle(el);
        const clipX = cs.overflowX === 'hidden' || cs.overflowX === 'clip';
        const clipY = cs.overflowY === 'hidden' || cs.overflowY === 'clip';
        if ((clipX && el.scrollWidth > el.clientWidth + 1) || (clipY && el.scrollHeight > el.clientHeight + 1) || (cs.textOverflow === 'ellipsis' && el.scrollWidth > el.clientWidth + 1)) {
          out.push(describe(el) + ' scroll ' + el.scrollWidth + 'x' + el.scrollHeight + ' client ' + el.clientWidth + 'x' + el.clientHeight);
        }
      }
      return out;
    },
    buttonBackgrounds: () => {
      const alpha = (c) => { const m = c.match(/rgba?\(([^)]+)\)/); if (!m) return 0; const p = m[1].split(/[ ,\/]+/).filter(Boolean); return p.length > 3 ? parseFloat(p[3]) : 1; };
      const chromatic = (c) => { const p = (c.match(/[0-9.]+/g) || []).map(Number); if (p.length < 3) return false; if (p.length > 3 && p[3] < 0.5) return false; return Math.max(p[0], p[1], p[2]) - Math.min(p[0], p[1], p[2]) > 40; };
      const surfaceOf = (el) => { for (let p = el.parentElement; p; p = p.parentElement) { const c = getComputedStyle(p).backgroundColor; if (alpha(c) > 0.5) return c; } return getComputedStyle(document.body).backgroundColor; };
      return [...scope().querySelectorAll('button, [role="button"], a.button')].filter(visible).map((el) => {
        const bg = getComputedStyle(el).backgroundColor;
        return { label: describe(el), bg, surface: surfaceOf(el), filled: chromatic(bg) };
      });
    },
    filledButtons: () => {
      const alpha = (c) => { const m = c.match(/rgba?\(([^)]+)\)/); if (!m) return 0; const p = m[1].split(/[ ,\/]+/).filter(Boolean); return p.length > 3 ? parseFloat(p[3]) : 1; };
      const chromatic = (c) => { const p = (c.match(/[0-9.]+/g) || []).map(Number); if (p.length < 3) return false; if (p.length > 3 && p[3] < 0.5) return false; return Math.max(p[0], p[1], p[2]) - Math.min(p[0], p[1], p[2]) > 40; };
      const surfaceOf = (el) => { for (let p = el.parentElement; p; p = p.parentElement) { const c = getComputedStyle(p).backgroundColor; if (alpha(c) > 0.5) return c; } return getComputedStyle(document.body).backgroundColor; };
      return [...scope().querySelectorAll('button, [role="button"], a.button')].filter(visible).filter((el) => { const bg = getComputedStyle(el).backgroundColor; return chromatic(bg); }).map((el) => 'bg ' + getComputedStyle(el).backgroundColor + ': ' + describe(el));
    },
    unnamed: () => targets().filter(({ el }) => {
      const e = el.matches('label') ? el.querySelector('input') || el : el;
      const text = (e.getAttribute('aria-label') || '') + (e.getAttribute('aria-labelledby') ? 'x' : '') + (e.labels && e.labels.length ? 'x' : '') + (e.innerText || '') + (e.getAttribute('title') || '') + ((e.type === 'submit' || e.type === 'button') ? (e.value || '') : '') + (e.querySelector && e.querySelector('img[alt]:not([alt=""])') ? 'x' : '');
      return text.trim() === '';
    }).map(({ el }) => describe(el)),
    hoverReveals: () => {
      const out = [];
      const visit = (rules) => {
        for (const rule of rules) {
          if (rule.cssRules && !rule.selectorText) visit(rule.cssRules);
          else if (rule.selectorText && rule.selectorText.includes(':hover')) {
            const s = rule.style;
            const reveals = ['display', 'visibility', 'opacity', 'clip', 'clip-path', 'max-height', 'height', 'transform'].filter((p) => s.getPropertyValue(p) !== '');
            if (reveals.length) out.push(rule.selectorText + ' { ' + reveals.join(', ') + ' }');
          }
        }
      };
      for (const sheet of document.styleSheets) {
        try { visit(sheet.cssRules); } catch { /* folha externa */ }
      }
      return out;
    },
    motion: () => document.getAnimations().map((a) => {
      const keyframes = a.effect && a.effect.getKeyframes ? a.effect.getKeyframes() : [];
      const props = new Set();
      for (const k of keyframes) for (const p of Object.keys(k)) props.add(p);
      const timing = a.effect ? a.effect.getComputedTiming() : { iterations: 1 };
      return { target: a.effect && a.effect.target ? describe(a.effect.target) : '?', props: [...props].filter((p) => ['transform', 'scale', 'rotate', 'translate'].includes(p)), infinite: timing.iterations === Infinity };
    }).filter((m) => m.props.length > 0 || m.infinite),
    dialogPanel: () => {
      const d = document.querySelector('[role="dialog"], [role="alertdialog"]');
      const panel = d ? d.closest('.dialog__panel') || d : null;
      if (!panel) return null;
      const cs = getComputedStyle(panel);
      const r = panel.getBoundingClientRect();
      const desc = panel.querySelector('.dialog__description');
      return {
        overflowY: cs.overflowY, scrollHeight: panel.scrollHeight, clientHeight: panel.clientHeight,
        top: r.top, bottom: r.bottom, height: r.height, innerHeight: window.innerHeight,
        borderTop: cs.borderTopWidth + ' ' + cs.borderTopStyle,
        panelColor: cs.color, descriptionColor: desc ? getComputedStyle(desc).color : null,
      };
    },
  };
})();
`;

type E2E = {
  describe: (el: Element) => string;
  tabbableList: () => string[];
  positiveTabindex: () => string[];
  activeIndex: () => { index: number; what: string };
  activeLabel: () => string;
  insideScope: () => boolean;
  overflowX: () => { scrollWidth: number; clientWidth: number };
  overlaps: () => string[];
  smallTargets: (min: number) => string[];
  clipped: (skipTextFields?: boolean) => string[];
  filledButtons: () => string[];
  buttonBackgrounds: () => { label: string; bg: string; surface: string; filled: boolean }[];
  unnamed: () => string[];
  hoverReveals: () => string[];
  motion: () => { target: string; props: string[]; infinite: boolean }[];
  dialogPanel: () => {
    overflowY: string;
    scrollHeight: number;
    clientHeight: number;
    top: number;
    bottom: number;
    height: number;
    innerHeight: number;
    borderTop: string;
    panelColor: string;
    descriptionColor: string | null;
  } | null;
};
type Probe = Window & { __e2e: E2E };

async function inPage<T>(page: Page, fn: (e2e: E2E) => T): Promise<T> {
  return page.evaluate(`(${fn.toString()})(window.__e2e)`) as Promise<T>;
}

async function openScreen(
  browser: Browser,
  screen: Screen,
  options: BrowserContextOptions = {},
): Promise<{ page: Page; close: () => Promise<void> }> {
  const context = await browser.newContext({
    baseURL: "http://localhost:5173",
    storageState: screen.player ? fixtures.storageState : undefined,
    ...options,
  });
  await context.addInitScript(BROWSER_HELPERS);
  const page = await context.newPage();
  await screen.open(page, fixtures);
  return { page, close: () => context.close() };
}

async function withScreen(
  browser: Browser,
  screen: Screen,
  options: BrowserContextOptions,
  run: (page: Page) => Promise<void>,
) {
  const { page, close } = await openScreen(browser, screen, options);
  try {
    await run(page);
  } finally {
    await close();
  }
}

async function register(page: Page, email: string, displayName: string) {
  await page.goto("/cadastro");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByLabel("Nome de exibição").fill(displayName);
  await page.getByRole("checkbox", { name: /Li e aceito/ }).check();
  await page.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }).check();
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page).toHaveURL(/\/perfil$/);
}

test.beforeAll(async ({ browser }) => {
  test.setTimeout(120_000);
  const context = await browser.newContext({ baseURL: "http://localhost:5173" });
  const page = await context.newPage();
  const clock = await page.request.post(`${API_ORIGIN}/__test/clock/reset`);
  expect(clock.status(), "POST /__test/clock/reset").toBe(204);

  const suffix = uniqueSuffix();
  const email = `e2e-a11y-${suffix}@example.test`;
  const displayName = `e2ea11y${suffix.slice(-8)}`;
  await register(page, email, displayName);
  const storageState = test.info().outputPath("a11y-session.json");
  await context.storageState({ path: storageState });
  await page.context().clearCookies();

  await page.goto("/esqueci-senha");
  await page.getByLabel("Email").fill(email);
  await page.getByRole("button", { name: "Enviar instruções" }).click();
  await expect(page.getByRole("status")).toBeVisible();
  const response = await page.request.get(
    `${API_ORIGIN}/__test/password-reset/last-link?email=${encodeURIComponent(email)}`,
  );
  expect(response.status(), "link de redefinicao capturado pelo host E2E").toBe(200);
  const { link } = (await response.json()) as { link: string };
  await context.close();

  fixtures = { email, displayName, resetLink: link, storageState };
});

test.describe.configure({ timeout: 90_000 });

// ---------------------------------------------------------------------------------------------------------------
// 1. axe WCAG 2.1 AA
// ---------------------------------------------------------------------------------------------------------------
test.describe("axe: sem violacoes WCAG 2.1 AA", () => {
  for (const screen of SCREENS) {
    test(`${screen.id} sem violacoes axe`, async ({ browser }) => {
      await withScreen(browser, screen, {}, async (page) => {
        const results = await (await settledAxe(page)).withTags(WCAG_TAGS).analyze();
        expect(
          results.violations.map(
            (v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`,
          ),
        ).toEqual([]);
      });
    });
  }

  test("S1 em estado de erro (envio vazio) sem violacoes axe", async ({ browser }) => {
    await withScreen(browser, SCREENS[0]!, {}, async (page) => {
      await page.getByRole("button", { name: "Criar conta" }).click();
      await expect(page.locator('[role="alert"], a[href^="#"]').first()).toBeVisible();
      const results = await (await settledAxe(page)).withTags(WCAG_TAGS).analyze();
      expect(results.violations.map((v) => v.id)).toEqual([]);
    });
  });

  test("S2 em estado de erro (senha errada) sem violacoes axe", async ({ browser }) => {
    await withScreen(browser, SCREENS[1]!, {}, async (page) => {
      await page.getByLabel("Email").fill(fixtures.email);
      await page.getByLabel("Senha", { exact: true }).fill("e2e-senha-errada-999");
      await page.getByRole("button", { name: "Entrar" }).click();
      await expect(page.getByText("Email ou senha inválidos.")).toBeVisible();
      const results = await (await settledAxe(page)).withTags(WCAG_TAGS).analyze();
      expect(results.violations.map((v) => v.id)).toEqual([]);
    });
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 2. Teclado: ordem de Tab = ordem do DOM, sem tabindex positivo (A11Y-07, A11Y-10)
// ---------------------------------------------------------------------------------------------------------------
test.describe("teclado: ordem de Tab", () => {
  for (const screen of SCREENS) {
    test(`${screen.id} Tab percorre os controles na ordem do DOM, sem tabindex positivo`, async ({
      browser,
    }) => {
      await withScreen(browser, screen, {}, async (page) => {
        expect(await inPage(page, (e) => e.positiveTabindex()), "tabindex positivo").toEqual([]);

        const expected = await inPage(page, (e) => e.tabbableList());
        if (expected.length === 0) return;

        const visited: { index: number; what: string }[] = [];
        for (let i = 0; i < expected.length + 2; i++) {
          await page.keyboard.press("Tab");
          visited.push(await inPage(page, (e) => e.activeIndex()));
        }
        const stops = visited.filter((v) => v.index >= 0);
        const reached = new Set(stops.map((s) => s.index));
        expect(
          expected.filter((_, idx) => !reached.has(idx)),
          "controles que o Tab nao alcancou",
        ).toEqual([]);
        for (let i = 1; i < stops.length; i++) {
          const prev = stops[i - 1]!.index;
          const next = stops[i]!.index;
          const wraps = prev === expected.length - 1 && next === 0;
          expect(
            next === prev + 1 || wraps,
            `Tab foi de ${expected[prev]} para ${expected[next]} (fora da ordem do DOM)`,
          ).toBe(true);
        }
        if (screen.dialog) {
          expect(await inPage(page, (e) => e.insideScope()), "foco preso no dialog").toBe(true);
        }
      });
    });
  }
});

// R-42 / A11Y-23 (WCAG 2.4.1): o SkipLink e o primeiro item da ordem de Tab e Enter leva ao <main id="conteudo">.
// Nao se mede o "primeiro Tab" a partir do carregamento: o foco de rota esta no <h1> (A11Y-34) e o Tab seguinte
// pula o SkipLink e o cabecalho (QA-03, decisao pendente).
test.describe("teclado: SkipLink e o primeiro item da ordem de Tab", () => {
  for (const screen of SCREENS.filter((s) => !s.dialog)) {
    test(`${screen.id} SkipLink vem primeiro no DOM e Enter foca o <main>`, async ({ browser }) => {
      await withScreen(browser, screen, {}, async (page) => {
        const skip = page.getByRole("link", { name: "Pular para o conteúdo" });
        const firstTabbable = page
          .locator('a[href], button, input, select, textarea, [tabindex]:not([tabindex="-1"])')
          .first();
        await expect(firstTabbable).toHaveText("Pular para o conteúdo");
        await skip.focus();
        await expect(skip).toBeFocused();
        await page.keyboard.press("Enter");
        await expect(page.locator("main#conteudo")).toBeFocused();
      });
    });
  }
});

// Regra de foco (A11Y-23, A11Y-34, QA-03): na carga de pagina o foco nao vai ao <h1> e o primeiro Tab chega ao
// SkipLink; em transicao de rota da SPA o foco vai ao <h1>.
test.describe("foco: carga de pagina x transicao da SPA", () => {
  test("primeiro Tab em /entrar (carga) cai no SkipLink e o <h1> nao recebe foco", async ({
    browser,
  }) => {
    await withScreen(browser, SCREENS[1]!, {}, async (page) => {
      await expect(page.locator("main h1")).not.toBeFocused();
      await page.keyboard.press("Tab");
      await expect(page.getByRole("link", { name: "Pular para o conteúdo" })).toBeFocused();
    });
  });

  test("navegacao dentro da SPA (/entrar para /cadastro) move o foco para o <h1>", async ({
    browser,
  }) => {
    await withScreen(browser, SCREENS[1]!, {}, async (page) => {
      await page.locator("main").getByRole("link", { name: "Criar conta" }).click();
      await expect(page).toHaveURL(/\/cadastro$/);
      await expect(page.locator("main h1")).toBeFocused();
    });
  });
});

async function tabTo(page: Page, label: RegExp, max = 40) {
  for (let i = 0; i < max; i++) {
    await page.keyboard.press("Tab");
    const current = await inPage(page, (e) => e.activeLabel());
    if (label.test(current)) return;
  }
  throw new Error(`Tab nao alcancou o controle ${label} em ${max} passos`);
}

test.describe("teclado: fluxos completos so com o teclado", () => {
  test("S1 cadastro completo por teclado cria a conta e abre o perfil", async ({ browser }) => {
    const { page, close } = await openScreen(browser, SCREENS[0]!);
    try {
      const suffix = uniqueSuffix();
      await tabTo(page, /^Email/);
      await page.keyboard.type(`e2e-kbd-${suffix}@example.test`);
      await tabTo(page, /^Senha\b/);
      await page.keyboard.type(PASSWORD);
      await tabTo(page, /^Nome de exibição/);
      await page.keyboard.type(`e2ekbd${suffix.slice(-8)}`);
      await tabTo(page, /Li e aceito/);
      await page.keyboard.press("Space");
      await tabTo(page, /Declaro ter 18 anos/);
      await page.keyboard.press("Space");
      await tabTo(page, /^Criar conta$/);
      await page.keyboard.press("Enter");
      await expect(page).toHaveURL(/\/perfil$/);
    } finally {
      await close();
    }
  });

  test("S2 login, menu da conta, S11 abrir e fechar com Escape devolvendo o foco, logout: so teclado", async ({
    browser,
  }) => {
    const { page, close } = await openScreen(browser, SCREENS[1]!);
    try {
      await tabTo(page, /^Email/);
      await page.keyboard.type(fixtures.email);
      await tabTo(page, /^Senha\b/);
      await page.keyboard.type(PASSWORD);
      await page.keyboard.press("Enter");
      await expect(page).toHaveURL(/\/perfil$/);

      await tabTo(page, /^Excluir conta$/);
      await page.keyboard.press("Enter");
      await expect(page.getByRole("alertdialog", { name: "Excluir conta" })).toBeVisible();
      expect(await inPage(page, (e) => e.insideScope()), "foco dentro do dialog").toBe(true);
      await page.keyboard.press("Escape");
      await expect(page.getByRole("alertdialog")).toBeHidden();
      expect(await inPage(page, (e) => e.activeLabel()), "foco volta ao gatilho").toMatch(
        /^Excluir conta$/,
      );

      await tabTo(page, /^Menu da conta:/);
      await page.keyboard.press("Enter");
      await expect(page.getByRole("button", { name: "Sair" })).toBeVisible();
      await tabTo(page, /^Sair$/);
      await page.keyboard.press("Enter");
      await expect(page).toHaveURL(/\/entrar/);
    } finally {
      await close();
    }
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 3. Viewports 320/768/1280 e zoom 400% (A11Y-18, UX-10)
// ---------------------------------------------------------------------------------------------------------------
const VIEWPORTS = [
  { name: "320", width: 320, height: 800 },
  { name: "768", width: 768, height: 1024 },
  { name: "1280", width: 1280, height: 800 },
  { name: "zoom 400% (1280x1024 / 4)", width: 320, height: 256 },
];

test.describe("responsivo: sem sobreposicao e sem rolagem horizontal", () => {
  for (const screen of SCREENS) {
    test(`${screen.id} em 320, 768, 1280 e zoom 400%`, async ({ browser }) => {
      await withScreen(browser, screen, {}, async (page) => {
        const baseline = (await inPage(page, (e) => e.tabbableList())).length;
        for (const vp of VIEWPORTS) {
          await page.setViewportSize({ width: vp.width, height: vp.height });
          const o = await inPage(page, (e) => e.overflowX());
          expect
            .soft(o.scrollWidth, `${vp.name}: rolagem horizontal`)
            .toBeLessThanOrEqual(o.clientWidth);
          expect
            .soft(await inPage(page, (e) => e.overlaps()), `${vp.name}: controles sobrepostos`)
            .toEqual([]);
          expect
            .soft(
              (await inPage(page, (e) => e.tabbableList())).length,
              `${vp.name}: controles ainda disponiveis`,
            )
            .toBe(baseline);
          expect
            .soft(await inPage(page, (e) => e.unnamed()), `${vp.name}: controles sem nome`)
            .toEqual([]);
        }
      });
    });
  }
});

// ---------------------------------------------------------------------------------------------------------------
// 4. Texto a 200% e espacamento aumentado (A11Y-19)
// ---------------------------------------------------------------------------------------------------------------
const TEXT_SPACING_CSS = `
  html { font-size: 200% !important; }
  * { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; }
  p { margin-bottom: 2em !important; }
`;

test.describe("texto a 200% e espacamento aumentado sem corte", () => {
  for (const screen of SCREENS) {
    test(`${screen.id} sem corte nem rolagem horizontal`, async ({ browser }) => {
      await withScreen(browser, screen, {}, async (page) => {
        await page.addStyleTag({ content: TEXT_SPACING_CSS });
        for (const width of [1280, 768]) {
          await page.setViewportSize({ width, height: 800 });
          const o = await inPage(page, (e) => e.overflowX());
          expect
            .soft(o.scrollWidth, `${width}: rolagem horizontal`)
            .toBeLessThanOrEqual(o.clientWidth);
          expect
            .soft(await inPage(page, (e) => e.clipped()), `${width}: conteudo cortado`)
            .toEqual([]);
          expect
            .soft(await inPage(page, (e) => e.overlaps()), `${width}: controles sobrepostos`)
            .toEqual([]);
        }
      });
    });
  }
});

// T-173 / U-10 e T-175 / R-58: a 320px com texto a 200% o documento nao rola na horizontal, nada se sobrepoe nem
// fica cortado, nas telas de pagina, em S3, nos dois dialogos e no link invalido de S7.
const PAGE_SCREEN_IDS = ["S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9", "S10", "S11"];

// S7 com link invalido ou expirado: outra tela (outro <h1>) na mesma rota.
const INVALID_LINK_SCREEN: Screen = {
  id: "S7",
  player: false,
  dialog: false,
  open: async (page) => {
    await page.goto("/redefinir-senha");
    await expect(
      page.getByRole("heading", { level: 1, name: "Link inválido ou expirado" }),
    ).toBeVisible();
  },
};

const BIG_TEXT_SCREENS: { label: string; screen: Screen }[] = [
  ...SCREENS.filter((s) => PAGE_SCREEN_IDS.includes(s.id)).map((s) => ({
    label: s.id,
    screen: s,
  })),
  { label: "S7 com link invalido", screen: INVALID_LINK_SCREEN },
];

test.describe("320px com texto a 200%: sem rolagem horizontal, sobreposicao nem corte (U-10, R-58)", () => {
  for (const { label, screen } of BIG_TEXT_SCREENS) {
    test(`${label} a 320px e texto a 200%`, async ({ browser }) => {
      await withScreen(browser, screen, {}, async (page) => {
        await page.setViewportSize({ width: 320, height: 800 });
        await page.addStyleTag({ content: TEXT_SPACING_CSS });
        const o = await inPage(page, (e) => e.overflowX());
        expect
          .soft(o.scrollWidth, `scrollWidth ${o.scrollWidth} > clientWidth ${o.clientWidth}`)
          .toBeLessThanOrEqual(o.clientWidth);
        expect.soft(await inPage(page, (e) => e.overlaps()), "controles sobrepostos").toEqual([]);
        // Um campo de texto de linha unica rola o proprio texto (nome longo a 200%); qualquer outro elemento
        // cortado reprova. Unica excecao: o controle nativo do upload (.file-upload__input, so no S10), cujo texto
        // interno "No file chosen" vira "N..." a 200% sem como o CSS evitar. Decisao do usuario em R-63, registrada em
        // design.md A.8.5; o nome do arquivo escolhido aparece a parte em .file-upload__name. A excecao nao depende da
        // medida do controle (varia com fonte e sistema); a caixa dele tem de ficar dentro da viewport (sem estouro).
        const clipped = (await inPage(page, (e) => e.clipped(true))).filter(
          (entry) => !isAcceptedUploadClip(screen.id, entry),
        );
        expect.soft(clipped, "conteudo cortado").toEqual([]);
        if (screen.id === "S10") {
          const box = await page.locator(".file-upload__input").boundingBox();
          expect(box, "controle de upload visivel").not.toBeNull();
          expect
            .soft(box!.x, "controle de upload a esquerda da viewport")
            .toBeGreaterThanOrEqual(0);
          expect
            .soft(box!.x + box!.width, "controle de upload alem da viewport")
            .toBeLessThanOrEqual(320);
        }
        if (screen.dialog) {
          const panel = await page.evaluate(() => {
            const d = document.querySelector('[role="dialog"], [role="alertdialog"]');
            const el = d ? d.closest(".dialog__panel") || d : null;
            return el ? { scrollWidth: el.scrollWidth, clientWidth: el.clientWidth } : null;
          });
          expect(panel, "painel do dialogo").not.toBeNull();
          expect
            .soft(panel!.scrollWidth, "painel do dialogo rola na horizontal")
            .toBeLessThanOrEqual(panel!.clientWidth);
        }
      });
    });
  }
});

// T-175 / U-12: o botao "Mostrar senha" desce de linha quando falta espaco, e a area de texto fica legivel.
const PASSWORD_SCREEN_IDS = ["S1", "S2", "S5", "S7", "S11"];
const MIN_TEXT_AREA = 160;

type RowMetrics = { input: number; sameRow: boolean; fields: number };
function passwordRowMetrics(): RowMetrics {
  const scope = document.querySelector('[role="dialog"], [role="alertdialog"]') || document.body;
  const controls = [...scope.querySelectorAll(".text-field__control")].filter((c) =>
    c.querySelector(".password-field__toggle"),
  );
  const first = controls[0];
  const input = first?.querySelector("input")?.getBoundingClientRect();
  const button = first?.querySelector(".password-field__toggle")?.getBoundingClientRect();
  return {
    fields: controls.length,
    input: input ? input.width : 0,
    sameRow: !!input && !!button && button.top < input.bottom - 1 && button.bottom > input.top + 1,
  };
}

test.describe("campo de senha: area de texto legivel a 320px com texto a 200% (U-12)", () => {
  for (const screen of SCREENS.filter((s) => PASSWORD_SCREEN_IDS.includes(s.id))) {
    test(`${screen.id}: a 200% o botao desce e a area de texto tem largura util`, async ({
      browser,
    }) => {
      await withScreen(browser, screen, {}, async (page) => {
        await page.setViewportSize({ width: 320, height: 800 });
        await page.addStyleTag({ content: TEXT_SPACING_CSS });
        const m = await page.evaluate(passwordRowMetrics);
        expect(m.fields, "campo de senha com botao na tela").toBeGreaterThan(0);
        expect(m.input, `area de texto de ${Math.round(m.input)}px`).toBeGreaterThanOrEqual(
          MIN_TEXT_AREA,
        );
        expect(m.sameRow, "o botao desceu de linha").toBe(false);
      });
    });

    // QA-22: a 320px a largura do botao depende da fonte do sistema (no Ubuntu do CI ele desce de linha no S11),
    // entao vale o que importa ao Jogador: na mesma linha, ou abaixo com a area de texto legivel.
    test(`${screen.id}: a 100% o botao fica na mesma linha a 768 e 1280px e a 320px so desce com area legivel`, async ({
      browser,
    }) => {
      await withScreen(browser, screen, {}, async (page) => {
        for (const width of [768, 1280]) {
          await page.setViewportSize({ width, height: 800 });
          const m = await page.evaluate(passwordRowMetrics);
          expect(m.fields).toBeGreaterThan(0);
          expect(m.sameRow, `${width}: botao e campo na mesma linha`).toBe(true);
        }
        await page.setViewportSize({ width: 320, height: 800 });
        const narrow = await page.evaluate(passwordRowMetrics);
        expect(narrow.fields).toBeGreaterThan(0);
        expect(
          narrow.sameRow || narrow.input >= MIN_TEXT_AREA,
          `320: botao abaixo com area de texto de ${Math.round(narrow.input)}px`,
        ).toBe(true);
      });
    });
  }
});

// T-175 / U-13 e U-08 (A11Y-20): o <h1> tem 24px/32px calculados a 100% em toda tela, e o <title> e "<h1> - Deckloom".
test.describe("h1 de 24px e titulo igual ao h1 em toda tela (U-13, U-08)", () => {
  const all = [
    ...SCREENS.filter((s) => !s.dialog).map((s) => ({ label: s.id, screen: s })),
    { label: "S7 com link invalido", screen: INVALID_LINK_SCREEN },
  ];
  for (const { label, screen } of all) {
    test(`${label}: h1 24px com altura de linha 32px e title coerente`, async ({ browser }) => {
      await withScreen(
        browser,
        screen,
        { viewport: { width: 1280, height: 800 } },
        async (page) => {
          const info = await page.evaluate(() => {
            const h1 = document.querySelector("main h1");
            const cs = h1 ? getComputedStyle(h1) : null;
            return {
              text: h1?.textContent?.trim() ?? "",
              size: cs?.fontSize ?? "",
              lineHeight: cs?.lineHeight ?? "",
              weight: cs?.fontWeight ?? "",
              title: document.title,
            };
          });
          expect(info.size, "font-size do h1").toBe("24px");
          expect(parseFloat(info.lineHeight), "line-height do h1").toBeCloseTo(32, 0);
          expect(info.weight, "peso do h1").toBe("700");
          expect(info.title).toBe(`${info.text} - Deckloom`);
        },
      );
    });
  }
});

// T-181 / R-71: unica excecao da varredura de corte, sem amarrar numeros: o controle .file-upload__input no S10.
function isAcceptedUploadClip(screenId: string, entry: string): boolean {
  // describe() devolve "input.classe1.classe2 "texto"" (o "#id" do React e instavel, entao e ignorado).
  return (
    screenId === "S10" &&
    /^input(?:#[^\s.]+)?\.(?:[^\s.]+\.)*file-upload__input(?:\.[^\s.]+)* "/.test(entry)
  );
}

// T-177 / R-63: a varredura de corte dispensa so campo de texto; um <input type="file"> cortado reprova.
test.describe("clipped() distingue campo de texto de upload (R-63)", () => {
  test("input de texto cortado e dispensado; input type=file cortado e acusado", async ({
    browser,
  }) => {
    const s2 = SCREENS.find((s) => s.id === "S2")!;
    await withScreen(browser, s2, {}, async (page) => {
      await page.evaluate(() => {
        for (const type of ["text", "email", "file"]) {
          const input = document.createElement("input");
          input.type = type;
          input.id = `sonda-${type}`;
          input.style.cssText = "width: 40px; display: block; font-size: 32px; overflow: clip;";
          if (type !== "file") input.value = "um valor bem comprido para passar de 40px";
          document.querySelector("main")!.append(input);
        }
      });
      const found = await inPage(page, (e) => e.clipped(true));
      expect(
        found.some((entry) => entry.includes("#sonda-file")),
        "file acusado",
      ).toBe(true);
      expect(
        found.some((entry) => entry.includes("#sonda-text")),
        "text dispensado",
      ).toBe(false);
      expect(
        found.some((entry) => entry.includes("#sonda-email")),
        "email dispensado",
      ).toBe(false);
      const all = await inPage(page, (e) => e.clipped());
      expect(
        all.some((entry) => entry.includes("#sonda-text")),
        "sem dispensa, text aparece",
      ).toBe(true);
    });
  });
});

// T-181 / R-71: a excecao do upload nao vaza para outro <input> nem para outra tela.
test.describe("excecao do upload (R-71)", () => {
  test("aceita so .file-upload__input no S10, seja qual for a medida", () => {
    expect(
      isAcceptedUploadClip("S10", 'input#_r_9_.file-upload__input "" scroll 300x48 client 240x48'),
    ).toBe(true);
    expect(
      isAcceptedUploadClip("S10", 'input.file-upload__input "" scroll 242x48 client 240x48'),
    ).toBe(true);
  });

  test("outra tela ou outro elemento continua reprovando", () => {
    expect(
      isAcceptedUploadClip("S2", 'input.file-upload__input "" scroll 242x48 client 240x48'),
    ).toBe(false);
    expect(isAcceptedUploadClip("S10", 'input#sonda-file "" scroll 242x48 client 240x48')).toBe(
      false,
    );
    expect(
      isAcceptedUploadClip("S10", 'span.file-upload__name "" scroll 242x48 client 240x48'),
    ).toBe(false);
  });

  test("S10: outro <input type=file> cortado sobrevive ao filtro", async ({ browser }) => {
    const s10 = SCREENS.find((s) => s.id === "S10")!;
    await withScreen(browser, s10, {}, async (page) => {
      await page.evaluate(() => {
        const input = document.createElement("input");
        input.type = "file";
        input.id = "sonda-file";
        input.style.cssText = "width: 40px; display: block; font-size: 32px; overflow: clip;";
        document.querySelector("main")!.append(input);
      });
      const kept = (await inPage(page, (e) => e.clipped(true))).filter(
        (entry) => !isAcceptedUploadClip("S10", entry),
      );
      expect(kept.some((entry) => entry.includes("#sonda-file"))).toBe(true);
    });
  });
});

// T-175 / U-14: a 320px com texto a 200% o painel do menu da conta tem margens iguais nos dois lados.
test.describe("menu da conta: margens iguais a 320px com texto a 200% (U-14)", () => {
  test("S8: espaco a esquerda igual ao da direita", async ({ browser }) => {
    const s8 = SCREENS.find((s) => s.id === "S8")!;
    await withScreen(browser, s8, {}, async (page) => {
      await page.setViewportSize({ width: 320, height: 800 });
      await page.addStyleTag({ content: TEXT_SPACING_CSS });
      await page.getByRole("button", { name: /^Menu da conta:/ }).click();
      const panel = page.locator("[data-account-menu-panel]");
      await expect(panel).toBeVisible();
      // Espera o fim da animacao de abertura (MOT-11), nao um tempo fixo.
      await panel.evaluate((el) => Promise.all(el.getAnimations().map((a) => a.finished)));
      const box = (await panel.boundingBox())!;
      const left = box.x;
      const right = 320 - (box.x + box.width);
      expect(Math.abs(left - right), `esquerda ${left}px, direita ${right}px`).toBeLessThanOrEqual(
        1,
      );
      expect(left, "painel dentro da tela").toBeGreaterThanOrEqual(0);
    });
  });
});

// T-175 / U-15: quando os links de visitante descem de linha, o texto alinha com o wordmark.
test.describe("cabecalho de visitante: texto dos links alinhado ao wordmark (U-15)", () => {
  test("S2 a 320px e 200%: o primeiro link comeca na mesma margem do wordmark", async ({
    browser,
  }) => {
    const s2 = SCREENS.find((s) => s.id === "S2")!;
    await withScreen(browser, s2, {}, async (page) => {
      await page.setViewportSize({ width: 320, height: 800 });
      await page.addStyleTag({ content: TEXT_SPACING_CSS });
      const m = await page.evaluate(() => {
        const mark = document.querySelector(".app-header__wordmark")!.getBoundingClientRect();
        const link = document.querySelector(".app-header__actions .app-header__link")!;
        const range = document.createRange();
        range.selectNodeContents(link);
        return {
          markLeft: mark.left,
          markBottom: mark.bottom,
          textLeft: range.getBoundingClientRect().left,
          linkTop: link.getBoundingClientRect().top,
        };
      });
      expect(m.linkTop, "os links descem de linha").toBeGreaterThanOrEqual(m.markBottom - 1);
      expect(
        Math.abs(m.textLeft - m.markLeft),
        `texto em ${m.textLeft}, wordmark em ${m.markLeft}`,
      ).toBeLessThanOrEqual(1);
    });
  });
});

// T-175 / U-17: a 320px com texto a 200% o nome no perfil ganha a largura do painel, sem quebrar em 6 letras.
test.describe("perfil: nome com largura util a 320px com texto a 200% (U-17)", () => {
  test("S8: o nome ocupa pelo menos tres quartos da largura do painel", async ({ browser }) => {
    const s8 = SCREENS.find((s) => s.id === "S8")!;
    await withScreen(browser, s8, {}, async (page) => {
      await page.setViewportSize({ width: 320, height: 800 });
      await page.addStyleTag({ content: TEXT_SPACING_CSS });
      const m = await page.evaluate(() => {
        const name = document.querySelector(".profile__name")!.getBoundingClientRect();
        const panel = document.querySelector(".profile")!.getBoundingClientRect();
        return { name: name.width, panel: panel.width };
      });
      expect(m.name, `nome com ${m.name}px num painel de ${m.panel}px`).toBeGreaterThanOrEqual(
        m.panel * 0.75,
      );
    });
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 5. Nada essencial depende de hover (A11Y-32)
// ---------------------------------------------------------------------------------------------------------------
test.describe("nada essencial depende de hover", () => {
  for (const screen of SCREENS) {
    test(`${screen.id} com hover: none`, async ({ browser }) => {
      await withScreen(browser, screen, { hasTouch: true, isMobile: true }, async (page) => {
        expect(
          await page.evaluate(() => window.matchMedia("(hover: none)").matches),
          "emulacao sem hover",
        ).toBe(true);
        expect(
          await inPage(page, (e) => e.hoverReveals()),
          "regras :hover que revelam ou escondem conteudo",
        ).toEqual([]);
        expect(await inPage(page, (e) => e.unnamed()), "controles sem nome acessivel").toEqual([]);
        expect(
          await page.locator('[role="tooltip"], [data-tooltip]').count(),
          "tooltips so por hover",
        ).toBe(0);
      });
    });
  }
});

// ---------------------------------------------------------------------------------------------------------------
// 6. Midias emuladas: reduced motion (A11Y-37) e prefers-contrast: more (A11Y-38)
// ---------------------------------------------------------------------------------------------------------------
test.describe("prefers-reduced-motion: reduce", () => {
  for (const screen of SCREENS) {
    test(`${screen.id} sem rotacao, escala nem animacao infinita`, async ({ browser }) => {
      await withScreen(browser, screen, { reducedMotion: "reduce" }, async (page) => {
        expect(
          await page.evaluate(() => window.matchMedia("(prefers-reduced-motion: reduce)").matches),
        ).toBe(true);
        expect(
          await inPage(page, (e) => e.motion()),
          "animacoes com transform/escala/rotacao",
        ).toEqual([]);
      });
    });
  }
});

test.describe("prefers-contrast: more", () => {
  for (const screen of SCREENS.filter((s) => s.dialog)) {
    test(`${screen.id} painel com borda de 1px e texto de apoio na cor do texto`, async ({
      browser,
    }) => {
      await withScreen(browser, screen, { contrast: "more" }, async (page) => {
        expect(
          await page.evaluate(() => window.matchMedia("(prefers-contrast: more)").matches),
        ).toBe(true);
        const panel = await inPage(page, (e) => e.dialogPanel());
        expect(panel, "painel do dialog").not.toBeNull();
        expect(panel!.borderTop).toBe("1px solid");
        if (panel!.descriptionColor !== null) {
          expect(panel!.descriptionColor).toBe(panel!.panelColor);
        }
      });
    });
  }
});

// ---------------------------------------------------------------------------------------------------------------
// 7. ART-02: no maximo um botao preenchido por tela; o de perigo e o unico em S8/S11
// ---------------------------------------------------------------------------------------------------------------
test.describe("ART-02: no maximo um botao preenchido por tela", () => {
  for (const screen of SCREENS) {
    for (const width of [1280, 768, 390]) {
      test(`${screen.id} a ${width}px`, async ({ browser }) => {
        await withScreen(browser, screen, { viewport: { width, height: 900 } }, async (page) => {
          const filled = await inPage(page, (e) => e.filledButtons());
          expect(filled.length, `botoes preenchidos: ${filled.join(" | ")}`).toBeLessThanOrEqual(1);
        });
      });
    }
  }

  test("S8 tem no maximo um botao preenchido, e e 'Editar perfil'", async ({ browser }) => {
    await withScreen(browser, SCREENS[7]!, {}, async (page) => {
      const all = await inPage(page, (e) => e.buttonBackgrounds());
      console.log("S8 botoes (fundo computado):", JSON.stringify(all));
      const filled = await inPage(page, (e) => e.filledButtons());
      expect(filled.length, `botoes preenchidos: ${filled.join(" | ")}`).toBe(1);
      expect(filled[0]).toMatch(/Editar perfil/);
    });
  });
});

// ---------------------------------------------------------------------------------------------------------------
// 8. ART-08: alvos de 44px a 320px
// ---------------------------------------------------------------------------------------------------------------
test.describe("ART-08: alvos de 44px a 320px", () => {
  for (const screen of SCREENS) {
    test(`${screen.id} botoes e links com pelo menos 44x44`, async ({ browser }) => {
      await withScreen(browser, screen, { viewport: { width: 320, height: 800 } }, async (page) => {
        const small = await page.evaluate(
          (min) => (window as unknown as Probe).__e2e.smallTargets(min),
          MIN_TARGET,
        );
        expect(small, "alvos menores que 44x44").toEqual([]);
        const o = await inPage(page, (e) => e.overflowX());
        expect(o.scrollWidth, "rolagem horizontal a 320px").toBeLessThanOrEqual(o.clientWidth);
      });
    });
  }
});

// ---------------------------------------------------------------------------------------------------------------
// 9. WC-14: dialogo mais alto que a janela (320x480, celular deitado, texto a 200%)
// ---------------------------------------------------------------------------------------------------------------
const SHORT_WINDOWS = [
  { name: "320x480", width: 320, height: 480, bigText: false },
  { name: "320x480 com texto a 200%", width: 320, height: 480, bigText: true },
  { name: "celular deitado 480x320", width: 480, height: 320, bigText: false },
];

test.describe("WC-14: dialogo mais alto que a janela", () => {
  for (const screen of SCREENS.filter((s) => s.dialog)) {
    for (const win of SHORT_WINDOWS) {
      test(`${screen.id} em ${win.name}: rola por inteiro, botoes alcancaveis, foco preso e devolvido`, async ({
        browser,
      }) => {
        await withScreen(
          browser,
          screen,
          { viewport: { width: win.width, height: win.height } },
          async (page) => {
            if (win.bigText) await page.addStyleTag({ content: TEXT_SPACING_CSS });
            const dialog = page.locator('[role="dialog"], [role="alertdialog"]');
            const metrics = (await inPage(page, (e) => e.dialogPanel()))!;

            expect(metrics.top, "topo do painel visivel").toBeGreaterThanOrEqual(0);
            expect(metrics.height, "painel nao passa da janela").toBeLessThanOrEqual(
              metrics.innerHeight + 1,
            );
            if (metrics.scrollHeight > metrics.clientHeight) {
              expect(["auto", "scroll"], "painel rola quando o conteudo e maior").toContain(
                metrics.overflowY,
              );
            }

            const buttons = dialog.getByRole("button");
            const count = await buttons.count();
            expect(count).toBeGreaterThan(0);
            for (let i = 0; i < count; i++) {
              const button = buttons.nth(i);
              await button.scrollIntoViewIfNeeded();
              const box = (await button.boundingBox())!;
              expect(box.y, `botao ${i} acima da janela`).toBeGreaterThanOrEqual(-1);
              expect(box.y + box.height, `botao ${i} abaixo da janela`).toBeLessThanOrEqual(
                win.height + 1,
              );
              const onTop = await button.evaluate((el) => {
                const r = el.getBoundingClientRect();
                const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                return hit === el || el.contains(hit);
              });
              expect(onTop, `botao ${i} coberto por outro elemento`).toBe(true);
            }

            for (let i = 0; i < 12; i++) {
              await page.keyboard.press("Tab");
              expect(await inPage(page, (e) => e.insideScope()), "foco escapou do dialog").toBe(
                true,
              );
            }

            await page.keyboard.press("Escape");
            await expect(dialog).toBeHidden();
            const trigger = screen.id === "S10" ? /^Alterar avatar$/ : /^Excluir conta$/;
            expect(await inPage(page, (e) => e.activeLabel()), "foco volta ao gatilho").toMatch(
              trigger,
            );
          },
        );
      });
    }
  }
});
