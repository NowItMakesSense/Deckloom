import { useId } from "react";

import type { ChoiceOption } from "./CheckboxGroup";
import { FormError } from "./FormError";
import "./CheckboxGroup.css";
import "./RadioGroup.css";

export interface RadioGroupProps {
  legend: string;
  name: string;
  options: ChoiceOption[];
  defaultValue?: string;
  emptyText?: string;
  error?: string;
}

export function RadioGroup({
  legend,
  name,
  options,
  defaultValue,
  emptyText,
  error,
}: RadioGroupProps) {
  const groupId = useId();
  const errorId = `${groupId}-error`;

  return (
    <fieldset className="choice-group" aria-describedby={error ? errorId : undefined}>
      <legend className="choice-group__legend">{legend}</legend>
      {options.length === 0 ? (
        <p className="choice-group__empty">{emptyText}</p>
      ) : (
        <div role="radiogroup" aria-label={legend} className="choice-group__radios">
          {options.map((option, index) => {
            const inputId = `${groupId}-${index}`;
            return (
              <div className="radio" key={option.value}>
                <input
                  className="radio__input"
                  type="radio"
                  id={inputId}
                  name={name}
                  value={option.value}
                  defaultChecked={option.value === defaultValue}
                />
                <label className="radio__label" htmlFor={inputId}>
                  {option.label}
                </label>
              </div>
            );
          })}
        </div>
      )}
      {error ? <FormError id={errorId}>{error}</FormError> : null}
    </fieldset>
  );
}
