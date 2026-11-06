import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";

// T-123 (RED): fluxos do Google no navegador - S3, S4, S5 e pendencia expirada (REQ-006 a REQ-009, REQ-036, REQ-039).
//
// Contrato do stub Google para fluxo de navegador (a implementar pelo GREEN de T-123):
//   POST {API}/__test/clock/advance {"seconds":N}             -> 204 (ja existe, T-121)
//   POST {API}/__test/google/identity                          -> 204
//        {"subject","email","name"?,"emailVerified"?,"outcome"?:"approve"|"deny"}
//        define a identidade devolvida pelas proximas autorizacoes (persiste ate nova chamada).
//        emailVerified padrao true; outcome padrao "approve"; name ausente => id_token sem claim "name".
//   GET  {StubAuthority}/authorize?client_id&redirect_uri&state&nonce&code_challenge&code_challenge_method&response_type=code&scope
//        approve -> 302 para redirect_uri?code=<codigo unico>&state=<state recebido>
//        deny    -> 302 para redirect_uri?error=access_denied&state=<state recebido>
//        o codigo fica ligado ao nonce recebido e a identidade vigente, e o /token emite o id_token
//        com sub/email/name/email_verified dessa identidade e o nonce desse /authorize.

const API_ORIGIN = "http://127.0.0.1:5099";
const PASSWORD = "e2e-senha-segura-123";
const GOOGLE_FAILED_MESSAGE =
  "Não foi possível concluir o login com Google. Tente de novo ou entre com email e senha.";
const PENDING_TTL_PLUS_SECONDS = 10 * 60 + 30;

interface GoogleIdentity {
  subject: string;
  email: string;
  name?: string;
  emailVerified?: boolean;
  outcome?: "approve" | "deny";
}

function uniqueSuffix() {
  return `${Date.now()}${Math.floor(Math.random() * 1000)}`;
}

function uniqueGoogle(label: string, overrides: Partial<GoogleIdentity> = {}): GoogleIdentity {
  const suffix = uniqueSuffix();
  return {
    subject: `e2e-sub-${label}-${suffix}`,
    email: `e2e-${label}-${suffix}@example.test`,
    name: `E2E Google ${suffix.slice(-8)}`,
    emailVerified: true,
    outcome: "approve",
    ...overrides,
  };
}

async function expectNoAxeViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze();
  expect(results.violations).toEqual([]);
}

async function setGoogleIdentity(page: Page, identity: GoogleIdentity) {
  const response = await page.request.post(`${API_ORIGIN}/__test/google/identity`, {
    data: identity,
  });
  expect(response.status(), "stub Google: POST /__test/google/identity").toBe(204);
}

async function advanceClock(page: Page, seconds: number) {
  const response = await page.request.post(`${API_ORIGIN}/__test/clock/advance`, {
    data: { seconds },
  });
  expect(response.status()).toBe(204);
}

async function continueWithGoogle(page: Page) {
  await page.goto("/entrar");
  await page.getByRole("button", { name: "Continuar com Google" }).click();
}

async function acceptSignupTerms(page: Page) {
  await page.getByRole("checkbox", { name: /Li e aceito/ }).check();
  await page.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }).check();
}

async function signOut(page: Page, displayName: string) {
  await page.getByRole("button", { name: `Menu da conta: ${displayName}` }).click();
  await page.getByRole("button", { name: "Sair" }).click();
  await expect(page).toHaveURL(/\/entrar/);
}

async function registerWithPassword(page: Page, email: string, displayName: string) {
  await page.goto("/cadastro");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByLabel("Nome de exibição").fill(displayName);
  await page.getByRole("checkbox", { name: /Li e aceito/ }).check();
  await page.getByRole("checkbox", { name: "Declaro ter 18 anos ou mais." }).check();
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page).toHaveURL(/\/perfil$/);
}

