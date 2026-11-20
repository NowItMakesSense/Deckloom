// Exemplos dentro das cartas dos ganchos (REQ-L01 v3). Dados do mock (deck-mock.ts), todos inventados e
// marcados como exemplo; as contas fecham: tem + faltam = total, antes e depois de cada "Tenho +1".
import { BellIcon } from "@/design-system/icons";

import { ManaCost } from "./ManaCost";
import {
  META,
  PRICE_ALERT,
  SHOPS,
  cardCost,
  formatBRL,
  shopQuote,
  summarize,
  type Deck,
} from "./mock/deck-mock";

import "./examples.css";

const VISIBLE_CARDS = 4;

function ExampleNote() {
  return <p className="example-note">Exemplo ilustrativo, com dados inventados.</p>;
}

export interface DeckExampleProps {
  deck: Deck;
  /** O jogador diz que tem mais uma copia da carta. */
  onHave: (cardName: string) => void;
}

/** Gancho 1: quanto do deck voce ja tem. */
export function DeckExample({ deck, onHave }: DeckExampleProps) {
  const summary = summarize(deck);
  const complete = summary.missing === 0;
  return (
    <div className="deck-example" data-ui>
      <p className="deck-example__name">
        {deck.name} <span className="deck-example__game">{deck.game}</span>
      </p>
      <p
        className="deck-example__total tabular-nums"
        role="status"
        data-have={summary.have}
        data-missing={summary.missing}
        data-total={summary.total}
      >
        {complete ? (
          <>
            Deck completo: <strong>{summary.have}</strong> de <strong>{summary.total}</strong>{" "}
            cartas
          </>
        ) : (
          <>
            Você já tem <strong>{summary.have}</strong> de <strong>{summary.total}</strong> cartas ·
            faltam <strong>{summary.missing}</strong>
          </>
        )}
      </p>
      {complete ? null : (
        <ul className="deck-example__list" aria-label="Cartas que faltam">
          {summary.incomplete.slice(0, VISIBLE_CARDS).map((card) => (
            <li key={card.name} className="deck-example__row">
              <span className="deck-example__card">
                <span className="deck-example__card-name">{card.name}</span>
                <ManaCost cost={cardCost(card.name)} />
              </span>
              <span className="deck-example__have-count tabular-nums">
                {card.have} de {card.need}
              </span>
              <button
                type="button"
                className="deck-example__have"
                aria-label={`Tenho +1 de ${card.name}`}
                onClick={() => onHave(card.name)}
              >
                Tenho +1
              </button>
            </li>
          ))}
        </ul>
      )}
      <ExampleNote />
    </div>
  );
}

/** Gancho 2: o meta dos torneios contra a colecao. */
export function MetaExample() {
  return (
    <div className="meta-example" data-ui>
      <ul className="meta-example__list" aria-label="Decks em alta nos torneios">
        {META.map((row) => (
          <li key={row.name} className="meta-example__row">
            <span className="meta-example__name">{row.name}</span>
            <span className="meta-example__count tabular-nums">
              você tem {row.have} de {row.total} · faltam {row.total - row.have}
            </span>
            <div
              className="meta-example__bar"
              role="progressbar"
              aria-label={`${row.name}: ${row.have} de ${row.total} cartas`}
              aria-valuemin={0}
              aria-valuemax={row.total}
              aria-valuenow={row.have}
            >
              <span style={{ width: `${(row.have / row.total) * 100}%` }} />
            </div>
          </li>
        ))}
      </ul>
      <ExampleNote />
    </div>
  );
}

export interface ShopsExampleProps {
  deck: Deck;
}

/** Gancho 3: so o que falta, nas lojas parceiras, comparando preco. */
export function ShopsExample({ deck }: ShopsExampleProps) {
  const missing = summarize(deck).missing;
  if (missing === 0) {
    return (
      <div className="shops-example" data-ui>
        <p className="shops-example__empty">Nada a comprar: você já tem o deck inteiro.</p>
        <ExampleNote />
      </div>
    );
  }
  const quotes = SHOPS.map((shop) => ({ shop, total: shopQuote(deck, shop).total }));
  const best = Math.min(...quotes.map((quote) => quote.total));
  const bestId = quotes.find((quote) => quote.total === best)?.shop.id;
  return (
    <div className="shops-example" data-ui>
      <ul className="shops-example__list" aria-label="Lojas parceiras">
        {quotes.map(({ shop, total }) => (
          <li key={shop.id} className="shops-example__row">
            <span className="shops-example__shop">
              <span className="shops-example__name">{shop.name}</span>
              <span className="shops-example__missing">faltam {missing} cartas</span>
            </span>
            <span className="shops-example__price tabular-nums">
              {shop.id === bestId ? <span className="shops-example__best">Menor preço</span> : null}
              <strong>{formatBRL(total)}</strong>
            </span>
          </li>
        ))}
      </ul>
      <ExampleNote />
    </div>
  );
}

/** Gancho 4: IA que explica o porque de cada carta, pedido em linguagem natural e alerta de queda de preco. */
export function AssistantExample() {
  return (
    <div className="assistant-example" data-ui>
      <p className="assistant-example__label">Pedido em linguagem natural</p>
      <blockquote className="assistant-example__ask">
        Um deck de voadores azul e preto com as cartas que eu já tenho.
      </blockquote>
      <div className="assistant-example__alert">
        <BellIcon size={18} />
        <span>
          <strong>Alerta de preço:</strong> {PRICE_ALERT.card} caiu de{" "}
          <span className="tabular-nums">{formatBRL(PRICE_ALERT.from)}</span> para{" "}
          <span className="tabular-nums">{formatBRL(PRICE_ALERT.to)}</span> na {PRICE_ALERT.shop}.
        </span>
      </div>
      <ExampleNote />
    </div>
  );
}
