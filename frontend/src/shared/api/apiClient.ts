// Codigo 401 que nao derruba a sessao: a senha atual errada e erro de campo, nao sessao expirada.
const INCORRECT_PASSWORD_CODE = "incorrect_password";

export type FieldErrors = Record<string, string[]>;

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: FieldErrors;
  readonly traceId?: string;

  constructor(status: number, code: string, fieldErrors: FieldErrors = {}, traceId?: string) {
    super(code);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
    this.traceId = traceId;
  }
}

export interface ApiClientOptions {
  baseUrl: string;
  onSessionExpired: () => void;
  isDevelopment?: boolean;
}

export interface ApiClient {
  get<T>(path: string): Promise<T>;
  post<T>(path: string, body?: unknown): Promise<T>;
  put<T>(path: string, body?: unknown): Promise<T>;
  delete<T>(path: string): Promise<T>;
  upload<T>(path: string, body: FormData): Promise<T>;
}

const CSRF_PATH = "/api/v1/csrf";
const DEFAULT_TIMEOUT_MS = 20_000;
const UPLOAD_TIMEOUT_MS = 30_000;

type Method = "GET" | "POST" | "PUT" | "DELETE";

interface ProblemDetails {
  code?: string;
  errors?: FieldErrors;
  traceId?: string;
}

async function fetchWithTimeout(
  url: string,
  init: RequestInit,
  timeoutMs: number,
): Promise<Response> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...init, signal: controller.signal });
  } catch {
    throw new ApiError(0, "network_error");
  } finally {
    clearTimeout(timer);
  }
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return {};
  }
}

export function createApiClient(options: ApiClientOptions): ApiClient {
  const { baseUrl, onSessionExpired, isDevelopment = false } = options;
  let csrfToken: Promise<string> | undefined;

  const toApiError = async (response: Response): Promise<ApiError> => {
    const problem = await readProblem(response);
    if (isDevelopment && problem.traceId) {
      console.error(
        `[api] ${response.status} ${problem.code ?? "error"} traceId=${problem.traceId}`,
      );
    }
    return new ApiError(
      response.status,
      problem.code ?? "unknown_error",
      problem.errors ?? {},
      problem.traceId,
    );
  };

  const getCsrfToken = (): Promise<string> => {
    csrfToken ??= fetchWithTimeout(
      baseUrl + CSRF_PATH,
      { method: "GET", credentials: "same-origin" },
      DEFAULT_TIMEOUT_MS,
    )
      .then(async (response) => {
        if (!response.ok) throw await toApiError(response);
        return ((await response.json()) as { token: string }).token;
      })
      .catch((error: unknown) => {
        csrfToken = undefined;
        throw error;
      });
    return csrfToken;
  };

  const request = async <T>(
    method: Method,
    path: string,
    body: unknown,
    timeoutMs: number,
  ): Promise<T> => {
    const headers: Record<string, string> = {};
    if (method !== "GET") headers["X-CSRF-Token"] = await getCsrfToken();

    let payload: BodyInit | undefined;
    if (body instanceof FormData) {
      payload = body;
    } else if (body !== undefined) {
      headers["Content-Type"] = "application/json";
      payload = JSON.stringify(body);
    }

    const response = await fetchWithTimeout(
      baseUrl + path,
      { method, headers, body: payload, credentials: "same-origin" },
      timeoutMs,
    );
    if (!response.ok) {
      const error = await toApiError(response);
      // Senha incorreta ao confirmar uma acao nao encerra a sessao (S11).
      if (response.status === 401 && error.code !== INCORRECT_PASSWORD_CODE) onSessionExpired();
      throw error;
    }
    if (response.status === 204) return undefined as T;
    const text = await response.text();
    if (text === "") return undefined as T;
    return JSON.parse(text) as T;
  };

  return {
    get: (path) => request("GET", path, undefined, DEFAULT_TIMEOUT_MS),
    post: (path, body) => request("POST", path, body, DEFAULT_TIMEOUT_MS),
    put: (path, body) => request("PUT", path, body, DEFAULT_TIMEOUT_MS),
    delete: (path) => request("DELETE", path, undefined, DEFAULT_TIMEOUT_MS),
    upload: (path, body) => request("POST", path, body, UPLOAD_TIMEOUT_MS),
  };
}
