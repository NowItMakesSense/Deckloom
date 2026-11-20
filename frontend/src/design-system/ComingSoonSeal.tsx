import "./ComingSoonSeal.css";

/**
 * Selo "Em breve" (docs/design-system.md 1.9): marca o que ainda nao existe. E metal (dourado), nao e
 * interativo e o estado nao depende so de cor: o texto esta sempre presente.
 */
export function ComingSoonSeal() {
  return <span className="coming-soon-seal">Em breve</span>;
}
