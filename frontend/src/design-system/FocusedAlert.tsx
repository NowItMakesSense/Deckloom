import { useEffect, useRef } from "react";
import type { ReactNode } from "react";

import { Alert } from "./Alert";
import type { AlertVariant } from "./Alert";

export interface FocusedAlertProps {
  variant: AlertVariant;
  children: ReactNode;
  /** false = so renderiza o alerta, sem mover o foco (carga de pagina, A11Y-23). Padrao true. */
  focus?: boolean;
  /** Repassado ao Alert: regiao vazia na montagem, texto no efeito. */
  announceAfterMount?: boolean;
  /** Repassado a regiao com role. */
  id?: string;
  /** Repassado ao Alert (T-167). */
  onAnnounced?: () => void;
}

// Alert que recebe o foco ao aparecer (o próprio elemento com role fica focado).
export function FocusedAlert({
  variant,
  children,
  focus = true,
  announceAfterMount,
  id,
  onAnnounced,
}: FocusedAlertProps) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!focus) return;
    const target = ref.current?.firstElementChild;
    if (target instanceof HTMLElement) {
      target.tabIndex = -1;
      target.focus();
    }
  }, [focus]);
  return (
    <div ref={ref}>
      <Alert
        variant={variant}
        announceAfterMount={announceAfterMount}
        id={id}
        onAnnounced={onAnnounced}
      >
        {children}
      </Alert>
    </div>
  );
}
