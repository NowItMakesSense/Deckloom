// T-L03 / REQ-L06 v1: a home usa a identidade "Grimorio arcano" v1.0: so tokens (sem cor ou fonte literal) e vidro
// com solido de reserva.
import { describe, expect, it } from "vitest";

import {
  findCssViolations,
  findLiteralViolations,
  findTsxLiteralViolations,
  formatViolations,
} from "@/shared/testing/cssChecks";

const css = import.meta.glob("./*.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;
const tsx = import.meta.glob(["./*.tsx", "!./*.test.tsx"], {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

describe("features/inicio: so tokens", () => {
  it("a varredura encontra o CSS e o TSX da home", () => {
    expect(Object.keys(css).length).toBeGreaterThan(0);
    expect(Object.keys(tsx).length).toBeGreaterThan(0);
  });

  it("o CSS nao tem cor nem fonte literal e o vidro segue a receita", () => {
    const violations = Object.entries(css).flatMap(([path, source]) => [
      ...findLiteralViolations(source, path),
      ...findCssViolations(source, path),
    ]);
    expect(formatViolations(violations)).toBe("");
  });

  it("o TSX nao tem cor nem fonte literal", () => {
    const violations = Object.entries(tsx).flatMap(([path, source]) =>
      findTsxLiteralViolations(source, path),
    );
    expect(formatViolations(violations)).toBe("");
  });
});
