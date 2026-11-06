// T-155 (RED, R-27): higiene - o componente raiz orfao App.tsx nao deve existir.
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

const SRC = "src";

function productionSources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return productionSources(path);
    const isSource = /\.(ts|tsx)$/.test(name) && !/\.test\.(ts|tsx)$/.test(name);
    return isSource ? [path] : [];
  });
}

describe("codigo morto (R-27)", () => {
  it("App.tsx e App.test.tsx nao existem", () => {
    expect(existsSync(join(SRC, "app", "App.tsx"))).toBe(false);
    expect(existsSync(join(SRC, "app", "App.test.tsx"))).toBe(false);
  });

  it("main.tsx monta AppRoutes", () => {
    const main = readFileSync(join(SRC, "main.tsx"), "utf8");
    expect(main).toMatch(/<AppRoutes\s*\/>/);
  });

  it("nenhum modulo de producao importa ./App", () => {
    const importers = productionSources(SRC).filter((file) =>
      /from\s+["'](?:\.\/|\.\.\/|@\/app\/)App["']/.test(readFileSync(file, "utf8")),
    );
    expect(importers).toEqual([]);
  });
});
