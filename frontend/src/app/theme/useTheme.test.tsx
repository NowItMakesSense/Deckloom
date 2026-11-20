// T-L01 / AC-L01c - gancho que a T-L01C usa no botao de troca de tema.
import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { THEME_STORAGE_KEY, setThemePreference } from "./theme";
import { useTheme } from "./useTheme";

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
  };
}

beforeEach(() => {
  window.localStorage.clear();
  document.documentElement.removeAttribute("data-theme");
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("useTheme", () => {
  it("expoe a preferencia e o tema resolvido (sistema escuro, sem escolha)", () => {
    stubSystemTheme(true);
    setThemePreference("system");

    const { result } = renderHook(() => useTheme());

    expect(result.current.preference).toBe("system");
    expect(result.current.theme).toBe("dark");
  });

  it("setPreference troca o tema, persiste e rerrenderiza", () => {
    stubSystemTheme(false);
    setThemePreference("system");
    const { result } = renderHook(() => useTheme());

    act(() => result.current.setPreference("dark"));

    expect(result.current.preference).toBe("dark");
    expect(result.current.theme).toBe("dark");
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("acompanha o sistema enquanto o usuario nao escolheu", () => {
    const system = stubSystemTheme(false);
    setThemePreference("system");
    const { result } = renderHook(() => useTheme());
    expect(result.current.theme).toBe("light");

    act(() => system.setDark(true));

    expect(result.current.theme).toBe("dark");
    expect(result.current.preference).toBe("system");
  });

  it("toggle alterna entre claro e escuro a partir do tema que o jogador esta vendo", () => {
    stubSystemTheme(true);
    setThemePreference("system");
    const { result } = renderHook(() => useTheme());

    act(() => result.current.toggle());
    expect(result.current.theme).toBe("light");
    expect(result.current.preference).toBe("light");

    act(() => result.current.toggle());
    expect(result.current.theme).toBe("dark");
    expect(result.current.preference).toBe("dark");
  });
});
