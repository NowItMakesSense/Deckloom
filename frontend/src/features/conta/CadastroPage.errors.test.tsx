// T-108 (RED): S1 Cadastro - erros do servidor, email indisponivel, versao e atraso.
// REQ-002, REQ-041, REQ-043; WC-12 (resposta lenta, relogio falso); MOT-05/MOT-06 (so opacity).
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "../../app/SessionProvider";
import { RATE_LIMIT_NOTICE_TEXT } from "../../design-system/RateLimitNotice";
import { normalize, parseCss, readAllCss } from "../../shared/testing/cssScan";
import { CadastroPage } from "./CadastroPage";

const BASE = "http://localhost";
const CSRF_URL = `${BASE}/api/v1/csrf`;
const SESSION_URL = `${BASE}/api/v1/auth/session`;
const TERMS_URL = `${BASE}/api/v1/legal/terms`;
const REGISTER_URL = `${BASE}/api/v1/auth/register`;
const OLD_VERSION = "2026-10-01";
const NEW_VERSION = "2026-11-01";

const EMAIL_UNAVAILABLE = "Não foi possível usar este email. Tente outro ou entre na sua conta.";
const VERSION_MISMATCH =
  "Os Termos de Uso ou a Política de Privacidade foram atualizados. Revise e aceite novamente.";
const GENERIC_ERROR = "Não foi possível criar a conta agora. Tente de novo.";

vi.setConfig({ testTimeout: 15_000 });

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  vi.useRealTimers();
  server.resetHandlers();
});
afterAll(() => server.close());

interface RegisterBody {
  email: string;
  password: string;
  displayName: string;
  acceptTerms: boolean;
  termsVersion: string;
  adultDeclaration: boolean;
}

function problem(status: number, body: object) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function created() {
  return HttpResponse.json(
    { displayName: "Jogadora Teste", avatar: null, hasPassword: true, hasGoogle: false },
    { status: 201 },
  );
}

function baseHandlers() {
  const state = { termsVersion: OLD_VERSION, termsReads: 0 };
  server.use(
    http.get(CSRF_URL, () => HttpResponse.json({ token: "csrf-token" })),
    http.get(SESSION_URL, () => problem(401, { code: "unauthenticated" })),
    http.get(TERMS_URL, () => {
      state.termsReads += 1;
      return HttpResponse.json({
        version: state.termsVersion,
        termsUrl: "/termos",
        privacyUrl: "/privacidade",
      });
    }),
  );
  return state;
}

function captureRegister(respond: () => Response | Promise<Response>) {
  const bodies: RegisterBody[] = [];
  server.use(
    http.post(REGISTER_URL, async ({ request }) => {
      bodies.push((await request.json()) as RegisterBody);
      return respond();
    }),
  );
  return bodies;
}

function LocationProbe() {
  const location = useLocation();
  return <p data-testid="location">{location.pathname}</p>;
}

function renderPage() {
  const terms = baseHandlers();
  const view = render(
    <MemoryRouter initialEntries={["/cadastro"]}>
      <SessionProvider baseUrl={BASE}>
        <Routes>
          <Route path="/cadastro" element={<CadastroPage />} />
          <Route path="/perfil" element={<p>pagina perfil</p>} />
          <Route path="/entrar" element={<p>pagina entrar</p>} />
        </Routes>
        <LocationProbe />
      </SessionProvider>
    </MemoryRouter>,
  );
  return { ...view, terms };
}

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByLabelText("Email"));
  await user.paste("jogadora@exemplo.com");
  await user.click(screen.getByLabelText("Senha"));
  await user.paste("senha-segura-1");
  await user.click(screen.getByLabelText("Nome de exibição"));
  await user.paste("Jogadora Teste");
  await user.click(screen.getByRole("checkbox", { name: /Termos de Uso/ }));
  await user.click(screen.getByRole("checkbox", { name: /18 anos ou mais/ }));
}

function summaryMessages(): string[] {
  const active = document.activeElement as HTMLElement;
  return within(active)
    .getAllByRole("link")
    .map((link) => link.textContent ?? "");
}

