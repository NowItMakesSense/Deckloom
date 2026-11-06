import { useState } from "react";

import { TextField } from "./TextField";
import "./PasswordField.css";

export interface PasswordFieldProps {
  label: string;
  name: string;
  id?: string;
  hint?: string;
  error?: string;
  /** Ids extras para aria-describedby (ex.: mensagem externa ao campo). */
  describedBy?: string;
  /** Nome acessivel fixo "Mostrar senha"; o estado fica so em aria-pressed (S11). */
  stableLabel?: boolean;
  readOnly?: boolean;
  placeholder?: string;
  autoComplete: "current-password" | "new-password";
}

export function PasswordField({ stableLabel = false, ...props }: PasswordFieldProps) {
  const [visible, setVisible] = useState(false);

  return (
    <TextField
      {...props}
      type={visible ? "text" : "password"}
      action={
        <button
          type="button"
          className="password-field__toggle"
          aria-pressed={visible}
          onClick={() => setVisible((current) => !current)}
        >
          {visible && !stableLabel ? "Ocultar senha" : "Mostrar senha"}
        </button>
      }
    />
  );
}
