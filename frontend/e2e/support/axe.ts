import AxeBuilder from "@axe-core/playwright";
import type { Page } from "@playwright/test";

// T-MAINT-003: o axe so pode medir a pagina em repouso. Componentes como o Alert entram com animacao
// (alert-in, ver design-system/Alert.css); medido no meio do fade, o contraste sai com cores intermediarias
// e o teste falha de forma intermitente (CI da main, run 42300957006: 4.28:1 no Alert de sucesso).
// Animacoes infinitas (ex.: skeleton) nao terminam e ficam de fora da espera.
export async function settledAxe(page: Page): Promise<AxeBuilder> {
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (animation) =>
          animation.playState !== "running" ||
          animation.effect?.getComputedTiming().iterations === Infinity,
      ),
  );
  return new AxeBuilder({ page });
}