function expectFormKept() {
  expect(screen.getByLabelText("Email")).toHaveValue("jogadora@exemplo.com");
  expect(screen.getByLabelText("Nome de exibição")).toHaveValue("Jogadora Teste");
  expect(screen.getByRole("checkbox", { name: /Termos de Uso/ })).toBeChecked();
  expect(screen.getByRole("checkbox", { name: /18 anos ou mais/ })).toBeChecked();
  expect(screen.getByLabelText("Senha")).toHaveValue("");
}

// Relogio falso so para timers: o MSW e o fetch continuam em tempo real (padrao de apiClient.test.ts).
// O formulario e preenchido antes, com timers reais; o envio usa fireEvent.
async function fillThenFakeTimers() {
  const view = renderPage();
  const user = userEvent.setup();
  await fillValid(user);
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
  return { ...view, user };
}

function gate() {
  let release: () => void = () => undefined;
  const promise = new Promise<void>((resolve) => {
    release = resolve;
  });
  return { promise, release };
}

// vi.waitFor avanca o relogio falso; este espera em tempo real, sem mexer no relogio falso.
const realSetTimeout = globalThis.setTimeout;
async function until(check: () => void) {
  const start = Date.now();
  for (;;) {
    try {
      check();
      return;
    } catch (error) {
      if (Date.now() - start > 2_000) throw error;
      await act(async () => {
        await new Promise<void>((resolve) => realSetTimeout(resolve, 10));
      });
    }
  }
}

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

function noticeRegion(container: HTMLElement): HTMLElement {
  const region = container.querySelector<HTMLElement>(".rate-limit-notice");
  expect(region, "RateLimitNotice ausente da pagina").not.toBeNull();
  return region as HTMLElement;
}

describe("S1 Cadastro - email indisponivel (REQ-002, REQ-041)", () => {
  it("409 email_unavailable: texto exato no campo email (descrevendo o input), foco no resumo, sem revelar nada alem disso", async () => {
    captureRegister(() =>
      problem(409, { code: "email_unavailable", errors: { email: ["unavailable"] } }),
    );
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([EMAIL_UNAVAILABLE]);
    const email = screen.getByLabelText("Email");
    expect(email).toHaveAttribute("aria-invalid", "true");
    const describedBy = email.getAttribute("aria-describedby") ?? "";
    const described = describedBy
      .split(" ")
      .map((id) => document.getElementById(id)?.textContent ?? "")
      .join(" ");
    expect(described).toContain(EMAIL_UNAVAILABLE);
    expectFormKept();
    expect(screen.getByTestId("location")).toHaveTextContent("/cadastro");
  });
});

describe("S1 Cadastro - versao dos termos desatualizada (REQ-043)", () => {
  it("version_mismatch: erro no campo de aceite e nova leitura de /legal/terms; reenvio usa a versao nova", async () => {
    const { terms } = renderPage();
    const bodies = captureRegister(() => {
      if (bodies.length === 1) {
        terms.termsVersion = NEW_VERSION;
        return problem(400, {
          code: "validation_failed",
          errors: { acceptTerms: ["version_mismatch"] },
        });
      }
      return created();
    });
    const user = userEvent.setup();
    await fillValid(user);
    await waitFor(() => expect(terms.termsReads).toBe(1));
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(document.activeElement).toHaveAttribute("role", "alert"));
    expect(summaryMessages()).toEqual([VERSION_MISMATCH]);
    expect(screen.getAllByText(VERSION_MISMATCH).length).toBeGreaterThanOrEqual(2);
    // Nova leitura da versao vigente depois da recusa.
    await waitFor(() => expect(terms.termsReads).toBe(2));
    expect(screen.getByTestId("location")).toHaveTextContent("/cadastro");

    const accept = screen.getByRole("checkbox", { name: /Termos de Uso/ });
    if (!(accept as HTMLInputElement).checked) await user.click(accept);
    await user.click(screen.getByLabelText("Senha"));
    await user.paste("senha-segura-1");
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    await waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[0].termsVersion).toBe(OLD_VERSION);
    expect(bodies[1].termsVersion).toBe(NEW_VERSION);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
  });
});

