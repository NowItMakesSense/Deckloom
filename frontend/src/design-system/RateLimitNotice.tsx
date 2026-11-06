import { useEffect, useState } from "react";

import "./RateLimitNotice.css";

export const RATE_LIMIT_NOTICE_DELAY_MS = 2_000;

export const RATE_LIMIT_NOTICE_TEXT =
  "Por segurança, esta resposta está demorando um pouco mais. Aguarde, não é necessário enviar de novo.";

export interface RateLimitNoticeProps {
  /** Envio em andamento; o aviso só aparece depois de RATE_LIMIT_NOTICE_DELAY_MS. */
  sending: boolean;
}

export function RateLimitNotice({ sending }: RateLimitNoticeProps) {
  const [waited, setWaited] = useState(false);

  useEffect(() => {
    if (!sending) {
      return undefined;
    }
    const timer = setTimeout(() => setWaited(true), RATE_LIMIT_NOTICE_DELAY_MS);
    return () => {
      clearTimeout(timer);
      setWaited(false);
    };
  }, [sending]);

  return (
    <div role="status" className="rate-limit-notice">
      {sending && waited ? (
        <span className="rate-limit-notice__text">{RATE_LIMIT_NOTICE_TEXT}</span>
      ) : null}
    </div>
  );
}
