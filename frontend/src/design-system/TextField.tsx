import { useId, type ReactNode } from "react";

import { FormError } from "./FormError";
import "./TextField.css";

export interface TextFieldProps {
  label: string;
  name: string;
  id?: string;
  hint?: string;
  error?: string;
  /** Ids extras para aria-describedby (ex.: mensagem externa ao campo). */
  describedBy?: string;
  placeholder?: string;
  autoComplete?: string;
  /** Valor inicial (campo não controlado). */
  defaultValue?: string;
  type?: "text" | "email" | "password";
  /** Somente leitura (ex.: durante o envio). */
  readOnly?: boolean;
  /** Control rendered inside the field, next to the input (e.g. show/hide password). */
  action?: ReactNode;
}

export function TextField({
  label,
  name,
  id,
  hint,
  error,
  describedBy: extraDescribedBy,
  placeholder,
  autoComplete,
  defaultValue,
  type = "text",
  readOnly,
  action,
}: TextFieldProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const hintId = `${inputId}-hint`;
  const errorId = `${inputId}-error`;
  const describedBy = [hint ? hintId : null, error ? errorId : null, extraDescribedBy]
    .filter(Boolean)
    .join(" ");

  return (
    <div className="text-field">
      <label className="text-field__label" htmlFor={inputId}>
        {label}
      </label>
      {hint ? (
        <p className="text-field__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      <div className="text-field__control">
        <input
          className="text-field__input"
          id={inputId}
          name={name}
          type={type}
          placeholder={placeholder}
          autoComplete={autoComplete}
          defaultValue={defaultValue}
          readOnly={readOnly}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy || undefined}
        />
        {action}
      </div>
      {error ? <FormError id={errorId}>{error}</FormError> : null}
    </div>
  );
}