describe("S1 Cadastro - falha geral (REQ-002)", () => {
  it.each([
    ["500", () => problem(500, { code: "internal_error", detail: "TEXTO-DO-SERVIDOR" })],
    ["503", () => problem(503, { code: "unavailable" })],
    ["erro de rede", () => HttpResponse.error()],
  ])("%s: Alert com o texto exato, dados preservados e senha limpa", async (_caso, respond) => {
    const bodies = captureRegister(respond);
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(GENERIC_ERROR);
    expect(alert.textContent).toBe(GENERIC_ERROR);
    expect(document.body.textContent).not.toContain("TEXTO-DO-SERVIDOR");
    expect(document.body.textContent).not.toContain("Não foi possível conectar");
    expectFormKept();
    expect(screen.getByLabelText("Email")).not.toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("button", { name: "Criar conta" })).not.toHaveAttribute("aria-busy");
    expect(screen.getByTestId("location")).toHaveTextContent("/cadastro");
    expect(bodies).toHaveLength(1);
  });

  it("depois da falha geral o Jogador pode reenviar", async () => {
    let calls = 0;
    const bodies = captureRegister(() => {
      calls += 1;
      return calls === 1 ? problem(500, { code: "internal_error" }) : created();
    });
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));
    await screen.findByText(GENERIC_ERROR);

    await user.click(screen.getByLabelText("Senha"));
    await user.paste("senha-segura-1");
    await user.click(screen.getByRole("button", { name: "Criar conta" }));
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
    expect(bodies).toHaveLength(2);
  });
});

describe("S1 Cadastro - resposta lenta (WC-12, REQ-041)", () => {
  it("antes de 2 s so ha o espaco reservado; depois de 2 s o RateLimitNotice mostra so o atraso, sem texto de bloqueio", async () => {
    const release = gate();
    captureRegister(async () => {
      await release.promise;
      return created();
    });
    const { container } = await fillThenFakeTimers();
    fireEvent.click(screen.getByRole("button", { name: "Criar conta" }));
    await until(() =>
      expect(screen.getByRole("button", { name: "Criar conta" })).toHaveAttribute(
        "aria-busy",
        "true",
      ),
    );

    const region = noticeRegion(container);
    expect(region).toHaveAttribute("role", "status");
    await advance(1_999);
    expect(region).toBeEmptyDOMElement();

    await advance(1);
    expect(region).toHaveTextContent(RATE_LIMIT_NOTICE_TEXT);
    expect(region.textContent).not.toMatch(/bloquead|bloqueio|muitas tentativas|conta foi/i);
    expect(noticeRegion(container)).toBe(region);
    release.release();
  });

  it.each([3_000, 12_000])(
    "resposta em %i ms: loading sem mudar o rotulo, campos somente leitura, sem duplo envio, depois sucesso",
    async (ms) => {
      const release = gate();
      const bodies = captureRegister(async () => {
        await release.promise;
        return created();
      });
      const { container } = await fillThenFakeTimers();
      const button = screen.getByRole("button", { name: "Criar conta" });
      const classBefore = button.className;
      fireEvent.click(button);
      await until(() => expect(button).toHaveAttribute("aria-busy", "true"));

      // Largura estavel: mesmo nome acessivel e mesmo texto durante o loading; nada de largura inline.
      expect(screen.getByRole("button", { name: "Criar conta" })).toBe(button);
      expect(button.textContent).toBe("Criar conta");
      expect(button.className).toBe(classBefore);
      expect(button.style.width).toBe("");
      for (const label of ["Email", "Nome de exibição"]) {
        expect(screen.getByLabelText(label)).toHaveAttribute("readonly");
      }
      expect(screen.getByLabelText("Senha")).toHaveAttribute("readonly");

      await advance(2_000);
      expect(noticeRegion(container)).toHaveTextContent(RATE_LIMIT_NOTICE_TEXT);
      fireEvent.click(button);
      fireEvent.submit(button.closest("form") as HTMLFormElement);
      await advance(ms - 2_000);
      expect(button).toHaveAttribute("aria-busy", "true");

      release.release();
      await until(() => expect(screen.getByTestId("location")).toHaveTextContent("/perfil"));
      expect(bodies).toHaveLength(1);
    },
  );

  it("sem resposta por 20 s: vira o erro generico, com dados preservados, senha limpa e o aviso de atraso some", async () => {
    const never = gate();
    const bodies = captureRegister(async () => {
      await never.promise;
      return created();
    });
    const { container } = await fillThenFakeTimers();
    const button = screen.getByRole("button", { name: "Criar conta" });
    fireEvent.click(button);
    await until(() => expect(bodies).toHaveLength(1));

    await advance(19_999);
    expect(button).toHaveAttribute("aria-busy", "true");
    expect(screen.queryByText(GENERIC_ERROR)).not.toBeInTheDocument();
    expect(noticeRegion(container)).toHaveTextContent(RATE_LIMIT_NOTICE_TEXT);

    await advance(1);
    const alert = screen.getByRole("alert");
    expect(alert.textContent).toBe(GENERIC_ERROR);
    expect(button).not.toHaveAttribute("aria-busy");
    expect(noticeRegion(container)).toBeEmptyDOMElement();
    expectFormKept();
    expect(screen.getByLabelText("Email")).not.toHaveAttribute("readonly");
    expect(screen.getByTestId("location")).toHaveTextContent("/cadastro");
    expect(bodies).toHaveLength(1);
    never.release();
  });
});

