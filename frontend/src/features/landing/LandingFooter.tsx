import { TRADEMARK_NOTE } from "./games";

import "./LandingFooter.css";

const FOOTER_LINKS = [
  { href: "#como-funciona", label: "Como funciona" },
  { href: "#planos", label: "Planos" },
] as const;

/** Rodape: faixa invertida (--color-night) com a logo para fundo escuro, os links da pagina e a nota de marcas. */
export function LandingFooter() {
  return (
    <footer className="landing-footer">
      <div className="landing-footer__inner">
        <img
          className="landing-footer__logo"
          src="/brand/deckloom/deckloom-logo-on-dark.svg"
          alt="Deckloom"
          width="176"
          height="48"
        />
        <nav className="landing-footer__nav" aria-label="Rodapé">
          <ul className="landing-footer__links">
            {FOOTER_LINKS.map((link) => (
              <li key={link.href}>
                <a className="landing-footer__link" href={link.href}>
                  {link.label}
                </a>
              </li>
            ))}
          </ul>
        </nav>
        <p className="landing-footer__note">{TRADEMARK_NOTE}</p>
      </div>
    </footer>
  );
}
