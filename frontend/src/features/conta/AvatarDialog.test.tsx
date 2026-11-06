// T-117 (RED): S10 Dialog alterar avatar.
// REQ-018, REQ-019, REQ-020, REQ-021, REQ-022, REQ-049; UX-07; A11Y-11; WC-09/WC-11 (so o que o jsdom verifica).
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { useState } from "react";
import { MemoryRouter } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider, useSession } from "../../app/SessionProvider";
import { expectNoA11yViolations } from "../../shared/testing/expectNoA11yViolations";
import { installUploadShim } from "../../shared/testing/uploadShim";
import { AvatarDialog } from "./AvatarDialog";
import type { AvatarValue } from "./AvatarDialog";

const dialogCss = Object.values(
  import.meta.glob("./AvatarDialog.css", {
    query: "?raw",
    import: "default",
    eager: true,
  }) as Record<string, string>,
)[0]!;

const BASE = "http://localhost";
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const CSRF_URL = `${BASE}/api/v1/csrf`;
const PRESETS_URL = `${BASE}/api/v1/catalog/avatar-presets`;
const PRESET_URL = `${BASE}/api/v1/me/avatar/preset`;
const CUSTOM_URL = `${BASE}/api/v1/me/avatar/custom`;

const REJECTED =
  "Não foi possível usar este arquivo. Envie uma imagem JPEG, PNG ou WebP de até 2 MB.";
const NETWORK = "Não foi possível enviar agora. Tente de novo.";
const LOAD_PRESETS = "Não foi possível carregar os avatares. Tente de novo.";
const SENDING = "Enviando imagem...";
const UPDATED = "Avatar atualizado.";
const NO_FILE = "Escolha um arquivo para enviar.";
const BAD_TYPE = "Escolha uma imagem JPEG, PNG ou WebP.";
const TOO_BIG = "A imagem precisa ter no máximo 2 MB.";
const MAX_BYTES = 2_097_152;

vi.setConfig({ testTimeout: 15_000 });

const server = setupServer();
let restoreUploadGlobals: () => void = () => undefined;
// R-22: o shim de File/FormData/fetch vale so para este arquivo e e restaurado no fim.
beforeAll(async () => {
  restoreUploadGlobals = await installUploadShim();
  server.listen({ onUnhandledFrame: "error" });
});
afterEach(() => {
  server.resetHandlers();
});
afterAll(() => {
  server.close();
  restoreUploadGlobals();
});

const P2: AvatarValue = {
  kind: "preset",
  presetId: "p2",
  imageUrl: "/avatars/presets/p2.svg",
  alt: "Avatar: Fenix",
};
const P3: AvatarValue = {
  kind: "preset",
  presetId: "p3",
  imageUrl: "/avatars/presets/p3.svg",
  alt: "Avatar: Golem",
};
const CUSTOM: AvatarValue = {
  kind: "custom",
  imageUrl: "/api/v1/me/avatar/custom?v=abc",
  alt: "Avatar enviado",
};

const PRESETS = [
  { id: "p1", label: "Dragao", imageUrl: "/avatars/presets/p1.svg", isDefault: true },
  { id: "p2", label: "Fenix", imageUrl: "/avatars/presets/p2.svg", isDefault: false },
  { id: "p3", label: "Golem", imageUrl: "/avatars/presets/p3.svg", isDefault: false },
];

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function rejected(status: number, reason: string) {
  return problem(status, { code: "avatar_rejected", reason });
}

function HeaderAvatar() {
  const session = useSession();
  const src = session.status === "player" ? (session.player.avatar as AvatarValue).imageUrl : "";
  return <img data-testid="header-avatar" alt="" src={src} />;
}

function Host({
  current,
  onChanged,
}: {
  current: AvatarValue;
  onChanged: (avatar: AvatarValue) => void;
}) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <HeaderAvatar />
      <main>
        <button type="button" onClick={() => setOpen(true)}>
          Alterar avatar
        </button>
      </main>
      <AvatarDialog
        open={open}
        onClose={() => setOpen(false)}
        current={current}
        onChanged={onChanged}
      />
    </>
  );
}

