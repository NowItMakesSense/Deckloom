import {
  useRef,
  type ComponentType,
  type ForwardRefExoticComponent,
  type HTMLAttributes,
  type MouseEvent,
  type RefAttributes,
} from "react";

import { prefersReducedMotion } from "../motion";

interface Handle {
  startAnimation: () => void;
  stopAnimation: () => void;
}

type RawIcon = ForwardRefExoticComponent<
  HTMLAttributes<HTMLDivElement> & { size?: number } & RefAttributes<Handle>
>;

export interface IconProps extends HTMLAttributes<HTMLDivElement> {
  size?: number;
  /** Nome acessivel. Sem ele, o icone e decorativo (aria-hidden). */
  label?: string;
}

/**
 * Embrulha um icone copiado do lucide-animated: decorativo por padrao, nome acessivel com `label`
 * e animacao so com movimento permitido. Ao receber o ref, o icone original deixa de animar sozinho
 * (modo controlado), entao a decisao de animar passa a ser nossa.
 */
export function withAccessibility(Raw: RawIcon, displayName: string): ComponentType<IconProps> {
  function Icon({ label, onMouseEnter, onMouseLeave, ...props }: IconProps) {
    const handle = useRef<Handle>(null);
    const a11y = label ? { role: "img", "aria-label": label } : { "aria-hidden": true as const };
    return (
      <Raw
        ref={handle}
        {...props}
        {...a11y}
        onMouseEnter={(e: MouseEvent<HTMLDivElement>) => {
          onMouseEnter?.(e);
          if (!prefersReducedMotion()) handle.current?.startAnimation();
        }}
        onMouseLeave={(e: MouseEvent<HTMLDivElement>) => {
          onMouseLeave?.(e);
          if (!prefersReducedMotion()) handle.current?.stopAnimation();
        }}
      />
    );
  }
  Icon.displayName = displayName;
  return Icon;
}
