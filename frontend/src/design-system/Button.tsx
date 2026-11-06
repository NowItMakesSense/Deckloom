import type { ReactNode } from "react";

import "./Button.css";

export interface ButtonProps {
  children: ReactNode;
  onClick?: () => void;
  loading?: boolean;
  disabled?: boolean;
  type?: "button" | "submit";
  /** Largura mínima estável (11rem, limitada à largura disponível): o botão não muda de largura ao entrar em loading (WC-12). */
  stableWidth?: boolean;
  /** Sem variante, o botao e o preenchido padrao. */
  variant?: "primary" | "secondary" | "danger";
}

export function Button({
  children,
  onClick,
  loading = false,
  disabled = false,
  type = "button",
  stableWidth = false,
  variant,
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled || undefined}
      className="button"
      data-variant={variant}
      data-stable={stableWidth ? "true" : undefined}
      aria-busy={loading ? "true" : undefined}
      onClick={loading ? undefined : onClick}
    >
      {loading ? <span className="button__spinner" aria-hidden="true" /> : null}
      {children}
    </button>
  );
}
