// T-L01 / AC-L01c - o tema segue o sistema e a escolha do usuario persiste (docs/design-system.md v1.0, secao Troca de tema).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  THEME_STORAGE_KEY,
  applyPreference,
  applyStoredTheme,
  getResolvedTheme,
  getThemePreference,
  readStoredPreference,
  resolveTheme,
  setThemePreference,
  storePreference,
  subscribeTheme,
  type StorageLike,
} from "./theme";

function memoryStorage(
  initial: Record<string, string> = {},
): StorageLike & { data: Map<string, string> } {
  const data = new Map(Object.entries(initial));
  return {
    data,
    getItem: (key) => data.get(key) ?? null,
    setItem: (key, value) => void data.set(key, value),
    removeItem: (key) => void data.delete(key),
  };
}

function brokenStorage(): StorageLike {
  const fail = () => {
    throw new DOMException("bloqueado", "SecurityError");
  };
  return { getItem: fail, setItem: fail, removeItem: fail };
}

/** matchMedia falso que deixa o teste trocar o tema do sistema e dispara os ouvintes. */
function stubSystemTheme(initialDark: boolean) {
  let dark = initialDark;
  const listeners = new Set<() => void>();
  vi.stubGlobal(
    "matchMedia",
    vi.fn((query: string) => ({
      get matches() {
        return query.includes("prefers-color-scheme: dark") ? dark : false;
      },
      media: query,
      addEventListener: (_: string, listener: () => void) => listeners.add(listener),
      removeEventListener: (_: string, listener: () => void) => listeners.delete(listener),
    })),
  );
  return {
    setDark(next: boolean) {
      dark = next;
      listeners.forEach((listener) => listener());
    },
    listenerCount: () => listeners.size,
  };
}

beforeEach(() => {
  document.documentElement.removeAttribute("data-theme");
  window.localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("resolveTheme - segue o sistema por padrao", () => {
  it.each([
    ["system", true, "dark"],
    ["system", false, "light"],
    ["light", true, "light"],
    ["dark", false, "dark"],
  ] as const)(
    "preferencia %s com sistema escuro=%s resolve %s",
    (preference, systemDark, expected) => {
      expect(resolveTheme(preference, systemDark)).toBe(expected);
    },
  );
});

describe("readStoredPreference - leitura segura do localStorage", () => {
  it("sem nada salvo, segue o sistema", () => {
    expect(readStoredPreference(memoryStorage())).toBe("system");
  });

  it.each(["light", "dark"] as const)("devolve a escolha salva (%s)", (saved) => {
    expect(readStoredPreference(memoryStorage({ [THEME_STORAGE_KEY]: saved }))).toBe(saved);
  });

  it("ignora valor invalido ou adulterado e segue o sistema", () => {
    expect(readStoredPreference(memoryStorage({ [THEME_STORAGE_KEY]: "<script>" }))).toBe("system");
    expect(readStoredPreference(memoryStorage({ [THEME_STORAGE_KEY]: "system" }))).toBe("system");
  });

  it("nao quebra quando o armazenamento esta bloqueado (modo privado, cookies desligados)", () => {
    expect(readStoredPreference(brokenStorage())).toBe("system");
  });
});

describe("storePreference - persiste so a escolha explicita", () => {
  it.each(["light", "dark"] as const)("salva %s sob a chave do app", (choice) => {
    const storage = memoryStorage();

    storePreference(choice, storage);

    expect(storage.data.get(THEME_STORAGE_KEY)).toBe(choice);
  });

  it("voltar ao sistema apaga a escolha salva", () => {
    const storage = memoryStorage({ [THEME_STORAGE_KEY]: "dark" });

    storePreference("system", storage);

    expect(storage.data.has(THEME_STORAGE_KEY)).toBe(false);
  });

  it("nao quebra quando o armazenamento esta bloqueado", () => {
    expect(() => storePreference("dark", brokenStorage())).not.toThrow();
    expect(() => storePreference("system", brokenStorage())).not.toThrow();
  });
});

describe("applyPreference - data-theme no <html>", () => {
  it.each(["light", "dark"] as const)("escolha explicita %s grava data-theme", (choice) => {
    const root = document.createElement("html");

    applyPreference(choice, root);

    expect(root.getAttribute("data-theme")).toBe(choice);
  });

  it("seguir o sistema remove o atributo (o CSS decide por prefers-color-scheme)", () => {
    const root = document.createElement("html");
    root.setAttribute("data-theme", "dark");

    applyPreference("system", root);

    expect(root.hasAttribute("data-theme")).toBe(false);
  });
});

describe("applyStoredTheme - antes do primeiro quadro", () => {
  it("aplica a escolha salva no <html> e a devolve", () => {
    const storage = memoryStorage({ [THEME_STORAGE_KEY]: "dark" });

    expect(applyStoredTheme(document.documentElement, storage)).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("sem escolha salva nao grava atributo: o sistema manda", () => {
    expect(applyStoredTheme(document.documentElement, memoryStorage())).toBe("system");
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
  });

  it("com armazenamento bloqueado nao lanca e segue o sistema", () => {
    expect(applyStoredTheme(document.documentElement, brokenStorage())).toBe("system");
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
  });
});

describe("estado do tema: preferencia, tema resolvido e troca pelo usuario", () => {
  it("com o sistema escuro e sem escolha, o tema resolvido e escuro e a preferencia e 'system'", () => {
    stubSystemTheme(true);
    setThemePreference("system");

    expect(getThemePreference()).toBe("system");
    expect(getResolvedTheme()).toBe("dark");
  });

  it("escolher claro vence o sistema escuro, grava data-theme e persiste no localStorage", () => {
    stubSystemTheme(true);

    setThemePreference("light");

    expect(getThemePreference()).toBe("light");
    expect(getResolvedTheme()).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe("light");
  });

  it("a escolha persiste: depois de recarregar, applyStoredTheme restaura o tema salvo", () => {
    stubSystemTheme(false);
    setThemePreference("dark");
    document.documentElement.removeAttribute("data-theme");

    expect(applyStoredTheme()).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("voltar para 'system' apaga a escolha e devolve o comando ao sistema", () => {
    stubSystemTheme(true);
    setThemePreference("light");

    setThemePreference("system");

    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    expect(getResolvedTheme()).toBe("dark");
  });

  it("avisa os ouvintes quando o usuario troca e quando o sistema muda (so em 'system')", () => {
    const system = stubSystemTheme(false);
    setThemePreference("system");
    const listener = vi.fn();
    const unsubscribe = subscribeTheme(listener);

    system.setDark(true);
    expect(listener).toHaveBeenCalledTimes(1);
    expect(getResolvedTheme()).toBe("dark");

    setThemePreference("light");
    expect(listener).toHaveBeenCalledTimes(2);
    expect(getResolvedTheme()).toBe("light");

    unsubscribe();
    system.setDark(false);
    expect(listener).toHaveBeenCalledTimes(2);
    expect(system.listenerCount()).toBe(0);
  });

  it("sem matchMedia (ambiente antigo) cai em claro sem lancar", () => {
    vi.stubGlobal("matchMedia", undefined);
    setThemePreference("system");

    expect(getResolvedTheme()).toBe("light");
  });
});
