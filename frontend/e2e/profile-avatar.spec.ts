import { readFileSync } from "node:fs";
import { randomBytes } from "node:crypto";
import { resolve } from "node:path";
import { deflateSync } from "node:zlib";
import { settledAxe } from "./support/axe";
import { expect, test } from "@playwright/test";
import type { Locator, Page } from "@playwright/test";

// T-125 (RED): perfil e avatar no navegador, com o SkiaSharp real do host E2E (REQ-014, REQ-018, REQ-019, REQ-020,
// REQ-049). Fixtures: as imagens pequenas sao reaproveitadas por caminho dos testes do processador (T-070 a T-072);
// as grandes (4096x4096, 4097x4096, > 2 MB) sao geradas aqui, em tempo de teste, como PNG sintetico (sem binarios).

const PASSWORD = "e2e-senha-segura-123";
const REJECTED =
  "Não foi possível usar este arquivo. Envie uma imagem JPEG, PNG ou WebP de até 2 MB.";
const BAD_TYPE = "Escolha uma imagem JPEG, PNG ou WebP.";
const TOO_BIG = "A imagem precisa ter no máximo 2 MB.";
const FIXTURES = resolve(
  import.meta.dirname,
  "../../backend/tests/Deckloom.Accounts.Infrastructure.Tests/Fixtures",
);

type User = { email: string; displayName: string };
type Upload = { name: string; mimeType: string; buffer: Buffer };
type Rejection = Upload & { message: string };

function uniqueUser(label: string): User {
  const suffix = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  return {
    email: `e2e-${label}-${suffix}@example.test`,
    displayName: `e2e${label}${suffix.slice(-8)}`,
  };
}

