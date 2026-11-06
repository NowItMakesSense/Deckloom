import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// T-122 (RED): cadastro, login, logout e sessao no navegador (REQ-001, REQ-004, REQ-010, REQ-011).

const PASSWORD = "e2e-senha-segura-123";

function uniqueUser(label: string) {
  const suffix = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  return {
    email: `e2e-${label}-${suffix}@example.test`,
    displayName: `e2e${label}${suffix.slice(-8)}`,
  };
}

async function expectNoAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze();
  expect(results.violations).toEqual([]);
}

async function register(page: Page, user: { email: string; displayName: string }) {
  await page.goto("/cadastro");
  await expectNoAxeViolations(page);
  await page.getByLabel("Email").fill(user.email);
  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByLabel("Nome de exibição").fill(user.displayName);
  await page.getByRole("checkbox", { name: /Li e aceito/ }).check();
  await page.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }).check();
  await page.getByRole("button", { name: "Criar conta" }).click();
}

async function signOut(page: Page, displayName: string) {
  await page.getByRole("button", { name: `Menu da conta: ${displayName}` }).click();
  await page.getByRole("button", { name: "Sair" }).click();
}

test("cadastro com termos e 18+ cai em /perfil com boas-vindas e Magic (UX-06)", async ({
  page,
}) => {
  const user = uniqueUser("cad");
  await register(page, user);

  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByText("Conta criada. Bem-vindo(a) ao Deckloom!")).toBeVisible();
  await expect(page.getByText("Magic: The Gathering")).toBeVisible();
  await expectNoAxeViolations(page);
});

test("logout pelo menu leva a /entrar com aviso e Voltar nao mostra o perfil (UX-09)", async ({
  page,
}) => {
  const user = uniqueUser("out");
  await register(page, user);
  await expect(page).toHaveURL(/\/perfil$/);

  await signOut(page, user.displayName);

  await expect(page).toHaveURL(/\/entrar/);
  await expect(page.getByText("Você saiu da sua conta.")).toBeVisible();
  await expectNoAxeViolations(page);

  await page.goBack();
  await expect(page.getByText(user.email)).toHaveCount(0);
  await expect(page.getByText(user.displayName)).toHaveCount(0);
  await expect(page.getByText("Magic: The Gathering")).toHaveCount(0);
});

test("login com Lembrar-me marcado entra em /perfil", async ({ page }) => {
  const user = uniqueUser("log");
  await register(page, user);
  await expect(page).toHaveURL(/\/perfil$/);
  await signOut(page, user.displayName);
  await expect(page).toHaveURL(/\/entrar/);

  await page.getByLabel("Email").fill(user.email);
  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByRole("checkbox", { name: /Lembrar-me/ }).check();
  await page.getByRole("button", { name: "Entrar" }).click();

  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByText("Magic: The Gathering")).toBeVisible();
  const cookies = await page.context().cookies();
  expect(cookies.some((cookie) => cookie.expires > Date.now() / 1000 + 24 * 3600)).toBe(true);
});

test("rota protegida sem sessao vai a /entrar com aviso e retorno interno respeitado", async ({
  page,
}) => {
  const user = uniqueUser("ret");
  await register(page, user);
  await signOut(page, user.displayName);
  await page.goto("/perfil");

  await expect(page).toHaveURL(/\/entrar\?.*aviso=sessao-expirada/);
  await expect(page).toHaveURL(/retorno=(%2F|\/)perfil/);
  await expect(page.getByText("Sua sessão expirou. Entre novamente.")).toBeVisible();
  await expectNoAxeViolations(page);

  await page.getByLabel("Email").fill(user.email);
  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/perfil$/);
});

test("retorno externo e ignorado apos o login", async ({ page }) => {
  const user = uniqueUser("ext");
  await register(page, user);
  await signOut(page, user.displayName);

  await page.goto(`/entrar?retorno=${encodeURIComponent("https://evil.example")}`);
  await page.getByLabel("Email").fill(user.email);
  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Entrar" }).click();

  await expect(page).not.toHaveURL(/evil\.example/);
  await expect(page).toHaveURL(/127\.0\.0\.1:5173|localhost:5173/);
  await expect(page).toHaveURL(/\/perfil$/);
});
