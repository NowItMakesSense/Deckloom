// Regras de direcao de arte e acessibilidade aplicadas ao CSS (T-095, design.md A.8 / A.8.7), na v1.0 do
// Grimorio arcano (docs/design-system.md 1.2, 1.6 e 5): vidro com sólido de reserva, gradiente so na textura de
// fantasia, sem caixa alta forcada, e nenhuma cor ou fonte literal fora dos tokens (findLiteralViolations).
import { normalize, parseCss, selectorBase, selectorParts, type CssRule } from "./cssScan";

export interface Violation {
  id: string;
  where: string;
  message: string;
}

const HOVER_MEDIA = ["(hover: hover)", "(pointer: fine)"];
const RADIUS_VALUE = /^(0|4px|6px|8px|9999px|50%|var\(--radius-(sm|md|lg|full)\))$/;
const ALLOWED_SHADOW_TOKENS = ["--shadow-menu", "--shadow-dialog"];
const FLOATING_SELECTOR = /account-?menu|dialog/i;
const TRANSITION_PROPERTIES = new Set([
  "transform",
  "opacity",
  "background-color",
  "color",
  "border-color",
]);
const HOVER_PROPERTIES = new Set([
  "color",
  "background-color",
  "border-color",
  "text-decoration",
  "text-decoration-color",
  "cursor",
]);
const EASINGS =
  /^(ease|linear|ease-out|ease-in-out|var\(--ease-(out|in-out)\)|cubic-bezier\([^)]*\))$/;
// Vidro (1.6.1): a unica forma permitida de backdrop-filter, sempre pelos tokens, com sólido de reserva.
const GLASS_FILTER = /^blur\(var\(--glass-blur\)\) saturate\(var\(--glass-saturate\)\)$/;
// Textura de fantasia (1.6.4): gradiente radial so no fundo da pagina ou em elemento de textura (classe *backdrop*).
const TEXTURE_SELECTOR = /^(html|body|:root|\.[\w-]*backdrop[\w-]*)$/;
const TIME = /^\d*\.?\d+m?s$/;
const CONTROL_SELECTOR =
  /(^|[^\w-])(button|input|select|textarea)\b|\.[\w-]*(button|btn|field|control|input|select)[\w-]*/i;
