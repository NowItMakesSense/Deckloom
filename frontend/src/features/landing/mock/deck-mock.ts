// Dados de exemplo da landing (T-L01, REQ-L01 v3): um deck, a colecao do jogador, lojas parceiras e o meta.
// Tudo inventado e marcado como exemplo na pagina. As contas ficam aqui para o teste (deck-mock.test.ts)
// garantir que tem + faltam = total antes e depois de cada "Tenho +1".
// T-L02: convertido para TypeScript sem mudar a logica; o HTML do custo de mana saiu (costMarkup interpolava
// texto sem escapar): o componente ManaCost renderiza o custo com JSX.

export interface DeckCard {
  readonly name: string;
  readonly need: number;
  readonly have: number;
  /** Custo no formato {2}{U}{B}; terreno nao tem custo. */
  readonly cost: string;
  readonly land?: boolean;
}

export interface Deck {
  readonly name: string;
  readonly game: string;
  /** Cores do deck (azul e preto). */
  readonly colors: readonly string[];
  readonly cards: readonly DeckCard[];
}

export const DECK: Deck = Object.freeze({
  name: "Revoada Noturna",
  game: "Magic: The Gathering",
  colors: Object.freeze(["U", "B"]),
  cards: Object.freeze([
    { name: "Corvo do Crepúsculo", need: 4, have: 4, cost: "{1}{B}" },
    { name: "Guardiã do Ninho", need: 4, have: 4, cost: "{2}{U}" },
    { name: "Lâmina Sussurrante", need: 3, have: 3, cost: "{1}{U}{B}" },
    { name: "Eco da Cripta", need: 4, have: 4, cost: "{B}" },
    { name: "Pacto Sombrio", need: 2, have: 2, cost: "{U}{B}" },
    { name: "Lanterna do Pântano", need: 2, have: 2, cost: "{2}" },
    { name: "Selo do Poente", need: 4, have: 4, cost: "{1}{U}" },
    { name: "Charco Silencioso", need: 20, have: 20, cost: "", land: true },
    { name: "Sentinela de Cinzas", need: 4, have: 1, cost: "{2}{B}" },
    { name: "Arauto da Névoa", need: 4, have: 2, cost: "{1}{U}" },
    { name: "Pena de Obsidiana", need: 2, have: 0, cost: "{3}{U}{B}" },
    { name: "Vigília da Lua Nova", need: 3, have: 1, cost: "{U}{U}" },
    { name: "Torre Esquecida", need: 4, have: 0, cost: "", land: true },
  ]),
});

// Custo de mana: "{2}{U}{B}" -> ["2", "u", "b"], os codigos das classes da fonte Mana (ms-2, ms-u, ms-b).
const MANA_NAMES: Readonly<Record<string, string>> = {
  W: "branco",
  U: "azul",
  B: "preto",
  R: "vermelho",
  G: "verde",
  C: "incolor",
};

export function manaSymbols(cost: string): string[] {
  if (!/^(\{([0-9]|[WUBRGC])\})*$/.test(cost)) throw new Error(`custo de mana invalido: ${cost}`);
  return [...cost.matchAll(/\{([^}]+)\}/g)].map((m) => m[1].toLowerCase());
}

export function manaLabel(cost: string): string {
  const parts = manaSymbols(cost).map((s) =>
    /^[0-9]$/.test(s) ? `${s} genérico` : MANA_NAMES[s.toUpperCase()],
  );
  return parts.length ? `Custo de mana: ${parts.join(", ")}` : "Terreno, sem custo de mana";
}

export function cardCost(name: string): string {
  const card = DECK.cards.find((c) => c.name === name);
  return card ? card.cost : "";
}

export interface IncompleteCard {
  readonly name: string;
  readonly need: number;
  readonly have: number;
  readonly missing: number;
}

export interface DeckSummary {
  readonly total: number;
  readonly have: number;
  readonly missing: number;
  readonly complete: { cards: number; copies: number };
  readonly incomplete: readonly IncompleteCard[];
}

export function summarize(deck: Deck): DeckSummary {
  let total = 0;
  let have = 0;
  const complete = { cards: 0, copies: 0 };
  const incomplete: IncompleteCard[] = [];
  for (const card of deck.cards) {
    total += card.need;
    have += card.have;
    if (card.have >= card.need) {
      complete.cards += 1;
      complete.copies += card.need;
    } else {
      incomplete.push({
        name: card.name,
        need: card.need,
        have: card.have,
        missing: card.need - card.have,
      });
    }
  }
  return { total, have, missing: total - have, complete, incomplete };
}

// Devolve um deck novo com uma copia a mais da carta (nunca passa do que o deck pede).
export function addOne(deck: Deck, name: string): Deck {
  return {
    ...deck,
    cards: deck.cards.map((card) =>
      card.name === name && card.have < card.need ? { ...card, have: card.have + 1 } : card,
    ),
  };
}

export interface Shop {
  readonly id: string;
  readonly name: string;
  /** Precos por carta, em centavos. */
  readonly prices: Readonly<Record<string, number>>;
}

// Lojas parceiras de exemplo (nomes e precos inventados, em centavos).
export const SHOPS: readonly Shop[] = Object.freeze([
  {
    id: "taverna",
    name: "Taverna das Cartas",
    prices: {
      "Sentinela de Cinzas": 740,
      "Arauto da Névoa": 1290,
      "Pena de Obsidiana": 3450,
      "Vigília da Lua Nova": 590,
      "Torre Esquecida": 2190,
    },
  },
  {
    id: "emporio",
    name: "Empório do Duelista",
    prices: {
      "Sentinela de Cinzas": 890,
      "Arauto da Névoa": 1150,
      "Pena de Obsidiana": 3290,
      "Vigília da Lua Nova": 650,
      "Torre Esquecida": 2390,
    },
  },
  {
    id: "bau",
    name: "Baú do Colecionador",
    prices: {
      "Sentinela de Cinzas": 820,
      "Arauto da Névoa": 1340,
      "Pena de Obsidiana": 3600,
      "Vigília da Lua Nova": 520,
      "Torre Esquecida": 2050,
    },
  },
]);

export interface QuoteLine {
  readonly name: string;
  readonly qty: number;
  readonly unit: number;
  readonly subtotal: number;
}

export function shopQuote(
  deck: Deck,
  shop: Shop,
): { readonly lines: readonly QuoteLine[]; readonly total: number } {
  const lines = summarize(deck).incomplete.map((card) => {
    const unit = shop.prices[card.name];
    return { name: card.name, qty: card.missing, unit, subtotal: card.missing * unit };
  });
  return { lines, total: lines.reduce((sum, line) => sum + line.subtotal, 0) };
}

// Alerta de queda de preco de uma carta que falta (o preco novo e o da tabela).
export const PRICE_ALERT = Object.freeze({
  card: "Sentinela de Cinzas",
  shop: "Taverna das Cartas",
  from: 990,
  to: 740,
});

export interface MetaRow {
  readonly name: string;
  readonly total: number;
  readonly have: number;
}

// Decks em alta nos torneios (exemplo) contra a colecao do jogador.
export const META: readonly MetaRow[] = Object.freeze([
  { name: "Aggro vermelho", total: 60, have: 52 },
  { name: "Midrange preto e verde", total: 60, have: 44 },
  { name: "Voadores azul e preto", total: 60, have: 38 },
  { name: "Controle azul e branco", total: 60, have: 31 },
]);

const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

export function formatBRL(cents: number): string {
  return brl.format(cents / 100);
}
