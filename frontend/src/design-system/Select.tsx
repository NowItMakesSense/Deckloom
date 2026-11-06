import { useId } from "react";

import type { ChoiceOption } from "./CheckboxGroup";
import { FormError } from "./FormError";
import "./Select.css";

export interface SelectProps {
  label: string;
  name: string;
  options: ChoiceOption[];
  id?: string;
  defaultValue?: string;
  hint?: string;
  error?: string;
}

export function Select({ label, name, options, id, defaultValue, hint, error }: SelectProps) {
  const generatedId = useId();
  const selectId = id ?? generatedId;
  const hintId = `${selectId}-hint`;
  const errorId = `${selectId}-error`;
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(" ");

  return (
    <div className="select">
      <label className="select__label" htmlFor={selectId}>
        {label}
      </label>
      {hint ? (
        <p className="select__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      <select
        className="select__control"
        id={selectId}
        name={name}
        defaultValue={defaultValue}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy || undefined}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      {error ? <FormError id={errorId}>{error}</FormError> : null}
    </div>
  );
}
