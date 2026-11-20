// Tema do app (docs/design-system.md v1.0, "Troca de tema"): segue o sistema; a escolha do jogador
// persiste no localStorage e vira data-theme no <html>. Sem escolha, o CSS decide por prefers-color-scheme.

export type Theme = "light" | "dark";
export type ThemePreference = "system" | Theme;

export const THEME_STORAGE_KEY = "deckloom.theme";

const DARK_QUERY = "(prefers-color-scheme: dark)";

let currentPreference: ThemePreference | null = null;
const listeners = new Set<() => void>();
let stopWatchingSystem: (() => void) | null = null;

export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

/** O acesso ao localStorage pode lancar (modo privado, cookies bloqueados): nesse caso nao ha armazenamento. */
function defaultStorage(): StorageLike | null {
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

function isExplicit(value: string | null): value is Theme {
  return value === "light" || value === "dark";
}

export function readStoredPreference(
  storage: StorageLike | null = defaultStorage(),
): ThemePreference {
  try {
    const saved = storage?.getItem(THEME_STORAGE_KEY) ?? null;
    return isExplicit(saved) ? saved : "system";
  } catch {
    return "system";
  }
}

export function storePreference(
  preference: ThemePreference,
  storage: StorageLike | null = defaultStorage(),
): void {
  try {
    if (preference === "system") storage?.removeItem(THEME_STORAGE_KEY);
    else storage?.setItem(THEME_STORAGE_KEY, preference);
  } catch {
    // Sem armazenamento a escolha vale so ate recarregar; o app continua funcionando.
  }
}

export function resolveTheme(preference: ThemePreference, systemDark: boolean): Theme {
  if (preference === "system") return systemDark ? "dark" : "light";
  return preference;
}

export function applyPreference(
  preference: ThemePreference,
  root: HTMLElement = document.documentElement,
): void {
  if (preference === "system") root.removeAttribute("data-theme");
  else root.setAttribute("data-theme", preference);
}

/** Chamada em main.tsx antes de montar o React, para o primeiro quadro ja sair no tema salvo. */
export function applyStoredTheme(
  root: HTMLElement = document.documentElement,
  storage: StorageLike | null = defaultStorage(),
): ThemePreference {
  const preference = readStoredPreference(storage);
  applyPreference(preference, root);
  currentPreference = preference;
  return preference;
}

function systemQuery(): MediaQueryList | null {
  return typeof window !== "undefined" && typeof window.matchMedia === "function"
    ? window.matchMedia(DARK_QUERY)
    : null;
}

function systemPrefersDark(): boolean {
  return systemQuery()?.matches ?? false;
}

function notify(): void {
  listeners.forEach((listener) => listener());
}

export function getThemePreference(): ThemePreference {
  currentPreference ??= readStoredPreference();
  return currentPreference;
}

export function getResolvedTheme(): Theme {
  return resolveTheme(getThemePreference(), systemPrefersDark());
}

export function setThemePreference(preference: ThemePreference): void {
  currentPreference = preference;
  storePreference(preference);
  applyPreference(preference);
  notify();
}

/** O sistema so interessa enquanto o jogador nao escolheu; o ouvinte do sistema vive enquanto houver assinantes. */
export function subscribeTheme(listener: () => void): () => void {
  listeners.add(listener);
  if (listeners.size === 1) {
    const query = systemQuery();
    const onSystemChange = () => {
      if (getThemePreference() === "system") notify();
    };
    query?.addEventListener("change", onSystemChange);
    stopWatchingSystem = () => query?.removeEventListener("change", onSystemChange);
  }
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      stopWatchingSystem?.();
      stopWatchingSystem = null;
    }
  };
}
