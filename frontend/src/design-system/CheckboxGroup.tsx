import { useId } from "react";

import { Checkbox } from "./Checkbox";
import { FormError } from "./FormError";
import "./CheckboxGroup.css";

export interface ChoiceOption {
  value: string;
  label: string;
}

export interface CheckboxGroupProps {
  legend: string;
  name: string;
  options: ChoiceOption[];
  defaultValue?: string[];
  /** Texto mostrado quando nao ha opcoes (A11Y-27). */
  emptyText?: string;
  error?: string;
}

export function CheckboxGroup({
  legend,
  name,
  options,
  defaultValue = [],
  emptyText,
  error,
}: CheckboxGroupProps) {
  const groupId = useId();
  const errorId = `${groupId}-error`;

  return (
    <fieldset className="choice-group" aria-describedby={error ? errorId : undefined}>
      <legend className="choice-group__legend">{legend}</legend>
      {options.length === 0 ? (
        <p className="choice-group__empty">{emptyText}</p>
      ) : (
        options.map((option) => (
          <Checkbox
            key={option.value}
            name={name}
            value={option.value}
            label={option.label}
            defaultChecked={defaultValue.includes(option.value)}
          />
        ))
      )}
      {error ? <FormError id={errorId}>{error}</FormError> : null}
    </fieldset>
  );
}