interface Options {
  current?: AvatarValue;
  presets?: () => Response | Promise<Response>;
  put?: () => Response | Promise<Response>;
  post?: () => Response | Promise<Response>;
  newAvatar?: AvatarValue;
}

function renderDialog(options: Options = {}) {
  const current = options.current ?? P2;
  const puts: unknown[] = [];
  const posts: { file: FormDataEntryValue | null; contentType: string }[] = [];
  const changed: AvatarValue[] = [];
  let sessionAvatar: AvatarValue = current;
  server.use(
    http.get(SESSION_URL, () =>
      HttpResponse.json({
        displayName: "Jogadora Teste",
        avatar: sessionAvatar,
        hasPassword: true,
        hasGoogle: false,
      }),
    ),
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(PRESETS_URL, () => (options.presets ? options.presets() : HttpResponse.json(PRESETS))),
    http.put(PRESET_URL, async ({ request }) => {
      puts.push(await request.json());
      if (options.put) return options.put();
      sessionAvatar = options.newAvatar ?? P3;
      return HttpResponse.json({ avatar: sessionAvatar });
    }),
    http.post(CUSTOM_URL, async ({ request }) => {
      const contentType = request.headers.get("content-type") ?? "";
      const form = await request.formData();
      posts.push({ file: form.get("file"), contentType });
      if (options.post) return options.post();
      sessionAvatar = options.newAvatar ?? CUSTOM;
      return HttpResponse.json({ avatar: sessionAvatar });
    }),
  );
  const view = render(
    <MemoryRouter>
      <SessionProvider baseUrl={BASE}>
        <Host current={current} onChanged={(avatar) => changed.push(avatar)} />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...view, puts, posts, changed };
}

function trigger(): HTMLElement {
  return screen.getByRole("button", { name: "Alterar avatar", hidden: true });
}

async function openDialog(user: ReturnType<typeof userEvent.setup>) {
  const button = await screen.findByRole("button", { name: "Alterar avatar" });
  await waitFor(() => {
    expect(screen.getByTestId("header-avatar").getAttribute("src")).not.toBe("");
  });
  await user.click(button);
  return screen.findByRole("dialog", { name: "Alterar avatar" });
}

function fileInput(): HTMLInputElement {
  const el = document.querySelector<HTMLInputElement>('input[type="file"]');
  expect(el, "input de arquivo ausente").not.toBeNull();
  return el as HTMLInputElement;
}

function makeFile(name: string, type: string, bytes = 10): File {
  return new File([new Uint8Array(bytes)], name, { type });
}

function headerSrc(): string | null {
  return screen.getByTestId("header-avatar").getAttribute("src");
}

describe("S10 Alterar avatar - abertura e presets (REQ-018, REQ-022)", () => {
  it("abre com titulo, grupo de presets em radios e o preset atual pre-selecionado", async () => {
    const user = userEvent.setup();
    renderDialog({ current: P2 });
    const dialog = await openDialog(user);
    expect(within(dialog).getByRole("group", { name: "Escolha um avatar" })).toBeInTheDocument();
    const radios = await within(dialog).findAllByRole("radio");
    expect(radios).toHaveLength(3);
    expect(within(dialog).getByRole("radio", { name: "Fenix" })).toBeChecked();
    expect(within(dialog).getByRole("radio", { name: "Golem" })).not.toBeChecked();
    expect(within(dialog).getByRole("button", { name: "Usar este avatar" })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Fechar" })).toBeInTheDocument();
  });

  it("enquanto os presets carregam, mostra estado de carregamento (aria-busy) sem radios (WC-09)", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const user = userEvent.setup();
    renderDialog({
      presets: async () => {
        await gate;
        return HttpResponse.json(PRESETS);
      },
    });
    const dialog = await openDialog(user);
    expect(dialog.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(within(dialog).queryAllByRole("radio")).toHaveLength(0);
    release();
    expect(await within(dialog).findByRole("radio", { name: "Fenix" })).toBeInTheDocument();
  });

  it("avatar atual custom: nenhum preset pre-selecionado e 'Usar este avatar' desabilitado ate escolher um", async () => {
    const user = userEvent.setup();
    renderDialog({ current: CUSTOM });
    const dialog = await openDialog(user);
    const radios = await within(dialog).findAllByRole("radio");
    radios.forEach((radio) => expect(radio).not.toBeChecked());
    const use = within(dialog).getByRole("button", { name: "Usar este avatar" });
    expect(use).toBeDisabled();
    await user.click(within(dialog).getByRole("radio", { name: "Golem" }));
    expect(use).toBeEnabled();
  });

  it("nao oferece arrastar-e-soltar: so o input de arquivo, sem texto de soltar (v0)", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    await within(dialog).findAllByRole("radio");
    expect(within(dialog).queryByText(/arraste|solte/i)).toBeNull();
    expect(
      dialog.querySelector('[ondrop], [data-dropzone], [role="presentation"][class*="drop"]'),
    ).toBeNull();
    expect(fileInput().type).toBe("file");
    expect(within(dialog).getByText(/JPEG, PNG ou WebP, até 2 MB/)).toBeInTheDocument();
  });

  it("nao tem violacoes de acessibilidade com o dialogo aberto", async () => {
    const user = userEvent.setup();
    const { container } = renderDialog();
    const dialog = await openDialog(user);
    await within(dialog).findAllByRole("radio");
    await expectNoA11yViolations(dialog);
    expect(container).toBeTruthy();
  });
});

