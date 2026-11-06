import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// T-126 (RED): exclusao de conta no navegador - conta com senha e conta Google (REQ-023 a REQ-027, REQ-042, UX-08, UX-14).

const API_ORIGIN = "http://127.0.0.1:5099";
const PASSWORD = "e2e-senha-segura-123";
const WRONG_PASSWORD = "e2e-senha-errada-999";
const DELETED_NOTICE = "Sua conta foi excluída.";
const WRONG_PASSWORD_MESSAGE = "Senha incorreta. Sua conta não foi excluída.";
const INVALID_CREDENTIALS = "Email ou senha inválidos.";
const CONFIRMATION_EXPIRED =
  "Sua confirmação expirou. Reautentique com o Google para excluir a conta.";
const WITHIN_WINDOW_SECONDS = 60;
const PAST_WINDOW_SECONDS = 6 * 60;

type User = { email: string; displayName: string };

function uniqueSuffix() {
  return `${Date.now()}${Math.floor(Math.random() * 1000)}`;
}

function uniqueUser(label: string): User {
  const suffix = uniqueSuffix();
  return {
    email: `e2e-${label}-${suffix}@example.test`,
    displayName: `e2e${label}${suffix.slice(-8)}`,
  };
}

async function expectNoAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze();
  expect(results.violations).toEqual([]);
}

async function register(page: Page, user: User) {
  await page.goto("/cadastro");
  await page.getByLabel("Email").fill(user.email);
  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByLabel("Nome de exibição").fill(user.displayName);
  await page.getByRole("checkbox", { name: /Li e aceito/ }).check();
  await page.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }).check();
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByRole("heading", { name: "Meu perfil" })).toBeVisible();
}

async function registerWithGoogle(page: Page, displayName: string, email: string, subject: string) {
  await setGoogleIdentity(page, subject, email);
  await page.goto("/entrar");
  await page.getByRole("button", { name: "Continuar com Google" }).click();
  await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
  await page.getByLabel("Nome de exibição").fill(displayName);
  await page.getByRole("checkbox", { name: /Li e aceito/ }).check();
  await page.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }).check();
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page).toHaveURL(/\/perfil$/);
}

async function setGoogleIdentity(page: Page, subject: string, email: string) {
  const response = await page.request.post(`${API_ORIGIN}/__test/google/identity`, {
    data: { subject, email, name: "E2E Google", emailVerified: true, outcome: "approve" },
  });
  expect(response.status(), "stub Google: POST /__test/google/identity").toBe(204);
}

async function advanceClock(page: Page, seconds: number) {
  const response = await page.request.post(`${API_ORIGIN}/__test/clock/advance`, {
    data: { seconds },
  });
  expect(response.status()).toBe(204);
}

// Contrato (GREEN de T-126): POST /__test/clock/reset, sem corpo, 204, volta o relogio ao ponto inicial do host.
test.beforeEach(async ({ page }) => {
  const response = await page.request.post(`${API_ORIGIN}/__test/clock/reset`);
  expect(response.status(), "POST /__test/clock/reset").toBe(204);
});

async function openPasswordDialog(page: Page) {
  await page.getByRole("button", { name: "Excluir conta" }).click();
  const dialog = page.getByRole("alertdialog", { name: "Excluir conta" });
  await expect(dialog).toBeVisible();
  return dialog;
}

async function submitPasswordDeletion(page: Page, password: string) {
  const dialog = await openPasswordDialog(page);
  await dialog.getByRole("checkbox", { name: "Entendo que a exclusão é definitiva." }).check();
  await dialog.getByLabel("Senha", { exact: true }).fill(password);
  await dialog.getByRole("button", { name: "Excluir minha conta definitivamente" }).click();
}

async function reauthenticateWithGoogle(page: Page) {
  const dialog = await openPasswordDialog(page);
  await dialog.getByRole("checkbox", { name: "Entendo que a exclusão é definitiva." }).check();
  await dialog.getByRole("button", { name: "Reautenticar com o Google" }).click();
  await expect(page).toHaveURL(/\/perfil\?exclusao=confirmar|\/perfil$/);
  const confirm = page.getByRole("alertdialog", { name: "Confirmar exclusão" });
  await expect(confirm).toBeVisible();
  return confirm;
}

async function loginWith(page: Page, email: string, password: string) {
  await page.goto("/entrar");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Entrar" }).click();
}

