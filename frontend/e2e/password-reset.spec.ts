import { settledAxe } from "./support/axe";
import { expect, test } from "@playwright/test";
import type { Browser, Page } from "@playwright/test";

// T-124 (RED): redefinicao de senha no navegador (REQ-028 a REQ-031). Usa os endpoints do host E2E (T-121):
//   GET  {API}/__test/password-reset/last-link?email=  -> 200 {"link":...} | 404
//   POST {API}/__test/clock/advance {"seconds":N}      -> 204

const API_ORIGIN = "http://127.0.0.1:5099";
const PASSWORD = "e2e-senha-segura-123";
const NEW_PASSWORD = "e2e-nova-senha-456";
const GENERIC_MESSAGE =
  "Se existir uma conta com esse email, enviaremos instruções para redefinir a senha. Confira também a caixa de spam.";
const INVALID_LINK_TITLE = "Link inválido ou expirado";
const RESET_TTL_PLUS_SECONDS = 60 * 60 + 30;

type User = { email: string; displayName: string };

function uniqueUser(label: string): User {
  const suffix = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  return {
    email: `e2e-${label}-${suffix}@example.test`,
    displayName: `e2e${label}${suffix.slice(-8)}`,
  };
}

async function expectNoAxeViolations(page: Page) {
  const results = await (await settledAxe(page)).analyze();
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
}

async function signOut(page: Page, displayName: string) {
  await page.getByRole("button", { name: `Menu da conta: ${displayName}` }).click();
  await page.getByRole("button", { name: "Sair" }).click();
  await expect(page).toHaveURL(/\/entrar/);
}

async function advanceClock(page: Page, seconds: number) {
  const response = await page.request.post(`${API_ORIGIN}/__test/clock/advance`, {
    data: { seconds },
  });
  expect(response.status()).toBe(204);
}

async function requestReset(page: Page, email: string) {
  await page.goto("/esqueci-senha");
  await page.getByLabel("Email").fill(email);
  await page.getByRole("button", { name: "Enviar instruções" }).click();
  await expect(page.getByRole("status")).toContainText(GENERIC_MESSAGE);
}

async function lastResetLink(page: Page, email: string): Promise<string> {
  const response = await page.request.get(
    `${API_ORIGIN}/__test/password-reset/last-link?email=${encodeURIComponent(email)}`,
  );
  expect(response.status(), "link de redefinicao capturado pelo host E2E").toBe(200);
  const body = (await response.json()) as { link: string };
  return body.link;
}