describe("S10 - falha ou lista vazia de presets (WC-09)", () => {
  it("falha no catalogo mostra a mensagem e mantem o envio de arquivo", async () => {
    const user = userEvent.setup();
    renderDialog({ presets: () => problem(500, { code: "server_error" }) });
    const dialog = await openDialog(user);
    expect(await within(dialog).findByText(LOAD_PRESETS)).toBeInTheDocument();
    expect(within(dialog).queryAllByRole("radio")).toHaveLength(0);
    expect(fileInput()).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Enviar imagem" })).toBeInTheDocument();
  });

  it("lista vazia mostra a mesma mensagem e mantem o envio de arquivo", async () => {
    const user = userEvent.setup();
    renderDialog({ presets: () => HttpResponse.json([]) });
    const dialog = await openDialog(user);
    expect(await within(dialog).findByText(LOAD_PRESETS)).toBeInTheDocument();
    expect(fileInput()).toBeInTheDocument();
  });
});

describe("S10 - usar preset (REQ-018, REQ-021, A11Y-11, UX-07)", () => {
  it("'Usar este avatar' chama PUT /me/avatar/preset com o preset escolhido", async () => {
    const user = userEvent.setup();
    const { puts } = renderDialog({ current: P2 });
    const dialog = await openDialog(user);
    await user.click(await within(dialog).findByRole("radio", { name: "Golem" }));
    await user.click(within(dialog).getByRole("button", { name: "Usar este avatar" }));
    await waitFor(() => expect(puts).toEqual([{ presetId: "p3" }]));
  });

  it("sucesso fecha o dialogo, devolve o foco ao gatilho e anuncia 'Avatar atualizado.'", async () => {
    const user = userEvent.setup();
    const { changed } = renderDialog({ current: P2, newAvatar: P3 });
    const dialog = await openDialog(user);
    await user.click(await within(dialog).findByRole("radio", { name: "Golem" }));
    await user.click(within(dialog).getByRole("button", { name: "Usar este avatar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(trigger()).toHaveFocus());
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent(UPDATED);
    expect(changed).toEqual([P3]);
  });

  it("UX-07: o avatar do header muda sem recarregar apos o sucesso", async () => {
    const user = userEvent.setup();
    renderDialog({ current: P2, newAvatar: P3 });
    const dialog = await openDialog(user);
    expect(headerSrc()).toBe(P2.imageUrl);
    await user.click(await within(dialog).findByRole("radio", { name: "Golem" }));
    await user.click(within(dialog).getByRole("button", { name: "Usar este avatar" }));
    await waitFor(() => expect(headerSrc()).toBe(P3.imageUrl));
  });

  it("UX-07: erro do servidor no preset mantem o dialogo aberto e o avatar atual", async () => {
    const user = userEvent.setup();
    const { changed } = renderDialog({
      current: P2,
      put: () => problem(500, { code: "server_error" }),
    });
    const dialog = await openDialog(user);
    await user.click(await within(dialog).findByRole("radio", { name: "Golem" }));
    await user.click(within(dialog).getByRole("button", { name: "Usar este avatar" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Não foi possível salvar. Tente de novo.",
    );
    expect(screen.getByRole("dialog", { name: "Alterar avatar" })).toBeInTheDocument();
    expect(headerSrc()).toBe(P2.imageUrl);
    expect(changed).toEqual([]);
  });
});

describe("S10 - validacao do cliente (REQ-019, REQ-020)", () => {
  it("sem arquivo escolhido: 'Escolha um arquivo para enviar.' e nada e enviado", async () => {
    const user = userEvent.setup();
    const { posts } = renderDialog();
    const dialog = await openDialog(user);
    await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
    expect(await within(dialog).findByText(NO_FILE)).toBeInTheDocument();
    expect(posts).toHaveLength(0);
  });

  it("tipo fora de JPEG/PNG/WebP: 'Escolha uma imagem JPEG, PNG ou WebP.' e nada e enviado", async () => {
    const user = userEvent.setup({ applyAccept: false });
    const { posts } = renderDialog();
    const dialog = await openDialog(user);
    await user.upload(fileInput(), makeFile("nota.txt", "text/plain"));
    await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
    expect(await within(dialog).findByText(BAD_TYPE)).toBeInTheDocument();
    expect(posts).toHaveLength(0);
  });

  it("mais de 2 MB (2.097.152 bytes): 'A imagem precisa ter no máximo 2 MB.' e nada e enviado", async () => {
    const user = userEvent.setup();
    const { posts } = renderDialog();
    const dialog = await openDialog(user);
    await user.upload(fileInput(), makeFile("grande.png", "image/png", MAX_BYTES + 1));
    await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
    expect(await within(dialog).findByText(TOO_BIG)).toBeInTheDocument();
    expect(posts).toHaveLength(0);
  });

  it("exatamente 2.097.152 bytes e aceito pelo cliente e enviado", async () => {
    const user = userEvent.setup();
    const { posts } = renderDialog();
    const dialog = await openDialog(user);
    await user.upload(fileInput(), makeFile("limite.webp", "image/webp", MAX_BYTES));
    await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
    await waitFor(() => expect(posts).toHaveLength(1));
    expect(within(dialog).queryByText(TOO_BIG)).toBeNull();
  });

  it("nome de arquivo de 200 caracteres aparece inteiro no dialogo (WC-11)", async () => {
    const user = userEvent.setup();
    renderDialog();
    const dialog = await openDialog(user);
    const name = `${"a".repeat(196)}.png`;
    await user.upload(fileInput(), makeFile(name, "image/png"));
    expect(await within(dialog).findByText(name)).toBeInTheDocument();
  });
});

describe("S10 - envio de imagem (REQ-019, REQ-021, UX-07)", () => {
  it("chama POST /me/avatar/custom (multipart, campo 'file') e mostra 'Enviando imagem...' enquanto aguarda", async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const user = userEvent.setup();
    const { posts } = renderDialog({
      post: async () => {
        await gate;
        return HttpResponse.json({ avatar: CUSTOM });
      },
    });
    const dialog = await openDialog(user);
    const file = makeFile("foto.png", "image/png");
    await user.upload(fileInput(), file);
    await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
    expect(await within(dialog).findByText(SENDING)).toBeInTheDocument();
    release();
    await waitFor(() => expect(posts).toHaveLength(1));
    expect(posts[0].contentType).toMatch(/^multipart\/form-data/);
    expect((posts[0].file as File).name).toBe("foto.png");
  });

  it("sucesso fecha, foco no gatilho, 'Avatar atualizado.' e o header muda sem recarregar", async () => {
    const user = userEvent.setup();
    const { changed } = renderDialog({ current: P2, newAvatar: CUSTOM });
    const dialog = await openDialog(user);
    await user.upload(fileInput(), makeFile("foto.png", "image/png"));
    await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    // QA-01: o Dialog devolve o foco no cleanup do efeito, que roda depois de o DOM sair; sob carga a
    // verificacao imediata via o foco ainda no <body>.
    await waitFor(() => expect(trigger()).toHaveFocus());
    expect(await screen.findByRole("status")).toHaveTextContent(UPDATED);
    await waitFor(() => expect(headerSrc()).toBe(CUSTOM.imageUrl));
    expect(changed).toEqual([CUSTOM]);
  });

  it.each([
    ["size", 413],
    ["type", 415],
    ["content", 422],
    ["content (animada)", 422],
    ["content (4097 px)", 422],
  ])(
    "rejeicao do servidor (%s) mostra a mesma mensagem unica e mantem o avatar atual",
    async (reason, status) => {
      const user = userEvent.setup();
      const { changed } = renderDialog({
        current: P2,
        post: () => rejected(status, reason.split(" ")[0]),
      });
      const dialog = await openDialog(user);
      await user.upload(fileInput(), makeFile("foto.png", "image/png"));
      await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
      expect(await within(dialog).findByText(REJECTED)).toBeInTheDocument();
      expect(screen.getByRole("dialog", { name: "Alterar avatar" })).toBeInTheDocument();
      expect(headerSrc()).toBe(P2.imageUrl);
      expect(changed).toEqual([]);
      expect(within(dialog).queryByText(SENDING)).toBeNull();
    },
  );

  it("erro de rede mostra 'Não foi possível enviar agora. Tente de novo.' e mantem o avatar atual", async () => {
    const user = userEvent.setup();
    const { changed } = renderDialog({ current: P2, post: () => HttpResponse.error() });
    const dialog = await openDialog(user);
    await user.upload(fileInput(), makeFile("foto.png", "image/png"));
    await user.click(within(dialog).getByRole("button", { name: "Enviar imagem" }));
    expect(await within(dialog).findByText(NETWORK)).toBeInTheDocument();
    expect(within(dialog).queryByText(REJECTED)).toBeNull();
    expect(headerSrc()).toBe(P2.imageUrl);
    expect(changed).toEqual([]);
  });
});

describe("S10 - fechar (A11Y-11)", () => {
  it("Esc fecha o dialogo e devolve o foco ao gatilho", async () => {
    const user = userEvent.setup();
    renderDialog();
    await openDialog(user);
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(trigger()).toHaveFocus());
  });

  it("'Fechar' fecha o dialogo sem chamar a API e devolve o foco ao gatilho", async () => {
    const user = userEvent.setup();
    const { puts, posts } = renderDialog();
    const dialog = await openDialog(user);
    await within(dialog).findAllByRole("radio");
    await user.click(within(dialog).getByRole("button", { name: "Fechar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    await waitFor(() => expect(trigger()).toHaveFocus());
    expect(puts).toHaveLength(0);
    expect(posts).toHaveLength(0);
    expect(screen.queryByText(UPDATED)).toBeNull();
  });
});

describe("S10 - estilos da galeria (WC-09, MOT-15; absorvidos do AvatarPicker, T-183)", () => {
  it("grade de 4 colunas, 3 a 320px, alvos de 44px e miniaturas redondas", () => {
    expect(dialogCss).toMatch(/grid-template-columns:\s*repeat\(4,/);
    expect(dialogCss).toMatch(/@media[^{]*320px[^{]*\{[^}]*\{[^}]*repeat\(3,/);
    expect(dialogCss).toMatch(/var\(--target-min\)|44px/);
    expect(dialogCss).toMatch(/border-radius:\s*(50%|var\(--radius-full\))/);
  });

  it("a selecao do preset e instantanea, sem transicao nem animacao", () => {
    const checked = [...dialogCss.matchAll(/([^{}]+)\{([^}]*)\}/g)]
      .filter((m) => m[1]!.includes(":checked"))
      .map((m) => m[2]!);
    expect(checked.length).toBeGreaterThan(0);
    for (const body of checked) expect(body).not.toMatch(/transition|animation/);
    expect(dialogCss).not.toMatch(/animation|@keyframes|transition/);
  });
});