test("senha errada na exclusao mantem a conta e a sessao (REQ-026)", async ({ page }) => {
  const user = uniqueUser("delwrong");
  await register(page, user);

  await submitPasswordDeletion(page, WRONG_PASSWORD);

  const dialog = page.getByRole("alertdialog", { name: "Excluir conta" });
  await expect(dialog.getByText(WRONG_PASSWORD_MESSAGE)).toBeVisible();
  await expect(page).toHaveURL(/\/perfil$/);
  await expectNoAxeViolations(page);

  await dialog.getByRole("button", { name: "Cancelar" }).click();
  await expect(dialog).toBeHidden();
  await page.reload();
  await expect(page).toHaveURL(/\/perfil$/);
  await expect(
    page.getByRole("button", { name: `Menu da conta: ${user.displayName}` }),
  ).toBeVisible();
});

test("senha certa exclui a conta e leva a /entrar com o aviso; login antigo falha com mensagem generica (REQ-023, REQ-027)", async ({
  page,
}) => {
  const user = uniqueUser("deldel");
  await register(page, user);
  const dialog = await openPasswordDialog(page);
  await expectNoAxeViolations(page);
  await dialog.getByRole("button", { name: "Cancelar" }).click();

  await submitPasswordDeletion(page, PASSWORD);

  await expect(page).toHaveURL(/\/entrar\?.*aviso=conta-excluida/);
  await expect(page.getByText(DELETED_NOTICE)).toBeVisible();
  await expectNoAxeViolations(page);

  await page.goto("/perfil");
  await expect(page).toHaveURL(/\/entrar\?.*aviso=sessao-expirada/);

  await loginWith(page, user.email, PASSWORD);
  await expect(page.getByText(INVALID_CREDENTIALS)).toBeVisible();
  await expect(page).toHaveURL(/\/entrar/);
});

test("novo cadastro com o mesmo email apos a exclusao cria perfil vazio (REQ-024, REQ-025)", async ({
  page,
}) => {
  const user = uniqueUser("delre");
  await register(page, user);
  await page.getByRole("button", { name: "Editar perfil" }).click();
  await page.getByLabel("Bio").fill("Bio que deve sumir com a conta.");
  await page.getByRole("button", { name: "Salvar alterações" }).click();
  await expect(page.getByText("Bio que deve sumir com a conta.")).toBeVisible();

  await submitPasswordDeletion(page, PASSWORD);
  await expect(page).toHaveURL(/\/entrar\?.*aviso=conta-excluida/);

  const reborn = { email: user.email, displayName: `${user.displayName}n` };
  await register(page, reborn);

  await expect(page.getByText("Bio que deve sumir com a conta.")).toHaveCount(0);
  await expect(page.getByText("Nenhuma bio.")).toBeVisible();
  await expect(page.getByText("Nenhum formato selecionado.")).toBeVisible();
  await expect(page.locator("main .profile__name")).toHaveText(reborn.displayName);
});

test("Voltar apos excluir nao mostra dados do perfil (UX-08)", async ({ page }) => {
  const user = uniqueUser("delback");
  await register(page, user);

  await submitPasswordDeletion(page, PASSWORD);
  await expect(page).toHaveURL(/\/entrar\?.*aviso=conta-excluida/);

  await page.goBack();
  await expect(page.getByText(user.email)).toHaveCount(0);
  await expect(page.getByText(user.displayName)).toHaveCount(0);
  await expect(page.getByText("Magic: The Gathering")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Meu perfil" })).toHaveCount(0);
});

test("conta Google: reautenticar e confirmar em ate 2 min exclui a conta (REQ-042)", async ({
  page,
}) => {
  const suffix = uniqueSuffix();
  const email = `e2e-delg-${suffix}@example.test`;
  const subject = `e2e-sub-delg-${suffix}`;
  const displayName = `e2edelg${suffix.slice(-8)}`;
  await registerWithGoogle(page, displayName, email, subject);

  await setGoogleIdentity(page, subject, email);
  const first = await openPasswordDialog(page);
  await expectNoAxeViolations(page);
  await first.getByRole("button", { name: "Cancelar" }).click();

  const confirm = await reauthenticateWithGoogle(page);
  await expectNoAxeViolations(page);
  await advanceClock(page, WITHIN_WINDOW_SECONDS);
  await confirm.getByRole("button", { name: "Excluir minha conta definitivamente" }).click();

  await expect(page).toHaveURL(/\/entrar\?.*aviso=conta-excluida/);
  await expect(page.getByText(DELETED_NOTICE)).toBeVisible();

  // A conta Google deixou de existir: entrar de novo cai no cadastro (S4), nao em /perfil.
  await setGoogleIdentity(page, subject, email);
  await page.getByRole("button", { name: "Continuar com Google" }).click();
  await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
});

test("conta Google: confirmar apos 6 min mostra confirmacao expirada e a conta continua (UX-14, REQ-042)", async ({
  page,
}) => {
  const suffix = uniqueSuffix();
  const email = `e2e-delx-${suffix}@example.test`;
  const subject = `e2e-sub-delx-${suffix}`;
  const displayName = `e2edelx${suffix.slice(-8)}`;
  await registerWithGoogle(page, displayName, email, subject);

  await setGoogleIdentity(page, subject, email);
  const confirm = await reauthenticateWithGoogle(page);
  await advanceClock(page, PAST_WINDOW_SECONDS);
  await confirm.getByRole("button", { name: "Excluir minha conta definitivamente" }).click();

  await expect(page.getByText(CONFIRMATION_EXPIRED)).toBeVisible();
  const first = page.getByRole("alertdialog", { name: "Excluir conta" });
  await expect(first).toBeVisible();
  await expect(first.getByRole("button", { name: "Reautenticar com o Google" })).toBeVisible();
  await expectNoAxeViolations(page);

  await first.getByRole("button", { name: "Cancelar" }).click();
  await page.reload();
  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByRole("button", { name: `Menu da conta: ${displayName}` })).toBeVisible();
});