test("email novo no Google abre a S4 com o nome editavel e so cria a conta apos nome, termos e 18+ (REQ-006, REQ-039)", async ({
  page,
}) => {
  const google = uniqueGoogle("novo");
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);

  await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
  await expect(page.getByRole("heading", { name: "Quase lá" })).toBeVisible();
  await expect(page.getByText(google.email)).toBeVisible();
  const nameField = page.getByLabel("Nome de exibição");
  await expect(nameField).toHaveValue(google.name!);
  await expectNoAxeViolations(page);

  // Sem termos e 18+ a conta nao e criada: continua na S4 e nao ha sessao.
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
  const termsError = "Aceite os Termos de Uso e a Política de Privacidade para criar a conta.";
  const adultError = "Confirme que você tem 18 anos ou mais para criar a conta.";
  // Duplicacao intencional: link no ErrorSummary + erro inline no formulario.
  await expect(page.getByRole("link", { name: termsError })).toBeVisible();
  await expect(page.getByRole("link", { name: adultError })).toBeVisible();
  await expect(page.locator("form").getByText(termsError)).toBeVisible();
  await expect(page.locator("form").getByText(adultError)).toBeVisible();
  await page.goto("/perfil");
  await expect(page).toHaveURL(/\/entrar\?.*aviso=sessao-expirada/);

  // Volta pelo Google (a pendencia vale 10 min), edita o nome e conclui.
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);
  await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
  const edited = `e2eg${uniqueSuffix().slice(-10)}`;
  await page.getByLabel("Nome de exibição").fill(edited);
  await acceptSignupTerms(page);
  await page.getByRole("button", { name: "Criar conta" }).click();

  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByText("Conta criada. Bem-vindo(a) ao Deckloom!")).toBeVisible();
  await expect(page.getByRole("button", { name: `Menu da conta: ${edited}` })).toBeVisible();
  await expectNoAxeViolations(page);
});

test("conta Google existente entra direto em /perfil, sem passar pela S4 (REQ-007)", async ({
  page,
}) => {
  const google = uniqueGoogle("exist");
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);
  await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
  const displayName = `e2eg${uniqueSuffix().slice(-10)}`;
  await page.getByLabel("Nome de exibição").fill(displayName);
  await acceptSignupTerms(page);
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page).toHaveURL(/\/perfil$/);
  await signOut(page, displayName);

  const visited: string[] = [];
  page.on("framenavigated", (frame) => {
    if (frame === page.mainFrame()) visited.push(frame.url());
  });
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);

  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByRole("button", { name: `Menu da conta: ${displayName}` })).toBeVisible();
  expect(visited.some((url) => url.includes("/entrar/google/concluir"))).toBe(false);
  expect(visited.some((url) => url.includes("/entrar/google/vincular"))).toBe(false);
});

test("email de conta com senha abre a S5 e a senha correta vincula e entra (REQ-008, REQ-036)", async ({
  page,
}) => {
  const google = uniqueGoogle("vinc");
  const displayName = `e2ev${uniqueSuffix().slice(-10)}`;
  await registerWithPassword(page, google.email, displayName);
  await signOut(page, displayName);

  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);

  await expect(page).toHaveURL(/\/entrar\/google\/vincular$/);
  await expect(page.getByRole("heading", { name: "Vincular sua conta Google" })).toBeVisible();
  await expect(page.getByText(google.email)).toBeVisible();
  await expectNoAxeViolations(page);

  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Vincular e entrar" }).click();

  await expect(page).toHaveURL(/\/perfil$/);
  await expect(
    page.getByText("Google vinculado. Da próxima vez você pode entrar com o Google."),
  ).toBeVisible();

  // Depois do vinculo o Google entra direto.
  await signOut(page, displayName);
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);
  await expect(page).toHaveURL(/\/perfil$/);
});

