import { manaLabel, manaSymbols } from "./mock/deck-mock";

export interface ManaCostProps {
  /** Custo no formato {2}{U}{B}; vazio e terreno. */
  cost: string;
}

/**
 * Custo de mana com os simbolos da fonte Mana (public/brand/mana, carregada no index.html). Os simbolos sao
 * decorativos e o grupo tem nome acessivel em texto. Renderizado com JSX: nada de HTML interpolado.
 */
export function ManaCost({ cost }: ManaCostProps) {
  const symbols = manaSymbols(cost);
  return (
    <span className="cost" data-cost={cost} role="img" aria-label={manaLabel(cost)}>
      {symbols.length > 0 ? (
        symbols.map((symbol, index) => (
          <span
            key={`${symbol}-${index}`}
            className={`ms ms-cost ms-shadow ms-${symbol}`}
            aria-hidden="true"
          />
        ))
      ) : (
        <span className="cost__land" aria-hidden="true">
          terreno
        </span>
      )}
    </span>
  );
}
