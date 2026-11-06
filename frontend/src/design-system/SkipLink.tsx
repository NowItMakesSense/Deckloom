import type { MouseEvent } from "react";

import "./SkipLink.css";

export interface SkipLinkProps {
  /** id do <main> alvo; padrao "conteudo". */
  targetId?: string;
}

export function SkipLink({ targetId = "conteudo" }: SkipLinkProps) {
  function handleClick(event: MouseEvent<HTMLAnchorElement>) {
    const target = document.getElementById(targetId);
    if (!target) return;
    event.preventDefault();
    target.focus();
  }

  return (
    <a className="skip-link" href={`#${targetId}`} onClick={handleClick}>
      Pular para o conteúdo
    </a>
  );
}
