import { useEffect, useRef } from "react";
import { Link } from "react-router-dom";

import { ComingSoonSeal } from "@/design-system/ComingSoonSeal";
import { UserPlusIcon } from "@/design-system/icons";

import { animatePlans, tiltOnPointer } from "./plansMotion";

import "./PlansSection.css";

const PAID_FEATURES = [
  "Sugestões com IA",
  "Decks ilimitados",
  "Alertas de preço",
  "Sem anúncios",
] as const;

/**
 * Planos de assinante (REQ-L13): Gratis (ate 5 decks) e Arcano, o pago, a R$ 19,90 por mes. Todo botao leva a
 * /cadastro: o jogador ainda nao paga nada e a landing nao coleta dado de pagamento. Entrada com anime.js.
 */
export function PlansSection() {
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    const section = ref.current;
    if (!section) return;
    const stopMotion = animatePlans(section);
    const stopTilt = tiltOnPointer(section);
    return () => {
      stopTilt();
      stopMotion();
    };
  }, []);

  return (
    <section ref={ref} id="planos" className="plans" aria-labelledby="planos-titulo">
      <header className="plans__header">
        <h2 id="planos-titulo" className="plans__title">
          Comece grátis
        </h2>
        <p>
          Monte decks com a sua coleção sem pagar nada. O plano Arcano libera a IA e os alertas de
          preço.
        </p>
      </header>
      <div className="plans__row">
        <div className="plans__shell">
          <article className="plan" aria-labelledby="plano-gratis">
            <h3 id="plano-gratis" className="plan__name">
              Grátis
            </h3>
            <p className="plan__price">
              <b>R$ 0</b>
            </p>
            <ul className="plan__list">
              <li>Até 5 decks salvos</li>
            </ul>
            <Link className="plan__cta" to="/cadastro">
              <UserPlusIcon size={18} />
              Começar grátis
            </Link>
          </article>
        </div>
        <div className="plans__shell">
          <article className="plan plan--paid" aria-labelledby="plano-arcano">
            <svg className="plan__frame" aria-hidden="true" focusable="false">
              <rect className="plan__trace" pathLength="1" />
            </svg>
            <h3 id="plano-arcano" className="plan__name">
              Arcano <span className="plan__badge">Pago</span>
            </h3>
            <p className="plan__price">
              <b data-price aria-hidden="true">
                R$ 19,90
              </b>
              <span className="landing-sr-only">R$ 19,90</span>
              <span>por mês</span>
            </p>
            <ul className="plan__list">
              {PAID_FEATURES.map((feature) => (
                <li key={feature}>{feature}</li>
              ))}
            </ul>
            <p className="plan__soon">
              <ComingSoonSeal />
            </p>
            <Link className="plan__cta plan__cta--primary" to="/cadastro">
              <UserPlusIcon size={18} />
              Quero o Arcano
            </Link>
          </article>
        </div>
      </div>
      <p className="plans__fine">
        A assinatura paga ainda não é cobrada: os dois botões levam ao cadastro e a landing não
        coleta dado de pagamento.
      </p>
    </section>
  );
}
