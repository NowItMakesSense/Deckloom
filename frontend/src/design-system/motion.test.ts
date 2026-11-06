// T-096 (RED): criterios de movimento por checagem estatica de CSS (design.md A.8: MOT-01/02/05/07/08/09).
// O CSS dos componentes ainda nao existe: cada teste falha com mensagem clara ate o GREEN criar o arquivo.
import { describe, expect, it } from "vitest";

import { normalize, parseCss, readAllCss, type CssRule } from "../shared/testing/cssScan";

const files = readAllCss();

function rulesOf(name: string): CssRule[] {
  const css = files[`/src/design-system/${name}`];
  expect(
    css,
    `arquivo ausente: src/design-system/${name} (T-096 GREEN deve cria-lo)`,
  ).toBeDefined();
  return parseCss(css).rules;
}

/** Valores de :root em tokens.css, para resolver var(--x) nas duracoes. */
function tokens(): Record<string, string> {
  const root = parseCss(files["/src/design-system/tokens.css"] ?? "").rules.find(
    (r) => r.selector === ":root" && r.media.length === 0,
  );
  return Object.fromEntries((root?.declarations ?? []).map((d) => [d.property, d.value]));
}

function resolve(value: string): string {
  const map = tokens();
  return normalize(value).replace(
    /var\((--[\w-]+)\)/g,
    (match, name: string) => map[name] ?? match,
  );
}

function declared(rules: CssRule[], selector: RegExp, property: string, reduced = false) {
  return rules
    .filter(
      (r) =>
        selector.test(r.selector) &&
        r.media.some((m) => normalize(m).includes("prefers-reduced-motion: reduce")) === reduced,
    )
    .flatMap((r) => r.declarations.filter((d) => d.property === property));
}

function expectSpin(rules: CssRule[], selector: RegExp): void {
  const animation = declared(rules, selector, "animation").map((d) => resolve(d.value));
  expect(animation.length, `sem 'animation' em regra ${selector}`).toBeGreaterThan(0);
  expect(
    animation.some((v) => /\b800ms\b/.test(v) && /\blinear\b/.test(v) && /infinite/.test(v)),
    `animation deve ser 800ms linear infinite: ${animation.join(" | ")}`,
  ).toBe(true);
  const spin = rules.find(
    (r) =>
      r.keyframes !== null &&
      r.declarations.some((d) => d.property === "transform" && /rotate\(/.test(d.value)),
  );
  expect(spin, "sem @keyframes com transform: rotate").toBeDefined();
}

function expectReducedMotionStops(rules: CssRule[], selector: RegExp): void {
  const stops = declared(rules, selector, "animation", true).filter(
    (d) => normalize(d.value) === "none",
  );
  expect(
    stops.length,
    "faltou @media (prefers-reduced-motion: reduce) com animation: none",
  ).toBeGreaterThan(0);
}

describe("MOT-01: pressionaveis respondem ao toque", () => {
  it("--duration-press vale 120ms em tokens.css", () => {
    expect(tokens()["--duration-press"]).toBe("120ms");
  });

  it("Button.css: :active { transform: scale(0.97) } e transition de transform em 120ms", () => {
    const rules = rulesOf("Button.css");

    const active = rules.filter((r) => /:active/.test(r.selector) && r.media.length === 0);
    expect(active.flatMap((r) => r.declarations)).toContainEqual({
      property: "transform",
      value: "scale(0.97)",
    });
    const transitions = rules
      .flatMap((r) => r.declarations.filter((d) => d.property === "transition"))
      .map((d) => resolve(d.value));
    expect(transitions.some((v) => /transform 120ms/.test(v))).toBe(true);
  });
});

describe("MOT-02: hover so em cor, 150ms, so com ponteiro fino", () => {
  it.each(["Button.css", "Link.css"])("%s", (file) => {
    const rules = rulesOf(file);

    const hover = rules.filter((r) => /:hover/.test(r.selector));
    expect(hover.length, "sem regra :hover").toBeGreaterThan(0);
    for (const rule of hover) {
      const media = normalize(rule.media.join(" "));
      expect(media).toContain("(hover: hover)");
      expect(media).toContain("(pointer: fine)");
    }
    const transitions = rules.flatMap((r) =>
      r.declarations.filter((d) => d.property === "transition"),
    );
    expect(transitions.some((d) => /150ms/.test(resolve(d.value)))).toBe(true);
  });
});

describe("MOT-05: Alert entra com opacity 0 a 1 em 160ms --ease-out", () => {
  it("Alert.css anima so opacity, sem deslocamento, em 160ms", () => {
    const rules = rulesOf("Alert.css");

    const keyframes = rules.filter((r) => r.keyframes !== null);
    expect(keyframes.length, "sem @keyframes de entrada").toBeGreaterThan(0);
    for (const frame of keyframes) {
      expect(frame.declarations.map((d) => d.property).every((p) => p === "opacity")).toBe(true);
    }
    const animation = declared(rules, /alert/i, "animation").map((d) => resolve(d.value));
    expect(
      animation.some((v) => /\b160ms\b/.test(v) && /cubic-bezier\(0\.23, 1, 0\.32, 1\)/.test(v)),
      `animation do Alert deve usar 160ms e --ease-out: ${animation.join(" | ")}`,
    ).toBe(true);
  });
});

describe("MOT-07: indicador de loading do Button gira a cada 800ms", () => {
  it("Button.css: rotacao linear infinita de 800ms e reduced-motion sem animacao", () => {
    const rules = rulesOf("Button.css");

    expectSpin(rules, /spinner|loading|busy/i);
    expectReducedMotionStops(rules, /spinner|loading|busy/i);
  });
});

describe("MOT-08: Spinner gira a cada 800ms", () => {
  it("Spinner.css: rotacao linear infinita de 800ms e reduced-motion sem animacao", () => {
    const rules = rulesOf("Spinner.css");

    expectSpin(rules, /spinner/i);
    expectReducedMotionStops(rules, /spinner/i);
  });
});

describe("MOT-09: Skeleton e plano, sem brilho passando", () => {
  it("Skeleton.css nao tem animation, transition nem @keyframes", () => {
    const rules = rulesOf("Skeleton.css");

    expect(rules.length, "Skeleton.css sem regras").toBeGreaterThan(0);
    expect(rules.filter((r) => r.keyframes !== null)).toEqual([]);
    const motion = rules.flatMap((r) =>
      r.declarations.filter(
        (d) => /^(animation|transition)/.test(d.property) && normalize(d.value) !== "none",
      ),
    );
    expect(motion).toEqual([]);
  });
});