function tokenOf(link: string): string {
  const token = new URL(link).hash.replace(/^#token=/, "");
  expect(token.length).toBeGreaterThan(0);
  return token;
}

async function setNewPassword(page: Page, password: string) {
  await page.getByLabel("Nova senha").fill(password);
  await page.getByRole("button", { name: "Salvar nova senha" }).click();
}

async function expectInvalidLink(page: Page) {
  await expect(page.getByRole("heading", { name: INVALID_LINK_TITLE })).toBeVisible();
  await expect(
    page.getByText("Este link de redefinição não pode ser usado. Solicite um novo."),
  ).toBeVisible();
  await expect(page.getByRole("link", { name: "Solicitar novo link" })).toBeVisible();
}

async function registeredUser(page: Page, label: string): Promise<User> {
  const user = uniqueUser(label);
  await register(page, user);
  await signOut(page, user.displayName);
  return user;
}

async function sessionStatus(page: Page): Promise<number> {
  return (await page.request.get("/api/v1/auth/session")).status();
}

async function openNewContextPage(browser: Browser) {
  const context = await browser.newContext();
  return { context, page: await context.newPage() };
}

test("pedido mostra a mesma mensagem para email existente e inexistente (sem enumeracao)", async ({
  page,
}) => {
  const user = await registeredUser(page, "rpq");

  await page.goto("/esqueci-senha");
  await expect(page.getByRole("heading", { name: "Redefinir senha" })).toBeVisible();
  await expectNoAxeViolations(page);

  await requestReset(page, user.email);
  const existing = await page.getByRole("status").innerText();
  await requestReset(page, `e2e-nao-existe-${Date.now()}@example.test`);
  const unknown = await page.getByRole("status").innerText();

  expect(existing).toBe(unknown);
  expect(existing).toContain(GENERIC_MESSAGE);
});

test("abrir o link remove o token da URL e do historico; recarregar cai em link invalido (UX-11)", async ({
  page,
}) => {
  const user = await registeredUser(page, "rpt");
  await requestReset(page, user.email);
  const link = await lastResetLink(page, user.email);
  const token = tokenOf(link);

  await page.goto(link);
  await expect(page.getByRole("heading", { name: "Criar nova senha" })).toBeVisible();
  await expect(page).toHaveURL(/\/redefinir-senha$/);
  expect(page.url()).not.toContain(token);
  await expectNoAxeViolations(page);

  await page.goBack();
  await page.goForward();
  expect(page.url()).not.toContain(token);

  await page.reload();
  await expectInvalidLink(page);
});

test("senha curta nao consome o token; a senha valida depois funciona", async ({ page }) => {
  const user = await registeredUser(page, "rps");
  await requestReset(page, user.email);
  const link = await lastResetLink(page, user.email);

  await page.goto(link);
  await setNewPassword(page, "curta");
  await expect(
    page.getByText("A senha precisa ter pelo menos 8 caracteres.").first(),
  ).toBeVisible();

  await setNewPassword(page, NEW_PASSWORD);
  await expect(page).toHaveURL(/\/entrar/);
  await expect(page.getByRole("status")).toContainText("Senha alterada. Entre com a nova senha.");
});

test("nova senha valida leva a /entrar com aviso, encerra a sessao anterior e vale para o login", async ({
  page,
  browser,
}) => {
  const user = uniqueUser("rpn");
  await register(page, user);
  expect(await sessionStatus(page)).toBe(200);

  const other = await openNewContextPage(browser);
  await requestReset(other.page, user.email);
  const link = await lastResetLink(other.page, user.email);
  await other.page.goto(link);
  await setNewPassword(other.page, NEW_PASSWORD);

  await expect(other.page).toHaveURL(/\/entrar\?.*aviso=senha-alterada/);
  await expect(other.page.getByRole("status")).toContainText(
    "Senha alterada. Entre com a nova senha.",
  );
  await expectNoAxeViolations(other.page);

  expect(await sessionStatus(page), "sessao anterior encerrada").not.toBe(200);

  await other.page.getByLabel("Email").fill(user.email);
  await other.page.getByLabel("Senha", { exact: true }).fill(NEW_PASSWORD);
  await other.page.getByRole("button", { name: "Entrar" }).click();
  await expect(other.page).toHaveURL(/\/perfil$/);
  await other.context.close();
});

test("reutilizar o link ja usado mostra link invalido ou expirado (UX-03)", async ({ page }) => {
  const user = await registeredUser(page, "rpu");
  await requestReset(page, user.email);
  const link = await lastResetLink(page, user.email);

  await page.goto(link);
  await setNewPassword(page, NEW_PASSWORD);
  await expect(page).toHaveURL(/\/entrar\?.*aviso=senha-alterada/);

  await page.goto(link);
  await setNewPassword(page, "e2e-outra-senha-789");
  await expectInvalidLink(page);
  await expectNoAxeViolations(page);
});

test("link acima do prazo de 60 minutos mostra link invalido ou expirado (UX-03)", async ({
  page,
}) => {
  const user = await registeredUser(page, "rpe");
  await requestReset(page, user.email);
  const link = await lastResetLink(page, user.email);

  await advanceClock(page, RESET_TTL_PLUS_SECONDS);

  await page.goto(link);
  await setNewPassword(page, NEW_PASSWORD);
  await expectInvalidLink(page);
});

test("novo pedido invalida o link anterior", async ({ page }) => {
  const user = await registeredUser(page, "rpn2");
  await requestReset(page, user.email);
  const oldLink = await lastResetLink(page, user.email);
  await requestReset(page, user.email);
  const newLink = await lastResetLink(page, user.email);
  expect(tokenOf(newLink)).not.toBe(tokenOf(oldLink));

  await page.goto(oldLink);
  await setNewPassword(page, NEW_PASSWORD);
  await expectInvalidLink(page);

  await page.goto("/esqueci-senha");
  await page.goto(newLink);
  await setNewPassword(page, NEW_PASSWORD);
  await expect(page).toHaveURL(/\/entrar\?.*aviso=senha-alterada/);
});
