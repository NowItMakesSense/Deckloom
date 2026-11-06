import type { ReactNode } from "react";
import { Link as RouterLink } from "react-router-dom";

import "./Link.css";

export interface LinkProps {
  href: string;
  external?: boolean;
  /** Link sozinho no paragrafo (nao inline em texto): area de toque minima de 44px (ART-08). */
  standalone?: boolean;
  children: ReactNode;
}

export function Link({ href, external = false, standalone = false, children }: LinkProps) {
  const className = standalone ? "link link--standalone" : "link";
  if (!external) {
    return (
      <RouterLink className={className} to={href}>
        {children}
      </RouterLink>
    );
  }
  return (
    <a className={className} href={href} target="_blank" rel="noopener noreferrer">
      {children} <span className="link__hint">(abre em nova aba)</span>
    </a>
  );
}