test("senha errada na S5 nao vincula o Google nem abre sessao (REQ-036)", async ({ page }) => {
  const google = uniqueGoogle("errada");
  const displayName = `e2ee${uniqueSuffix().slice(-10)}`;
  await registerWithPassword(page, google.email, displayName);
  await signOut(page, displayName);

  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);
  await expect(page).toHaveURL(/\/entrar\/google\/vincular$/);

  await page.getByLabel("Senha", { exact: true }).fill("e2e-senha-errada-999");
  await page.getByRole("button", { name: "Vincular e entrar" }).click();

  await expect(page.getByText("Senha incorreta. O Google não foi vinculado.")).toBeVisible();
  await expect(page).toHaveURL(/\/entrar\/google\/vincular$/);
  await expectNoAxeViolations(page);

  // Nenhuma sessao: rota protegida volta ao login.
  await page.goto("/perfil");
  await expect(page).toHaveURL(/\/entrar\?.*aviso=sessao-expirada/);

  // Nenhum vinculo: novo retorno do Google ainda pede a senha (S5), nao entra direto.
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);
  await expect(page).toHaveURL(/\/entrar\/google\/vincular$/);
});

test("cancelar no Google volta a /entrar com a mensagem unica e sem sessao (REQ-009)", async ({
  page,
}) => {
  const google = uniqueGoogle("cancel", { outcome: "deny" });
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);

  await expect(page).toHaveURL(/\/entrar\?.*aviso=google-falhou/);
  await expect(page.getByText(GOOGLE_FAILED_MESSAGE)).toBeVisible();
  await expectNoAxeViolations(page);

  await page.goto("/perfil");
  await expect(page).toHaveURL(/\/entrar\?.*aviso=sessao-expirada/);
});

test("email nao verificado no Google volta a /entrar com a mensagem unica e nao cria conta (REQ-009)", async ({
  page,
}) => {
  const google = uniqueGoogle("nover", { emailVerified: false });
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);

  await expect(page).toHaveURL(/\/entrar\?.*aviso=google-falhou/);
  await expect(page.getByText(GOOGLE_FAILED_MESSAGE)).toBeVisible();
  await expectNoAxeViolations(page);
  await expect(page.getByText(google.email)).toHaveCount(0);
});

test("pendencia de cadastro Google expirada (> 10 min) volta ao login ao concluir a S4 (REQ-039)", async ({
  page,
}) => {
  const google = uniqueGoogle("exp4");
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);
  await expect(page).toHaveURL(/\/entrar\/google\/concluir$/);
  // QA-02: a S4 le a pendencia (GET) ao montar; o relogio so avanca depois de o formulario aparecer,
  // senao a leitura ja chega expirada e a tela volta ao login antes de o teste preencher o campo.
  await expect(page.getByLabel("Nome de exibição")).toBeVisible();

  await advanceClock(page, PENDING_TTL_PLUS_SECONDS);

  await page.getByLabel("Nome de exibição").fill(`e2eg${uniqueSuffix().slice(-10)}`);
  await acceptSignupTerms(page);
  await page.getByRole("button", { name: "Criar conta" }).click();

  await expect(page).toHaveURL(/\/entrar\?.*aviso=google-falhou/);
  await expect(page.getByText(GOOGLE_FAILED_MESSAGE)).toBeVisible();
  await page.goto("/perfil");
  await expect(page).toHaveURL(/\/entrar\?.*aviso=sessao-expirada/);
});

test("pendencia de vinculo Google expirada (> 10 min) volta ao login ao enviar a senha na S5 (REQ-008)", async ({
  page,
}) => {
  const google = uniqueGoogle("exp5");
  const displayName = `e2ex${uniqueSuffix().slice(-10)}`;
  await registerWithPassword(page, google.email, displayName);
  await signOut(page, displayName);
  await setGoogleIdentity(page, google);
  await continueWithGoogle(page);
  await expect(page).toHaveURL(/\/entrar\/google\/vincular$/);
  // QA-02: mesma espera da S4 - o relogio so avanca com a S5 pronta.
  await expect(page.getByLabel("Senha", { exact: true })).toBeVisible();

  await advanceClock(page, PENDING_TTL_PLUS_SECONDS);

  await page.getByLabel("Senha", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Vincular e entrar" }).click();

  await expect(page).toHaveURL(/\/entrar\?.*aviso=google-falhou/);
  await expect(page.getByText(GOOGLE_FAILED_MESSAGE)).toBeVisible();
});