function fixture(name: string): Buffer {
  return readFileSync(resolve(FIXTURES, name));
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(data: Buffer): number {
  let c = 0xffffffff;
  for (const byte of data) c = CRC_TABLE[(c ^ byte) & 0xff]! ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type: string, data: Buffer): Buffer {
  const body = Buffer.concat([Buffer.from(type, "latin1"), data]);
  const head = Buffer.alloc(4);
  head.writeUInt32BE(data.length);
  const tail = Buffer.alloc(4);
  tail.writeUInt32BE(crc32(body));
  return Buffer.concat([head, body, tail]);
}

/** PNG sintetico. `random` gera pixels incompressiveis (para passar de 2 MB); senao, cinza liso. */
function syntheticPng(width: number, height: number, channels: 1 | 3, random = false): Buffer {
  const stride = width * channels + 1;
  const raw = random ? randomBytes(stride * height) : Buffer.alloc(stride * height);
  if (random) for (let row = 0; row < height; row++) raw[row * stride] = 0;
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = channels === 1 ? 0 : 2;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw, { level: random ? 0 : 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
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
  await expect(page.getByRole("heading", { name: "Meu perfil" })).toBeVisible();
}

const profileAvatar = (page: Page): Locator => page.locator("main img.avatar");
const headerAvatar = (page: Page): Locator =>
  page.getByRole("button", { name: /^Menu da conta:/ }).locator("img");

async function srcOf(locator: Locator): Promise<string> {
  await expect(locator).toBeVisible();
  return (await locator.getAttribute("src")) ?? "";
}

async function openAvatarDialog(page: Page) {
  await page.getByRole("button", { name: "Alterar avatar" }).click();
  await expect(page.getByRole("dialog", { name: "Alterar avatar" })).toBeVisible();
}

async function uploadFile(page: Page, file: Upload) {
  await page.getByLabel("Arquivo").setInputFiles(file);
  await page.getByRole("button", { name: "Enviar imagem" }).click();
}

async function expectRejectedKeepingAvatar(page: Page, file: Rejection) {
  const dialog = page.getByRole("dialog", { name: "Alterar avatar" });
  const beforeProfile = await srcOf(profileAvatar(page));
  const beforeHeader = await srcOf(headerAvatar(page));
  await uploadFile(page, file);
  await expect(dialog.getByText(file.message, { exact: true })).toBeVisible();
  await expect(dialog).toBeVisible();
  await dialog.getByRole("button", { name: "Fechar" }).click();
  await expect(dialog).toBeHidden();
  expect(await srcOf(profileAvatar(page))).toBe(beforeProfile);
  expect(await srcOf(headerAvatar(page))).toBe(beforeHeader);
}

async function expectServedWebpWithoutExif(page: Page, src: string) {
  const response = await page.request.get(new URL(src, page.url()).toString());
  expect(response.status()).toBe(200);
  expect(response.headers()["content-type"]).toContain("image/webp");
  const text = (await response.body()).toString("latin1");
  expect(text.startsWith("RIFF")).toBe(true);
  expect(text.slice(8, 12)).toBe("WEBP");
  expect(text).not.toContain("EXIF");
  expect(text).not.toContain("Exif");
  expect(text).not.toContain("GPS");
}

test("editar nome, bio e formatos mostra o perfil atualizado (S9 -> S8)", async ({ page }) => {
  const user = uniqueUser("edit");
  await register(page, user);
  await page.getByRole("button", { name: "Editar perfil" }).click();
  await expect(page).toHaveURL(/\/perfil\/editar$/);
  await expectNoAxeViolations(page);

  const newName = `${user.displayName}x`;
  await page.getByLabel("Nome de exibição").fill(newName);
  await page.getByLabel("Bio").fill("Jogador de Commander desde 2015.");
  const group = page.getByRole("group", { name: "Formatos preferidos" });
  const format = group.getByRole("checkbox").first();
  await expect(format).toBeVisible();
  await format.check();
  const label = await format.evaluate((el) => (el as HTMLInputElement).labels?.[0]?.textContent);
  const formatName = (label ?? "").trim();
  expect(formatName.length).toBeGreaterThan(0);
  await page.getByRole("button", { name: "Salvar alterações" }).click();

  await expect(page).toHaveURL(/\/perfil$/);
  await expect(page.getByText("Perfil atualizado.")).toBeVisible();
  await expect(page.locator("main .profile__name")).toHaveText(newName);
  await expect(page.getByText("Jogador de Commander desde 2015.")).toBeVisible();
  await expect(page.getByText(formatName, { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: `Menu da conta: ${newName}` })).toBeVisible();
  await expectNoAxeViolations(page);
});

test("escolher um preset muda o avatar no perfil e no header", async ({ page }) => {
  await register(page, uniqueUser("preset"));
  const before = await srcOf(profileAvatar(page));
  await openAvatarDialog(page);
  await expectNoAxeViolations(page);

  const dialog = page.getByRole("dialog");
  await dialog.getByRole("radio").and(page.locator(":not(:checked)")).first().check();
  await page.getByRole("button", { name: "Usar este avatar" }).click();

  await expect(dialog).toBeHidden();
  await expect(page.getByText("Avatar atualizado.")).toBeVisible();
  await expect.poll(() => profileAvatar(page).getAttribute("src")).not.toBe(before);
  expect(await srcOf(headerAvatar(page))).toBe(await srcOf(profileAvatar(page)));
});

test("upload de JPEG com EXIF atualiza perfil e header sem recarregar; servido como webp sem EXIF (UX-07)", async ({
  page,
}) => {
  await register(page, uniqueUser("jpeg"));
  const before = await srcOf(profileAvatar(page));
  await page.evaluate(() => {
    (window as unknown as { __noReload: boolean }).__noReload = true;
  });
  await openAvatarDialog(page);
  await uploadFile(page, {
    name: "foto.jpg",
    mimeType: "image/jpeg",
    buffer: fixture("exif-gps-orientation6.jpg"),
  });

  await expect(page.getByRole("dialog")).toBeHidden();
  await expect(page.getByText("Avatar atualizado.")).toBeVisible();
  await expect.poll(() => profileAvatar(page).getAttribute("src")).not.toBe(before);
  const src = await srcOf(profileAvatar(page));
  expect(await srcOf(headerAvatar(page))).toBe(src);
  const marker = await page.evaluate(
    () => (window as unknown as { __noReload?: boolean }).__noReload,
  );
  expect(marker).toBe(true);
  await expectServedWebpWithoutExif(page, src);
  await expectNoAxeViolations(page);
});

test("arquivos rejeitados mostram a mesma mensagem e mantem o avatar anterior", async ({
  page,
}) => {
  test.setTimeout(120_000);
  await register(page, uniqueUser("rej"));
  await openAvatarDialog(page);

  const rejected: Rejection[] = [
    {
      name: "setup.exe",
      mimeType: "application/x-msdownload",
      message: BAD_TYPE,
      buffer: Buffer.from("MZ\u0090\u0000fake", "latin1"),
    },
    { name: "pixel.gif", mimeType: "image/gif", message: BAD_TYPE, buffer: fixture("pixel.gif") },
    {
      name: "grande.png",
      mimeType: "image/png",
      message: TOO_BIG,
      buffer: syntheticPng(1000, 800, 3, true),
    },
    {
      name: "foto.png",
      mimeType: "text/plain",
      message: BAD_TYPE,
      buffer: Buffer.from("isto nao e uma imagem\n"),
    },
    {
      name: "animado.webp",
      mimeType: "image/webp",
      message: REJECTED,
      buffer: fixture("animated.webp"),
    },
    {
      name: "larga.png",
      mimeType: "image/png",
      message: REJECTED,
      buffer: syntheticPng(4097, 4096, 1),
    },
  ];
  expect(rejected[2]!.buffer.length).toBeGreaterThan(2_097_152);

  for (const file of rejected) {
    await test.step(file.name, () => expectRejectedKeepingAvatar(page, file));
    await openAvatarDialog(page);
  }
  await expectNoAxeViolations(page);
});

test("imagem 4096x4096 e aceita (REQ-049)", async ({ page }) => {
  test.setTimeout(120_000);
  await register(page, uniqueUser("max"));
  const before = await srcOf(profileAvatar(page));
  await openAvatarDialog(page);
  await uploadFile(page, {
    name: "maxima.png",
    mimeType: "image/png",
    buffer: syntheticPng(4096, 4096, 1),
  });

  await expect(page.getByRole("dialog")).toBeHidden({ timeout: 60_000 });
  await expect.poll(() => profileAvatar(page).getAttribute("src")).not.toBe(before);
  await expectServedWebpWithoutExif(page, await srcOf(profileAvatar(page)));
});
