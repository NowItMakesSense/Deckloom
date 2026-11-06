// T-103 (RED): apiClient com CSRF, Problem Details, timeouts e 401.
import { delay, http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, createApiClient } from "./apiClient";

const BASE = "http://localhost";
const server = setupServer();

beforeAll(() => server.listen({ onUnhandledFrame: "error" }));
afterEach(() => {
  server.resetHandlers();
  vi.useRealTimers();
  vi.restoreAllMocks();
});
afterAll(() => server.close());

function makeClient(overrides: { isDevelopment?: boolean } = {}) {
  const onSessionExpired = vi.fn();
  const client = createApiClient({
    baseUrl: BASE,
    onSessionExpired,
    isDevelopment: false,
    ...overrides,
  });
  return { client, onSessionExpired };
}

function problem(status: number, body: Record<string, unknown>) {
  return HttpResponse.json(body, {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

async function catchError(promise: Promise<unknown>): Promise<unknown> {
  try {
    await promise;
  } catch (error) {
    return error;
  }
  return undefined;
}

describe("apiClient - CSRF", () => {
  let csrfCalls: number;
  let seenHeaders: Array<{ method: string; token: string | null }>;

  beforeEach(() => {
    csrfCalls = 0;
    seenHeaders = [];
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => {
        csrfCalls += 1;
        return HttpResponse.json({ token: "csrf-abc" });
      }),
      http.all(`${BASE}/api/v1/things`, ({ request }) => {
        seenHeaders.push({
          method: request.method,
          token: request.headers.get("X-CSRF-Token"),
        });
        return HttpResponse.json({ ok: true });
      }),
    );
  });

  it("obtem GET /api/v1/csrf uma unica vez e envia X-CSRF-Token em POST, PUT e DELETE", async () => {
    const { client } = makeClient();

    await client.post("/api/v1/things", { a: 1 });
    await client.put("/api/v1/things", { a: 2 });
    await client.delete("/api/v1/things");

    expect(csrfCalls).toBe(1);
    expect(seenHeaders).toEqual([
      { method: "POST", token: "csrf-abc" },
      { method: "PUT", token: "csrf-abc" },
      { method: "DELETE", token: "csrf-abc" },
    ]);
  });

  it("nao envia X-CSRF-Token em GET nem busca o token para GET", async () => {
    const { client } = makeClient();

    await client.get("/api/v1/things");

    expect(csrfCalls).toBe(0);
    expect(seenHeaders).toEqual([{ method: "GET", token: null }]);
  });
});

describe("apiClient - Problem Details", () => {
  it("mapeia application/problem+json para {code, fieldErrors, traceId}", async () => {
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "t" })),
      http.post(`${BASE}/api/v1/players`, () =>
        problem(400, {
          type: "https://deckloom.app/problems/validation_failed",
          title: "Dados inválidos",
          status: 400,
          code: "validation_failed",
          errors: { displayName: ["length"], acceptTerms: ["required"] },
          traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
        }),
      ),
    );
    const { client } = makeClient();

    const error = await catchError(client.post("/api/v1/players", {}));

    expect(error).toBeInstanceOf(ApiError);
    const apiError = error as ApiError;
    expect(apiError.status).toBe(400);
    expect(apiError.code).toBe("validation_failed");
    expect(apiError.fieldErrors).toEqual({
      displayName: ["length"],
      acceptTerms: ["required"],
    });
    expect(apiError.traceId).toBe("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
  });

  it("usa fieldErrors vazio quando o Problem Details nao traz errors", async () => {
    server.use(
      http.get(`${BASE}/api/v1/boom`, () =>
        problem(500, { status: 500, code: "internal_error", traceId: "tr-1" }),
      ),
    );
    const { client } = makeClient();

    const error = (await catchError(client.get("/api/v1/boom"))) as ApiError;

    expect(error.code).toBe("internal_error");
    expect(error.fieldErrors).toEqual({});
  });
});

