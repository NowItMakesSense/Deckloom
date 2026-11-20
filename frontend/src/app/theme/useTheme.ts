import { useCallback, useMemo, useSyncExternalStore } from "react";

import {
  getResolvedTheme,
  getThemePreference,
  setThemePreference,
  subscribeTheme,
  type Theme,
  type ThemePreference,
} from "./theme";

export interface UseTheme {
  /** O que o jogador escolheu: "system" enquanto ele nao trocou. */
  preference: ThemePreference;
  /** O tema que ele esta vendo agora (claro ou escuro). */
  theme: Theme;
  setPreference: (preference: ThemePreference) => void;
  /** Alterna para o outro tema e passa a valer como escolha explicita. */
  toggle: () => void;
}

export function useTheme(): UseTheme {
  const preference = useSyncExternalStore(subscribeTheme, getThemePreference, getThemePreference);
  const theme = useSyncExternalStore(subscribeTheme, getResolvedTheme, getResolvedTheme);

  const toggle = useCallback(() => {
    setThemePreference(getResolvedTheme() === "dark" ? "light" : "dark");
  }, []);

  return useMemo(
    () => ({ preference, theme, setPreference: setThemePreference, toggle }),
    [preference, theme, toggle],
  );
}