const PRESSABLE_SELECTOR =
  /(^|[^\w-])button\b|\.[\w-]*(button|btn|tile|pressable)[\w-]*|\[role=["']?button/i;
const PRESSABLE_EXCLUDED = /group|label|icon|content|spinner|text/i;
const TARGET_MIN_PX = 44;

function where(rule: CssRule, file: string): string {
  const media = rule.media.length > 0 ? ` [${rule.media.join(" | ")}]` : "";
  return `${file}: ${rule.selector}${media}`;
}

function toPx(value: string): number | null {
  const match = /^(-?\d*\.?\d+)(px|rem)$/.exec(normalize(value));
  if (!match) return null;
  return Number(match[1]) * (match[2] === "rem" ? 16 : 1);
}

function transitionProperties(value: string): string[] {
  return splitTransition(value).map((segment) => {
    const tokens: string[] = [];
    let depth = 0;
    let current = "";
    for (const char of segment) {
      if (char === "(") depth += 1;
      if (char === ")") depth -= 1;
      if (char === " " && depth === 0) {
        if (current) tokens.push(current);
        current = "";
      } else current += char;
    }
    if (current) tokens.push(current);
    const property = tokens.filter(
      (token) =>
        !TIME.test(token) && !EASINGS.test(token) && !/^var\(--duration-[\w-]+\)$/.test(token),
    );
    return property.length === 0 ? "all" : property.join(" ");
  });
}

function splitTransition(value: string): string[] {
  const out: string[] = [];
  let depth = 0;
  let current = "";
  for (const char of normalize(value)) {
    if (char === "(") depth += 1;
    if (char === ")") depth -= 1;
    if (char === "," && depth === 0) {
      out.push(current.trim());
      current = "";
    } else current += char;
  }
  out.push(current.trim());
  return out;
}

function maxDurationMs(value: string): number {
  const durations = [...normalize(value).matchAll(/(?<![\w-])(\d*\.?\d+)(ms|s)\b/g)].map((m) =>
    m[2] === "s" ? Number(m[1]) * 1000 : Number(m[1]),
  );
  return durations.length === 0 ? 0 : Math.max(...durations);
}

function hasAlphaBelowOne(value: string): boolean {
  const v = normalize(value);
  if (/#[0-9a-f]{8}\b/.test(v) && !/#[0-9a-f]{6}ff\b/.test(v)) return true;
  if (/#[0-9a-f]{4}\b/.test(v) && !/#[0-9a-f]{3}f\b/.test(v)) return true;
  const fn = /(rgba|hsla)\(([^)]*)\)/.exec(v);
  if (fn) {
    const alpha = fn[2].split(",")[3];
    return alpha !== undefined && Number(alpha) < 1;
  }
  return /(rgb|hsl)\([^)]*\/\s*(0?\.\d+|\d+%)\s*\)/.test(v);
}

export function findCssViolations(css: string, file = "<css>"): Violation[] {
  const { rules, statements } = parseCss(css);
  const violations: Violation[] = [];
  const add = (id: string, rule: CssRule | null, message: string) =>
    violations.push({ id, where: rule ? where(rule, file) : file, message });

  for (const statement of statements) {
    if (/https?:\/\//i.test(statement) || /^@import/i.test(statement)) {
      add("EXT", null, `recurso externo ou @import: ${statement}`);
    }
  }

  for (const rule of rules) {
    const media = normalize(rule.media.join(" "));
    const parts = selectorParts(rule.selector);

    // ART-06 / A11Y-32: hover somente com ponteiro fino e so em cor.
    if (/:hover/.test(rule.selector)) {
      if (!HOVER_MEDIA.every((condition) => media.includes(condition))) {
        add("ART-06", rule, ":hover fora de @media (hover: hover) and (pointer: fine)");
      }
      for (const { property } of rule.declarations) {
        if (!HOVER_PROPERTIES.has(property)) {
          add("A11Y-32", rule, `:hover altera '${property}' (conteudo/estado so por hover)`);
        }
      }
    }

    // A11Y-37: movimento reduzido sem rotacao nem escala.
    if (media.includes("prefers-reduced-motion: reduce")) {
      for (const { property, value } of rule.declarations) {
        const v = normalize(value);
        if (/^(transform|rotate|scale|translate)$/.test(property) && v !== "none") {
          if (/(rotate|scale|translate)/.test(`${property} ${v}`)) {
            add("A11Y-37", rule, `reduced-motion mantem ${property}: ${value}`);
          }
        }
        if (property === "animation" && v !== "none") {
          add("A11Y-37", rule, `reduced-motion mantem animation: ${value}`);
        }
      }
    }

    for (const { property, value } of rule.declarations) {
      const v = normalize(value);
      const isCustom = property.startsWith("--");

      // ART-01
      if (/gradient\(/.test(v)) {
        const radialOnly = /radial-gradient\(/.test(v) && !/(linear|conic)-gradient\(/.test(v);
        const onTexture =
          /^background(-image)?$/.test(property) &&
          parts.every((part) => TEXTURE_SELECTOR.test(selectorBase(part)));
        if (!(radialOnly && onTexture)) {
          add(
            "ART-01",
            rule,
            `gradiente fora da textura de fantasia (radial no fundo): ${property}`,
          );
        }
      }
      if (property === "backdrop-filter" || property === "-webkit-backdrop-filter") {
        if (!GLASS_FILTER.test(v)) {
          add("ART-01", rule, `backdrop-filter fora da receita do vidro: ${value}`);
        }
      }
      if (property === "text-shadow") add("ART-01", rule, "text-shadow (brilho)");
      if (/^(-webkit-)?filter$/.test(property) && /(blur|drop-shadow|brightness)\(/.test(v)) {
        add("ART-01", rule, `filter com brilho/desfoque: ${value}`);
      }
      if (property === "box-shadow") {
        const isToken = ALLOWED_SHADOW_TOKENS.some((token) => v === `var(${token})`);
        const floating = parts.every((part) => FLOATING_SELECTOR.test(part));
        if (v !== "none" && !(isToken && floating)) {
          add("ART-01", rule, `sombra fora do AccountMenu/Dialog: ${value}`);
        }
      }
      if (
        isCustom &&
        property.startsWith("--shadow-") &&
        !ALLOWED_SHADOW_TOKENS.includes(property)
      ) {
        add("ART-01", rule, `token de sombra nao previsto: ${property}`);
      }
      if (/drop-shadow\(/.test(v) && property !== "filter") {
        add("ART-01", rule, `drop-shadow em ${property}`);
      }

      // ART-03
      if (isCustom && property.startsWith("--radius-")) {
        const allowed: Record<string, string> = {
          "--radius-sm": "4px",
          "--radius-md": "6px",
          "--radius-lg": "8px",
          "--radius-full": "9999px",
        };
        if (allowed[property] !== v) add("ART-03", rule, `${property}: ${value} fora da escala`);
      } else if (!isCustom && /radius$/.test(property)) {
        for (const token of v.replace(/\//g, " ").split(" ")) {
          if (!RADIUS_VALUE.test(token))
            add("ART-03", rule, `raio fora de {4,6,8,pleno}: ${token}`);
        }
      }

      // ART-07
      if (property === "will-change") add("ART-07", rule, "will-change preventivo");
      if (property === "transition" || property === "transition-property") {
        for (const prop of transitionProperties(
          property === "transition" ? value : `${value} 0s`,
        )) {
          if (prop === "all") add("ART-07", rule, `transition: all (${property}: ${value})`);
          else if (!prop.split(" ").every((p) => TRANSITION_PROPERTIES.has(p))) {
            add("ART-07", rule, `propriedade animada nao permitida: ${prop}`);
          }
        }
        if (property === "transition" && maxDurationMs(value) >= 300) {
          add("ART-07", rule, `duracao de transicao >= 300ms: ${value}`);
        }
        if (/(^|[\s,(])ease-in(?![\w-])/.test(v)) add("ART-07", rule, "easing ease-in em UI");
      }

      // ART-04
      if (property === "text-transform" && v === "uppercase") {
        add("ART-04", rule, "caixa alta forcada (text-transform; 1.2: versaletes ja vem da fonte)");
      }
      if (property === "letter-spacing" && !/^(0|0px|0em|normal)$/.test(v)) {
        add("ART-04", rule, `letter-spacing diferente de 0: ${value}`);
      }

      // A11Y-09
      if (
        (property === "outline" && /^(none|0)(\s|$)/.test(v)) ||
        (property === "outline-style" && v === "none") ||
        (property === "outline-width" && /^0(px)?$/.test(v))
      ) {
        const replaced = parts.every((part) => /:focus:not\(:focus-visible\)/.test(part));
        if (!replaced) add("A11Y-09", rule, `outline removido sem substituto: ${value}`);
      }

      // A11Y-39: nenhuma superficie translucida alem do scrim.
      if (
        (/^background(-color)?$/.test(property) ||
          (isCustom && property.startsWith("--color-") && property !== "--color-scrim")) &&
        hasAlphaBelowOne(v)
      ) {
        add("A11Y-39", rule, `superficie translucida em ${property}: ${value}`);
      }

      // Recursos externos (U15 / LGPD).
      if (/url\(\s*["']?(https?:)?\/\//i.test(v)) add("EXT", rule, `recurso externo: ${value}`);

      // Tipografia: nada abaixo de 14px.
      if (property === "font-size") {
        const px = toPx(v);
        if (px !== null && px < 14) add("TYPE-MIN", rule, `fonte abaixo de 14px: ${value}`);
      }

      // ART-08 / A11Y-36: controles com altura minima de 44px.
      if (/^(height|min-height|block-size|min-block-size)$/.test(property)) {
        const px = toPx(v);
        const isControl =
          parts.some((part) => CONTROL_SELECTOR.test(selectorBase(part))) &&
          !/checkbox|radio/i.test(rule.selector);
        if (isControl && px !== null && px < TARGET_MIN_PX) {
          add("ART-08", rule, `controle com ${property} ${value} (< 44px)`);
        }
      }
    }
  }

  // Vidro (1.6.3): todo arquivo com backdrop-filter traz o sólido opaco de reserva em @supports not.
  if (rules.some((r) => r.declarations.some((d) => /backdrop-filter$/.test(d.property)))) {
    const fallback = rules.filter((r) =>
      r.media.some((m) => /@supports\s+not\s*\(+\s*(-webkit-)?backdrop-filter/.test(m)),
    );
    const solid = fallback.some((r) => JSON.stringify(r.declarations).includes("--glass-solid"));
    if (!solid) {
      violations.push({
        id: "ART-01",
        where: file,
        message: "backdrop-filter sem fundo solido de reserva (@supports not + --glass-solid)",
      });
    }
  }

  // ART-06: todo pressionavel tem :active { transform: scale(0.97) } com o tempo de pressao.
  const pressables = new Set<string>();
  for (const rule of rules) {
    for (const part of selectorParts(rule.selector)) {
      const base = selectorBase(part);
      if (base && PRESSABLE_SELECTOR.test(base) && !PRESSABLE_EXCLUDED.test(base)) {
        pressables.add(base);
      }
    }
  }
  for (const base of pressables) {
    const group = rules.filter((rule) =>
      selectorParts(rule.selector).some((part) => selectorBase(part) === base),
    );
    const active = group.filter((rule) =>
      selectorParts(rule.selector).some(
        (part) => selectorBase(part) === base && /:active/.test(part),
      ),
    );
    const scales = active.some((rule) =>
      rule.declarations.some(
        (d) => d.property === "transform" && normalize(d.value) === "scale(0.97)",
      ),
    );
    if (!scales) {
      violations.push({
        id: "ART-06",
        where: `${file}: ${base}`,
        message: "pressionavel sem :active { transform: scale(0.97) }",
      });
    }
    const pressTransition = group.some((rule) =>
      rule.declarations.some(
        (d) =>
          d.property === "transition" &&
          /transform/.test(d.value) &&
          /var\(--duration-press\)/.test(d.value) &&
          /var\(--ease-out\)/.test(d.value),
      ),
    );
    if (!pressTransition) {
      violations.push({
        id: "ART-06",
        where: `${file}: ${base}`,
        message: "pressionavel sem transition de transform com --duration-press e --ease-out",
      });
    }
  }

  // ART-07: @keyframes so anima transform e opacity.
  for (const rule of rules) {
    if (rule.keyframes) {
      for (const { property } of rule.declarations) {
        if (property !== "transform" && property !== "opacity") {
          add("ART-07", rule, `@keyframes ${rule.keyframes} anima '${property}'`);
        }
      }
    }
  }

  return violations;
}

const NAMED_COLORS = new Set(
  (
    "black white red green blue yellow orange purple violet gold gray grey silver brown pink cyan magenta " +
    "lime navy teal maroon olive aqua fuchsia indigo crimson coral salmon khaki tan beige ivory lavender " +
    "plum orchid turquoise tomato azure snow linen"
  ).split(" "),
);
const FONT_VALUE_OK = /^(inherit|initial|unset|revert|var\(--font-(display|sans)\))$/;

/**
 * Cor e fonte literais (v1.0): fora de tokens.css e fonts.css, cor so por `var(--...)`, `transparent` ou `currentcolor`
 * e fonte so por `var(--font-display)`, `var(--font-sans)` ou `inherit`. Tokens e @font-face ficam de fora da varredura.
 */
export function findLiteralViolations(css: string, file = "<css>"): Violation[] {
  const { rules } = parseCss(css);
  const violations: Violation[] = [];
  for (const rule of rules) {
    for (const { property, value } of rule.declarations) {
      const v = normalize(value);
      const add = (message: string) =>
        violations.push({ id: "TOKEN", where: `${file}: ${rule.selector}`, message });
      if (/#[0-9a-f]{3,8}\b/.test(v)) add(`cor hexadecimal em ${property}: ${value}`);
      if (/(^|[^\w-])(rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch)\(/.test(v)) {
        add(`cor por funcao em ${property}: ${value}`);
      }
      const withoutVars = v.replace(/var\([^)]*\)/g, " ");
      if (
        withoutVars.split(/[\s,/()]+/).some((word) => NAMED_COLORS.has(word)) &&
        !/^(font|grid|animation|content)/.test(property)
      ) {
        add(`cor nomeada em ${property}: ${value}`);
      }
      if (property === "font-family" && !splitList(v).every((part) => FONT_VALUE_OK.test(part))) {
        add(`font-family fora dos tokens: ${value}`);
      }
      if (property === "font" && !/^inherit$/.test(v)) add(`atalho font literal: ${value}`);
    }
  }
  return violations;
}

const COLOR_ATTRIBUTES = "fill|stroke|color|stopColor|floodColor|lightingColor";
const STRING_LITERAL = String.raw`"([^"]*)"|'([^']*)'|` + "`([^`]*)`";

function kebab(name: string): string {
  return name.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`);
}

/**
 * Cor e fonte literais no TSX (v1.0): cor hexadecimal ou por funcao em qualquer lugar do arquivo; atributos de cor
 * (`fill="red"`) e objetos `style={{ ... }}` passam pelas mesmas regras do CSS (cor nomeada, `fontFamily` fora dos
 * tokens, atalhos de fonte). Texto corrido nao entra: so atributos de cor e `style` inline sao lidos como CSS.
 */
export function findTsxLiteralViolations(source: string, file = "<tsx>"): Violation[] {
  const violations: Violation[] = [];
  const add = (message: string) => violations.push({ id: "TOKEN", where: file, message });

  const hex = /#[0-9a-fA-F]{3,8}\b/.exec(source);
  if (hex) add(`cor hexadecimal: ${hex[0]}`);
  const fn = /(^|[^\w-])(rgba?|hsla?|hwb|lab|lch|oklab|oklch)\(/.exec(source);
  if (fn) add(`cor por funcao: ${fn[0].trim()}`);

  const declarations: string[] = [];
  const attribute = new RegExp(String.raw`\b(${COLOR_ATTRIBUTES})=(?:${STRING_LITERAL})`, "g");
  for (const match of source.matchAll(attribute)) {
    declarations.push(`${kebab(match[1])}: ${match[2] ?? match[3] ?? match[4]}`);
  }
  for (const block of source.matchAll(/style=\{\{([\s\S]*?)\}\}/g)) {
    const pair = new RegExp(String.raw`(\w+)\s*:\s*(?:${STRING_LITERAL})`, "g");
    for (const match of block[1].matchAll(pair)) {
      declarations.push(`${kebab(match[1])}: ${match[2] ?? match[3] ?? match[4]}`);
    }
  }
  if (declarations.length > 0) {
    for (const violation of findLiteralViolations(`.x { ${declarations.join("; ")}; }`, file)) {
      add(violation.message);
    }
  }
  return violations;
}

function splitList(value: string): string[] {
  return value.split(",").map((part) => part.trim());
}

const EMOJI = /\p{Extended_Pictographic}/u;
const BANNED_WORDS = /lorem|acme|john doe|eyebrow/i;

/** ART-04: sem emoji, eyebrow, "lorem", "Acme" no codigo e nos textos de producao. */
export function findTextViolations(sources: Record<string, string>): Violation[] {
  const violations: Violation[] = [];
  for (const [file, text] of Object.entries(sources)) {
    const emoji = EMOJI.exec(text);
    if (emoji) violations.push({ id: "ART-04", where: file, message: `emoji: ${emoji[0]}` });
    const word = BANNED_WORDS.exec(text);
    if (word) violations.push({ id: "ART-04", where: file, message: `termo proibido: ${word[0]}` });
  }
  return violations;
}

export function formatViolations(violations: Violation[]): string {
  return violations.map((v) => `[${v.id}] ${v.where} - ${v.message}`).join("\n");
}
