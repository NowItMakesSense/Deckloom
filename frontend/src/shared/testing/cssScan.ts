// Leitor minimo de CSS usado apenas pelos testes de regras de design (T-095).
// Sem dependencias: entende regras, @media/@supports/@layer aninhados e @keyframes.

export interface CssDeclaration {
  property: string;
  value: string;
}

export interface CssRule {
  /** Seletor completo, como escrito (lista separada por virgulas). */
  selector: string;
  /** Preludios dos at-rules condicionais que envolvem a regra, do mais externo ao mais interno. */
  media: string[];
  declarations: CssDeclaration[];
  /** Nome do @keyframes quando a regra e um quadro de animacao. */
  keyframes: string | null;
}

export interface ParsedCss {
  rules: CssRule[];
  /** At-rules sem bloco, por exemplo @import e @charset. */
  statements: string[];
}

export function normalize(value: string): string {
  return value.replace(/\s+/g, " ").trim().toLowerCase();
}

function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "");
}

/** Divide `text` em `separator` ignorando separadores dentro de parenteses ou aspas. */
export function splitTop(text: string, separator: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let quote: string | null = null;
  let current = "";
  for (const char of text) {
    if (quote) {
      current += char;
      if (char === quote) quote = null;
      continue;
    }
    if (char === '"' || char === "'") {
      quote = char;
      current += char;
      continue;
    }
    if (char === "(" || char === "[") depth += 1;
    if (char === ")" || char === "]") depth -= 1;
    if (char === separator && depth === 0) {
      parts.push(current);
      current = "";
      continue;
    }
    current += char;
  }
  parts.push(current);
  return parts;
}

function readPrelude(source: string, start: number): { end: number; terminator: string } {
  let depth = 0;
  let quote: string | null = null;
  for (let index = start; index < source.length; index += 1) {
    const char = source[index];
    if (quote) {
      if (char === quote) quote = null;
      continue;
    }
    if (char === '"' || char === "'") quote = char;
    else if (char === "(" || char === "[") depth += 1;
    else if (char === ")" || char === "]") depth -= 1;
    else if (depth === 0 && (char === "{" || char === ";" || char === "}")) {
      return { end: index, terminator: char };
    }
  }
  return { end: source.length, terminator: "" };
}

function matchBrace(source: string, openIndex: number): number {
  let depth = 0;
  let quote: string | null = null;
  for (let index = openIndex; index < source.length; index += 1) {
    const char = source[index];
    if (quote) {
      if (char === quote) quote = null;
      continue;
    }
    if (char === '"' || char === "'") quote = char;
    else if (char === "{") depth += 1;
    else if (char === "}") {
      depth -= 1;
      if (depth === 0) return index;
    }
  }
  throw new Error("CSS invalido: chave '{' sem fechamento");
}

function parseDeclarations(body: string): CssDeclaration[] {
  if (body.includes("{")) {
    throw new Error(
      "CSS aninhado dentro de regra nao e suportado pelo scanner de regras de design",
    );
  }
  const declarations: CssDeclaration[] = [];
  for (const chunk of splitTop(body, ";")) {
    const colon = chunk.indexOf(":");
    if (colon < 0) continue;
    const rawProperty = chunk.slice(0, colon).trim();
    const property = rawProperty.startsWith("--") ? rawProperty : rawProperty.toLowerCase();
    const value = chunk
      .slice(colon + 1)
      .replace(/!\s*important/gi, "")
      .replace(/\s+/g, " ")
      .trim();
    if (property) declarations.push({ property, value });
  }
  return declarations;
}

const CONDITIONAL_AT_RULES = /^@(media|supports|layer|container|scope)\b/i;

function parseInto(
  source: string,
  media: string[],
  keyframes: string | null,
  out: ParsedCss,
): void {
  let index = 0;
  while (index < source.length) {
    while (index < source.length && /\s/.test(source[index])) index += 1;
    if (index >= source.length) break;
    const { end, terminator } = readPrelude(source, index);
    const prelude = source.slice(index, end).replace(/\s+/g, " ").trim();
    if (terminator === ";" || terminator === "") {
      if (prelude) out.statements.push(prelude);
      index = end + 1;
      continue;
    }
    if (terminator === "}") {
      index = end + 1;
      continue;
    }
    const close = matchBrace(source, end);
    const body = source.slice(end + 1, close);
    if (CONDITIONAL_AT_RULES.test(prelude)) {
      parseInto(body, [...media, prelude], keyframes, out);
    } else if (/^@(-\w+-)?keyframes\b/i.test(prelude)) {
      parseInto(body, media, prelude.replace(/^@\S+\s+/, ""), out);
    } else {
      out.rules.push({
        selector: prelude,
        media,
        declarations: parseDeclarations(body),
        keyframes,
      });
    }
    index = close + 1;
  }
}

export function parseCss(source: string): ParsedCss {
  const out: ParsedCss = { rules: [], statements: [] };
  parseInto(stripComments(source), [], null, out);
  return out;
}

export function selectorParts(selector: string): string[] {
  return splitTop(selector, ",").map((part) => part.replace(/\s+/g, " ").trim());
}

/** Seletor sem pseudo-classes e pseudo-elementos: `.btn:active:not(:disabled)` vira `.btn`. */
export function selectorBase(part: string): string {
  return part
    .replace(/::?[a-zA-Z-]+(\((?:[^()]|\([^()]*\))*\))?/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

/** Todo o CSS do frontend (`src/**\/*.css`), por caminho. */
export function readAllCss(): Record<string, string> {
  return import.meta.glob("/src/**/*.css", {
    query: "?raw",
    import: "default",
    eager: true,
  }) as Record<string, string>;
}

/** Fontes TypeScript/TSX de producao (sem testes nem suporte de teste), por caminho. */
export function readProductionSources(): Record<string, string> {
  const all = import.meta.glob(["/src/**/*.ts", "/src/**/*.tsx"], {
    query: "?raw",
    import: "default",
    eager: true,
  }) as Record<string, string>;
  return Object.fromEntries(
    Object.entries(all).filter(
      ([path]) => !/\.test\.tsx?$/.test(path) && !path.includes("/shared/testing/"),
    ),
  );
}