describe("apiClient - 401 sessao expirada", () => {
  it("aciona o callback de sessao expirada registrado e rejeita com ApiError", async () => {
    server.use(
      http.get(`${BASE}/api/v1/me`, () =>
        problem(401, { status: 401, code: "unauthenticated", traceId: "t-2" }),
      ),
    );
    const { client, onSessionExpired } = makeClient();

    const error = await catchError(client.get("/api/v1/me"));

    expect(onSessionExpired).toHaveBeenCalledTimes(1);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
  });

  it("401 com code incorrect_password nao aciona o callback e rejeita com o erro de dominio (S11 mantem o dialog)", async () => {
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "csrf-abc" })),
      http.delete(`${BASE}/api/v1/me`, () =>
        problem(401, { status: 401, code: "incorrect_password", traceId: "t-3" }),
      ),
    );
    const { client, onSessionExpired } = makeClient();

    const error = await catchError(client.delete("/api/v1/me"));

    expect(onSessionExpired).not.toHaveBeenCalled();
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
    expect((error as ApiError).code).toBe("incorrect_password");
  });

  it("401 com outro code ou sem corpo aciona o callback exatamente uma vez cada", async () => {
    server.use(
      http.get(`${BASE}/api/v1/me`, () =>
        problem(401, { status: 401, code: "invalid_credentials" }),
      ),
      http.get(`${BASE}/api/v1/empty`, () => new HttpResponse(null, { status: 401 })),
    );
    const { client, onSessionExpired } = makeClient();

    await catchError(client.get("/api/v1/me"));
    expect(onSessionExpired).toHaveBeenCalledTimes(1);

    await catchError(client.get("/api/v1/empty"));
    expect(onSessionExpired).toHaveBeenCalledTimes(2);
  });

  it("nao aciona o callback em respostas que nao sao 401", async () => {
    server.use(
      http.get(`${BASE}/api/v1/me`, () => problem(403, { status: 403, code: "csrf_failed" })),
    );
    const { client, onSessionExpired } = makeClient();

    await catchError(client.get("/api/v1/me"));

    expect(onSessionExpired).not.toHaveBeenCalled();
  });
});

describe("apiClient - timeouts", () => {
  it("timeout de 20 s vira erro de rede generico", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    server.use(
      http.get(`${BASE}/api/v1/slow`, async () => {
        await delay("infinite");
        return HttpResponse.json({});
      }),
    );
    const { client } = makeClient();

    const pending = catchError(client.get("/api/v1/slow"));
    await vi.advanceTimersByTimeAsync(20_000);
    const error = (await pending) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe("network_error");
    expect(error.status).toBe(0);
  });

  it("nao estoura antes de 20 s", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    server.use(
      http.get(`${BASE}/api/v1/slow`, async () => {
        await delay("infinite");
        return HttpResponse.json({});
      }),
    );
    const { client } = makeClient();
    let settled = false;

    const pending = catchError(client.get("/api/v1/slow")).then((e) => {
      settled = true;
      return e;
    });
    await vi.advanceTimersByTimeAsync(19_999);

    expect(settled).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    await pending;
  });

  it("upload usa timeout de 30 s (nao 20 s)", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "t" })),
      http.post(`${BASE}/api/v1/me/avatar`, async () => {
        await delay("infinite");
        return HttpResponse.json({});
      }),
    );
    const { client } = makeClient();
    let settled = false;

    const pending = catchError(client.upload("/api/v1/me/avatar", new FormData())).then((e) => {
      settled = true;
      return e;
    });
    await vi.advanceTimersByTimeAsync(20_000);
    expect(settled).toBe(false);
    await vi.advanceTimersByTimeAsync(10_000);
    const error = (await pending) as ApiError;

    expect(error.code).toBe("network_error");
  });

  it("falha de conexao tambem vira erro de rede generico", async () => {
    server.use(http.get(`${BASE}/api/v1/down`, () => HttpResponse.error()));
    const { client } = makeClient();

    const error = (await catchError(client.get("/api/v1/down"))) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe("network_error");
  });
});

