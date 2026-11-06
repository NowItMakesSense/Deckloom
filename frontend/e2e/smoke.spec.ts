import { expect, test } from "@playwright/test";

// T-121 (RED): smoke do ambiente E2E - abrir /entrar e ver o <h1> da tela de login.
test("abre /entrar e mostra o h1", async ({ page }) => {
  await page.goto("/entrar");

  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
});