// T-168 / U-05: sem rolagem horizontal com texto a 200%; U-02/U-04: a caixa e descrita pelo aviso no instante do foco.
const VIEWPORT_WIDTHS = [320, 768, 1280];

async function setDocumentFontSize(page: Page, percent: number) {
  await page.evaluate((value) => {
    document.documentElement.style.fontSize = `${value}%`;
  }, percent);
}

for (const width of VIEWPORT_WIDTHS) {
  test(`S11 Google com aviso de confirmacao expirada, a ${width}px e texto a 200%, nao rola na horizontal (U-05)`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 800 });
    const suffix = uniqueSuffix();
    const email = `e2e-delw-${suffix}@example.test`;
    const subject = `e2e-sub-delw-${suffix}`;
    await registerWithGoogle(page, `e2edelw${suffix.slice(-8)}`, email, subject);
    await setGoogleIdentity(page, subject, email);
    const confirm = await reauthenticateWithGoogle(page);
    await advanceClock(page, PAST_WINDOW_SECONDS);

    await setDocumentFontSize(page, 200);
    await confirm.getByRole("button", { name: "Excluir minha conta definitivamente" }).click();
    await expect(page.getByText(CONFIRMATION_EXPIRED)).toBeVisible();

    const metrics = await page.locator(".dialog__panel").evaluate((panel) => ({
      scrollWidth: panel.scrollWidth,
      clientWidth: panel.clientWidth,
    }));
    expect(
      metrics.scrollWidth,
      `scrollWidth ${metrics.scrollWidth} > clientWidth ${metrics.clientWidth} a ${width}px`,
    ).toBeLessThanOrEqual(metrics.clientWidth);
  });
}

test("S2 com aviso de sessao expirada a 320px e texto a 200% nao rola na horizontal (U-05)", async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 800 });
  await page.goto("/entrar?aviso=sessao-expirada");
  await setDocumentFontSize(page, 200);
  await expect(page.getByRole("alert")).toBeVisible();
  const metrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(
    metrics.scrollWidth,
    `scrollWidth ${metrics.scrollWidth} > clientWidth ${metrics.clientWidth}`,
  ).toBeLessThanOrEqual(metrics.clientWidth);
});

// T-171 / U-09: o cabecalho (visitante e com sessao) reflui a 320px com texto a 200%, sem rolagem horizontal.
async function expectNoHorizontalScroll(page: Page, context: string) {
  const metrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(
    metrics.scrollWidth,
    `${context}: scrollWidth ${metrics.scrollWidth} > clientWidth ${metrics.clientWidth}`,
  ).toBeLessThanOrEqual(metrics.clientWidth);
  const outside = await page
    .locator("header.app-header a, header.app-header button")
    .evaluateAll((els) =>
      els
        .map((el) => ({
          text: (el.textContent ?? "").trim(),
          right: el.getBoundingClientRect().right,
        }))
        .filter((e) => e.right > document.documentElement.clientWidth + 0.5),
    );
  expect(outside, `${context}: controles do cabecalho fora da largura da tela`).toEqual([]);
}

for (const route of ["/entrar", "/entrar?aviso=sessao-expirada", "/cadastro"]) {
  test(`cabecalho de visitante em ${route} a 320px e texto a 200% reflui sem rolagem horizontal (U-09)`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    await page.goto(route);
    await setDocumentFontSize(page, 200);
    await expect(page.getByRole("link", { name: "Criar conta" }).first()).toBeVisible();
    await expectNoHorizontalScroll(page, route);
  });
}

