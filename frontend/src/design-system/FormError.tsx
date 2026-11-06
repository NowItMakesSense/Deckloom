import type { ReactNode } from "react";

import "./FormError.css";

export interface FormErrorProps {
  id?: string;
  children: ReactNode;
}

export function FormError({ id, children }: FormErrorProps) {
  return (
    <div className="form-error" id={id} role="alert">
      <svg
        className="form-error__icon"
        width="16"
        height="16"
        viewBox="0 0 24 24"
        aria-hidden="true"
        focusable="false"
      >
        <path
          fill="currentColor"
          d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"
        />
      </svg>
      <span>{children}</span>
    </div>
  );
}