describe("apiClient - traceId no console", () => {
  const traceId = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbbbbbbbbbb-01";

  beforeEach(() => {
    server.use(
      http.get(`${BASE}/api/v1/boom`, () =>
        problem(500, { status: 500, code: "internal_error", traceId }),
      ),
    );
  });

  it("em desenvolvimento registra o traceId no console", async () => {
    const spies = [
      vi.spyOn(console, "error").mockImplementation(() => undefined),
      vi.spyOn(console, "warn").mockImplementation(() => undefined),
      vi.spyOn(console, "info").mockImplementation(() => undefined),
      vi.spyOn(console, "log").mockImplementation(() => undefined),
      vi.spyOn(console, "debug").mockImplementation(() => undefined),
    ];
    const { client } = makeClient({ isDevelopment: true });

    await catchError(client.get("/api/v1/boom"));

    const logged = spies
      .flatMap((s) => s.mock.calls)
      .flat()
      .map(String);
    expect(logged.some((line) => line.includes(traceId))).toBe(true);
  });

  it("em producao nao escreve nada no console", async () => {
    const spies = [
      vi.spyOn(console, "error").mockImplementation(() => undefined),
      vi.spyOn(console, "warn").mockImplementation(() => undefined),
      vi.spyOn(console, "info").mockImplementation(() => undefined),
      vi.spyOn(console, "log").mockImplementation(() => undefined),
      vi.spyOn(console, "debug").mockImplementation(() => undefined),
    ];
    const { client } = makeClient({ isDevelopment: false });

    await catchError(client.get("/api/v1/boom"));

    for (const spy of spies) {
      expect(spy).not.toHaveBeenCalled();
    }
  });
});

describe("apiClient - credenciais", () => {
  it("inclui credenciais apenas para a mesma origem (credentials: same-origin)", async () => {
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "t" })),
      http.get(`${BASE}/api/v1/me`, () => HttpResponse.json({})),
      http.post(`${BASE}/api/v1/things`, () => HttpResponse.json({})),
    );
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const { client } = makeClient();

    await client.get("/api/v1/me");
    await client.post("/api/v1/things", {});

    expect(fetchSpy).toHaveBeenCalled();
    for (const call of fetchSpy.mock.calls) {
      const init = call[1] as RequestInit | undefined;
      expect(init?.credentials).toBe("same-origin");
    }
  });
});

describe("apiClient - logs sem segredos", () => {
  it("nenhuma senha nem token aparece no console, em dev ou producao, com sucesso ou erro", async () => {
    const password = "S3nh@-Super-Secreta-123";
    const csrf = "csrf-token-secreto";
    server.use(
      http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: csrf })),
      http.post(`${BASE}/api/v1/auth/login`, () =>
        problem(401, {
          status: 401,
          code: "invalid_credentials",
          traceId: "t-9",
        }),
      ),
    );
    const spies = [
      vi.spyOn(console, "error").mockImplementation(() => undefined),
      vi.spyOn(console, "warn").mockImplementation(() => undefined),
      vi.spyOn(console, "info").mockImplementation(() => undefined),
      vi.spyOn(console, "log").mockImplementation(() => undefined),
      vi.spyOn(console, "debug").mockImplementation(() => undefined),
    ];

    for (const isDevelopment of [true, false]) {
      const { client } = makeClient({ isDevelopment });
      await catchError(
        client.post("/api/v1/auth/login", {
          email: "a@b.co",
          password,
        }),
      );
    }

    const logged = JSON.stringify(spies.flatMap((s) => s.mock.calls));
    expect(logged).not.toContain(password);
    expect(logged).not.toContain(csrf);
  });
});

describe("apiClient - respostas de sucesso sem corpo", () => {
  beforeEach(() => {
    server.use(http.get(`${BASE}/api/v1/csrf`, () => HttpResponse.json({ token: "t" })));
  });

  it("POST 202 com corpo vazio resolve com undefined", async () => {
    server.use(
      http.post(
        `${BASE}/api/v1/auth/password-reset`,
        () => new HttpResponse(null, { status: 202 }),
      ),
    );
    const { client } = makeClient();

    await expect(
      client.post("/api/v1/auth/password-reset", { email: "a@b.co" }),
    ).resolves.toBeUndefined();
  });

  it("POST 204 com corpo vazio resolve com undefined", async () => {
    server.use(http.post(`${BASE}/api/v1/things`, () => new HttpResponse(null, { status: 204 })));
    const { client } = makeClient();

    await expect(client.post("/api/v1/things", {})).resolves.toBeUndefined();
  });

  it("GET 202 com corpo vazio resolve com undefined", async () => {
    server.use(http.get(`${BASE}/api/v1/things`, () => new HttpResponse(null, { status: 202 })));
    const { client } = makeClient();

    await expect(client.get("/api/v1/things")).resolves.toBeUndefined();
  });

  it("POST 200 com corpo JSON continua devolvendo o objeto", async () => {
    server.use(http.post(`${BASE}/api/v1/things`, () => HttpResponse.json({ id: 7, name: "x" })));
    const { client } = makeClient();

    await expect(client.post("/api/v1/things", {})).resolves.toEqual({ id: 7, name: "x" });
  });
});