for (const width of [320, 500]) {
  test(`cabecalho com sessao e nome longo a ${width}px e texto a 200% reflui, com o menu aberto tambem (U-09)`, async ({
    page,
  }) => {
    const user = {
      ...uniqueUser("hdr"),
      displayName: `Wisniewska-Kowalczyk ${uniqueSuffix().slice(-8)}`,
    };
    await register(page, user);
    await page.setViewportSize({ width, height: 800 });
    await setDocumentFontSize(page, 200);
    const trigger = page.getByRole("button", { name: `Menu da conta: ${user.displayName}` });
    await expect(trigger).toBeVisible();
    await expectNoHorizontalScroll(page, `sessao ${width}px, menu fechado`);
    await trigger.click();
    await expect(page.getByRole("button", { name: "Sair" })).toBeVisible();
    await expectNoHorizontalScroll(page, `sessao ${width}px, menu aberto`);
    // O painel nao pode ser cortado: nem a direita (scrollWidth) nem a esquerda (que o scrollWidth nao ve).
    const panel = await page.locator(".account-menu__panel").evaluate((el) => {
      const r = el.getBoundingClientRect();
      return { left: r.left, right: r.right, clientWidth: document.documentElement.clientWidth };
    });
    expect(panel.left, `painel do menu a ${width}px cortado a esquerda`).toBeGreaterThanOrEqual(0);
    expect(panel.right, `painel do menu a ${width}px cortado a direita`).toBeLessThanOrEqual(
      panel.clientWidth,
    );
    await expect(page.getByRole("button", { name: "Sair" })).toBeInViewport({ ratio: 1 });
    await page.screenshot({ path: `test-results/header-sessao-${width}-200.png` });
  });
}

test("S11 Google: no instante do foco a caixa e descrita pela regiao com o texto do aviso (U-02, U-04)", async ({
  page,
}) => {
  const suffix = uniqueSuffix();
  const email = `e2e-deld-${suffix}@example.test`;
  const subject = `e2e-sub-deld-${suffix}`;
  await registerWithGoogle(page, `e2edeld${suffix.slice(-8)}`, email, subject);
  await setGoogleIdentity(page, subject, email);
  const confirm = await reauthenticateWithGoogle(page);
  await advanceClock(page, PAST_WINDOW_SECONDS);

  // Registra, no proprio instante do foco, o que a caixa aponta via aria-describedby.
  await page.evaluate(() => {
    const w = window as unknown as { __focusRecords: unknown[] };
    w.__focusRecords = [];
    document.addEventListener(
      "focusin",
      (event) => {
        const target = event.target as HTMLElement;
        if (target.getAttribute("type") !== "checkbox") return;
        const ids = (target.getAttribute("aria-describedby") ?? "").split(/\s+/).filter(Boolean);
        w.__focusRecords.push(
          ids.map((id) => {
            const el = document.getElementById(id);
            return { role: el?.getAttribute("role") ?? null, text: el?.textContent ?? null };
          }),
        );
      },
      true,
    );
  });
  await confirm.getByRole("button", { name: "Excluir minha conta definitivamente" }).click();
  await expect(page.getByText(CONFIRMATION_EXPIRED)).toBeVisible();

  const first = page.getByRole("alertdialog", { name: "Excluir conta" });
  const box = first.getByRole("checkbox", { name: "Entendo que a exclusão é definitiva." });
  await expect(box).toBeFocused();

  const records = await page.evaluate(
    () =>
      (window as unknown as { __focusRecords: { role: string | null; text: string | null }[][] })
        .__focusRecords,
  );
  const atFocus = records.at(-1) ?? [];
  expect(atFocus.length, "aria-describedby vazio no instante do foco").toBeGreaterThan(0);
  const region = atFocus.find((r) => r.role === "alert");
  expect(region?.text, "regiao role=alert sem o texto do aviso no instante do foco").toContain(
    CONFIRMATION_EXPIRED,
  );

  // Arvore de acessibilidade do Chromium: a descricao calculada da caixa traz o aviso.
  const cdp = await page.context().newCDPSession(page);
  const { nodes } = (await cdp.send("Accessibility.getFullAXTree")) as {
    nodes: {
      role?: { value: string };
      name?: { value: string };
      description?: { value: string };
    }[];
  };
  const checkbox = nodes.find(
    (n) => n.role?.value === "checkbox" && n.name?.value === "Entendo que a exclusão é definitiva.",
  );
  expect(checkbox, "caixa na arvore de acessibilidade").toBeDefined();
  expect(checkbox?.description?.value ?? "").toContain(CONFIRMATION_EXPIRED);
});
