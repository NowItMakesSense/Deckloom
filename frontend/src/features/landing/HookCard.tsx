import type { ReactNode } from "react";

import { ComingSoonSeal } from "@/design-system/ComingSoonSeal";

import type { HookData, HookId } from "./hooks";

import "./HookCard.css";

export interface HookCardProps {
  hook: HookData;
  /** Exemplo do gancho, dentro da caixa de texto. */
  children?: ReactNode;
}

const SYMBOL_SRC = "/brand/deckloom/deckloom-symbol.svg";

/** Ornamento da janela de arte: anel de runas e um acento por gancho. Decorativo (aria-hidden). */
function ArtOrnament({ variant }: { variant: HookId }) {
  return (
    <svg className="hook-card__ornament" viewBox="0 0 200 140" aria-hidden="true" focusable="false">
      <circle cx="100" cy="70" r="56" className="hook-card__ring" />
      <circle cx="100" cy="70" r="46" className="hook-card__ring hook-card__ring--inner" />
      {[0, 45, 90, 135, 180, 225, 270, 315].map((angle) => (
        <path
          key={angle}
          d="M100 8v10M97 13h6"
          className="hook-card__rune"
          transform={`rotate(${angle} 100 70)`}
        />
      ))}
      {variant === "quanto-do-deck" ? (
        <path
          d="M24 118l16-22M176 118l-16-22M40 124l12-16M160 124l-12-16"
          className="hook-card__accent"
        />
      ) : null}
      {variant === "meta" ? (
        <path d="M18 70a82 30 0 0 1 164 0M18 70a82 30 0 0 0 164 0" className="hook-card__accent" />
      ) : null}
      {variant === "lojas" ? (
        <g className="hook-card__accent">
          <ellipse cx="30" cy="116" rx="14" ry="5" />
          <ellipse cx="30" cy="109" rx="14" ry="5" />
          <ellipse cx="170" cy="116" rx="14" ry="5" />
        </g>
      ) : null}
      {variant === "ia" ? (
        <path
          d="M30 24l4 10 10 4-10 4-4 10-4-10-10-4 10-4ZM172 100l3 7 7 3-7 3-3 7-3-7-7-3 7-3Z"
          className="hook-card__accent"
        />
      ) : null}
    </svg>
  );
}

/**
 * Gancho da landing como carta do Deckloom (REQ-L11): faixa de titulo, janela de arte com o simbolo do Deckloom,
 * linha de tipo e caixa de texto. Moldura propria, inspirada na anatomia de uma carta de Magic, sem copiar a
 * moldura da Wizards. Tudo e HTML real: legivel sem o palco WebGL. As quatro partes levam `data-reveal-item`:
 * no palco a camera as revela uma a uma (stage/overlays.ts); no fallback entram por secao (useRevealOnEnter).
 */
export function HookCard({ hook, children }: HookCardProps) {
  const titleId = `${hook.id}-titulo`;
  return (
    <article className="hook-card" aria-labelledby={titleId} data-hook={hook.id}>
      <header className="hook-card__title" data-part="title" data-reveal-item>
        <h2 id={titleId} className="hook-card__heading">
          {hook.title}
        </h2>
        <ComingSoonSeal />
      </header>
      <div className="hook-card__art" data-part="art" data-reveal-item>
        <ArtOrnament variant={hook.id} />
        <img className="hook-card__symbol" src={SYMBOL_SRC} alt="" width="56" height="56" />
      </div>
      <p className="hook-card__type" data-part="type" data-reveal-item>
        <span>Recurso</span>
        <span aria-hidden="true"> — </span>
        {hook.features.map((feature, index) => (
          <span key={feature}>
            {index > 0 ? ", " : null}
            <span>{feature}</span>
          </span>
        ))}
      </p>
      <div className="hook-card__text" data-part="text" data-reveal-item>
        <p className="hook-card__rules">{hook.text}</p>
        {children}
      </div>
    </article>
  );
}