describe("S1 Cadastro - movimento so com opacity (MOT-05, MOT-06)", () => {
  const files = readAllCss();
  const rulesOf = (name: string) => parseCss(files[`/src/design-system/${name}`] ?? "").rules;

  it("o Alert de falha geral usa o componente Alert (entrada so com opacity)", async () => {
    captureRegister(() => problem(500, { code: "internal_error" }));
    renderPage();
    const user = userEvent.setup();
    await fillValid(user);
    await user.click(screen.getByRole("button", { name: "Criar conta" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveClass("alert", "alert--error");
    const keyframes = rulesOf("Alert.css").filter((rule) => rule.keyframes !== null);
    expect(keyframes.length).toBeGreaterThan(0);
    for (const frame of keyframes) {
      expect(frame.declarations.map((d) => d.property).every((p) => p === "opacity")).toBe(true);
    }
  });

  it("RateLimitNotice.css: entrada so com opacity de 0 a 1 em 160ms --ease-out, sem deslocamento, e espaco reservado", () => {
    const css = files["/src/design-system/RateLimitNotice.css"];
    expect(
      css,
      "arquivo ausente: src/design-system/RateLimitNotice.css (T-108 GREEN)",
    ).toBeDefined();
    const rules = rulesOf("RateLimitNotice.css");

    const keyframes = rules.filter((rule) => rule.keyframes !== null);
    expect(keyframes.length, "sem @keyframes de entrada").toBeGreaterThan(0);
    for (const frame of keyframes) {
      expect(frame.declarations.map((d) => d.property).every((p) => p === "opacity")).toBe(true);
    }
    const animation = rules
      .filter((rule) => rule.keyframes === null && /rate-limit-notice/.test(rule.selector))
      .flatMap((rule) => rule.declarations.filter((d) => d.property === "animation"))
      .map((d) => normalize(d.value));
    expect(
      animation.some(
        (v) =>
          (/\b160ms\b/.test(v) || v.includes("var(--duration-fade)")) &&
          (v.includes("cubic-bezier(0.23, 1, 0.32, 1)") || v.includes("var(--ease-out)")),
      ),
      `animation do aviso deve usar 160ms e --ease-out: ${animation.join(" | ")}`,
    ).toBe(true);

    const all = rules.flatMap((rule) => rule.declarations);
    expect(all.some((d) => d.property === "transform" || d.property === "translate")).toBe(false);
    const reserved = rules
      .filter((rule) => /rate-limit-notice/.test(rule.selector) && rule.keyframes === null)
      .flatMap((rule) => rule.declarations)
      .some((d) => d.property === "min-height");
    expect(reserved, "faltou min-height reservando o espaco do aviso (WC-12)").toBe(true);
  });
});
