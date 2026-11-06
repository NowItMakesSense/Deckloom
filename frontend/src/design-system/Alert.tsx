import { useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";

import "./Alert.css";

export type AlertVariant = "info" | "success" | "error";

export interface AlertProps {
  variant: AlertVariant;
  children: ReactNode;
  /** A regiao com role nasce vazia e o texto entra num efeito apos a montagem (anuncio por leitor de tela, A11Y-31). */
  announceAfterMount?: boolean;
  /** Repassado a regiao com role (alvo de aria-describedby). */
  id?: string;
  /**
   * So com announceAfterMount: chamada uma vez, depois de o texto estar no DOM (A11Y-31).
   * Quem precisa de uma regiao ja preenchida (ex.: focar um campo descrito por ela) espera por esta chamada.
   */
  onAnnounced?: () => void;
}

const ICONS: Record<AlertVariant, string> = {
  info: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z",
  success: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm-2 15l-5-5 1.4-1.4L10 14.2l7.6-7.6L19 8l-9 9z",
  error: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z",
};

export function Alert({
  variant,
  children,
  announceAfterMount = false,
  id,
  onAnnounced,
}: AlertProps) {
  const [mounted, setMounted] = useState(!announceAfterMount);
  const onAnnouncedRef = useRef(onAnnounced);
  useEffect(() => {
    onAnnouncedRef.current = onAnnounced;
  });

  // A regiao existe vazia e o texto entra depois da pintura: um frame apos o commit (U-06, A11Y-31).
  useEffect(() => {
    if (!announceAfterMount) return;
    const frame = requestAnimationFrame(() => setMounted(true));
    return () => cancelAnimationFrame(frame);
  }, [announceAfterMount]);

  useEffect(() => {
    if (announceAfterMount && mounted) onAnnouncedRef.current?.();
  }, [announceAfterMount, mounted]);

  return (
    <div
      className={`alert alert--${variant}`}
      role={variant === "error" ? "alert" : "status"}
      id={id}
    >
      <svg
        className="alert__icon"
        width="20"
        height="20"
        viewBox="0 0 24 24"
        aria-hidden="true"
        focusable="false"
      >
        <path fill="currentColor" d={ICONS[variant]} />
      </svg>
      <div className="alert__text">{!announceAfterMount || mounted ? children : null}</div>
    </div>
  );
}
