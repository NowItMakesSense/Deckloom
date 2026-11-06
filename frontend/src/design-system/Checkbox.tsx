import { useId, type ChangeEventHandler, type ReactNode } from "react";

import "./Checkbox.css";

export interface CheckboxProps {
  label: ReactNode;
  name: string;
  id?: string;
  value?: string;
  defaultChecked?: boolean;
  disabled?: boolean;
  /** Id da mensagem que descreve a caixa (aria-describedby). */
  describedBy?: string;
  onChange?: ChangeEventHandler<HTMLInputElement>;
}

export function Checkbox({
  label,
  name,
  id,
  value,
  defaultChecked,
  disabled,
  describedBy,
  onChange,
}: CheckboxProps) {
  const generatedId = useId();
  const inputId = id ?? generatedId;

  return (
    <div className="checkbox">
      <input
        className="checkbox__input"
        type="checkbox"
        id={inputId}
        name={name}
        value={value}
        defaultChecked={defaultChecked}
        disabled={disabled}
        aria-describedby={describedBy}
        onChange={onChange}
      />
      <label className="checkbox__label" htmlFor={inputId}>
        {label}
      </label>
    </div>
  );
}
