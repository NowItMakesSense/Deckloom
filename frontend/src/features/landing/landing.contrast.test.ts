// T-L02 / AC-L02i (REQ-L06): texto AA (4,5:1) na landing, nos dois temas, inclusive sobre o PIOR ponto do fundo
// animado. O texto da landing so fica em tres materiais: vidro forte (compondo o rgba sobre o pior fundo previsto,
// --glass-worst, e sobre o solido de reserva), faixas opacas dos cards e o rodape invertido. Cada par abaixo e um
// par que o CSS da landing realmente usa.
import { describe, expect, it } from "vitest";

import { normalize, parseCss, readAllCss } from "@/shared/testing/cssScan";

type Rgb = readonly [number, number, number];
const TOKENS = readAllCss()["/src/design-system/tokens.css"];

function collect(selector: string): Map<string, string> {
  const map = new Map<string, string>();
  for (const rule of parseCss(TOKENS).rules) {
    if (rule.media.length > 0 || rule.selector.trim() !== selector) continue;
    for (const { property, value } of rule.declarations) {
      if (property.startsWith("--")) map.set(property, normalize(value));
    }
  }
  return map;
}

function theme(name: "light" | "dark"): (token: string) => string {
  const light = collect(":root");
  const merged =
    name === "light" ? light : new Map([...light, ...collect(':root[data-theme="dark"]')]);
  return (token) => {
    const raw = merged.get(token);
    if (raw === undefined) throw new Error(`token ausente: ${token}`);
    const alias = /^var\((--[\w-]+)\)$/.exec(raw);
    return alias ? (merged.get(alias[1]) ?? raw) : raw;
  };
}

function hex(value: string): Rgb {
  const match = /^#([0-9a-f]{6})$/.exec(value);
  if (!match) throw new Error(`cor hexadecimal esperada: ${value}`);
  const n = parseInt(match[1], 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

/** Cor opaca de um token: hex, ou rgba() composto sobre `backdrop`. */
function resolve(value: string, backdrop: Rgb): Rgb {
  const rgba = /^rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)$/.exec(value);
  if (!rgba) return hex(value);
  const alpha = Number(rgba[4]);
  const top: Rgb = [Number(rgba[1]), Number(rgba[2]), Number(rgba[3])];
  return top.map((channel, i) =>
    Math.round(channel * alpha + backdrop[i] * (1 - alpha)),
  ) as unknown as Rgb;
}

function luminance([r, g, b]: Rgb): number {
  const [lr, lg, lb] = [r, g, b].map((channel) => {
    const c = channel / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
}

function contrast(a: Rgb, b: Rgb): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

// [onde o par aparece, tinta, fundo, o que existe atras do fundo (vidro) ou null se opaco]
const PAIRS: ReadonlyArray<readonly [string, string, string, string | null]> = [
  [
    "hero, painel dos jogos, nav e legenda (texto sobre vidro forte, pior fundo)",
    "--color-text",
    "--glass-bg-strong",
    "--glass-worst",
  ],
  [
    "link e CTA secundario na nav sobre vidro forte (pior fundo)",
    "--color-text",
    "--glass-bg-strong",
    "--glass-worst",
  ],
  ["vidro forte no fallback sem backdrop-filter", "--color-text", "--glass-solid-strong", null],
  ["titulo do card (faixa de titulo)", "--color-on-primary-subtle", "--color-primary-subtle", null],
  ["linha de tipo e selo Em breve", "--color-on-gold-subtle", "--color-gold-subtle", null],
  ["caixa de texto do card", "--color-text", "--color-surface", null],
  [
    "nota do exemplo e contagens (apoio) na caixa de texto",
    "--color-text-muted",
    "--color-surface",
    null,
  ],
  ["pedido de exemplo da IA", "--color-on-primary-subtle", "--color-primary-subtle", null],
  ["Menor preco", "--color-success", "--color-success-subtle", null],
  ["Criar conta (botao primario)", "--color-on-primary", "--color-primary", null],
  ["rodape invertido", "--color-moon", "--color-night", null],
  ["planos: titulo sobre o fundo da pagina", "--color-text", "--color-ground", null],
  ["planos: texto de apoio sobre o fundo da pagina", "--color-text-muted", "--color-ground", null],
  [
    "planos: botao secundario (texto sobre violeta suave)",
    "--color-on-primary-subtle",
    "--color-primary-subtle",
    null,
  ],
  ["planos: botao primario", "--color-on-primary", "--color-primary", null],
  ["planos: selo Pago", "--color-on-gold-subtle", "--color-gold-subtle", null],
];

describe.each(["light", "dark"] as const)("contraste AA da landing no tema %s", (name) => {
  const token = theme(name);

  it.each(PAIRS)("%s", (_where, ink, background, behind) => {
    const backdrop = behind ? hex(token(behind)) : hex(token("--color-ground"));
    const ratio = contrast(hex(token(ink)), resolve(token(background), backdrop));

    expect(ratio, `${ink} sobre ${background}`).toBeGreaterThanOrEqual(4.5);
  });

  it("o vidro forte tambem passa sobre o fundo comum da pagina (nao so no pior fundo)", () => {
    const ground = hex(token("--color-ground"));
    const ratio = contrast(hex(token("--color-text")), resolve(token("--glass-bg-strong"), ground));

    expect(ratio).toBeGreaterThanOrEqual(4.5);
  });
});
