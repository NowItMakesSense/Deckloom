import { useId, useState } from "react";

import { graphemeLength } from "../shared/validation/graphemeLength";
import { FormError } from "./FormError";
import "./Textarea.css";

export interface TextareaProps {
  label: string;
  name: string;
  id?: string;
  defaultValue?: string;
  hint?: string;
  error?: string;
  /** Limite em elementos de texto (grapheme clusters). Padrao 280. */
  limit?: number;
}

const DEFAULT_LIMIT = 280;
const numberFormat = new Intl.NumberFormat("pt-BR");

export function Textarea({
  label,
  name,
  id,
  defaultValue = "",
  hint,
  error,
  limit = DEFAULT_LIMIT,
}: TextareaProps) {
  const generatedId = useId();
  const areaId = id ?? generatedId;
  const hintId = `${areaId}-hint`;
  const errorId = `${areaId}-error`;
  const counterId = `${areaId}-counter`;
  const [count, setCount] = useState(() => graphemeLength(defaultValue));

  const overLimit = count > limit;
  const counterText = `${numberFormat.format(count)} de ${numberFormat.format(limit)}`;
  const describedBy = [hint ? hintId : null, error ? errorId : null, counterId]
    .filter(Boolean)
    .join(" ");

  return (
    <div className="textarea">
      <label className="textarea__label" htmlFor={areaId}>
        {label}
      </label>
      {hint ? (
        <p className="textarea__hint" id={hintId}>
          {hint}
        </p>
      ) : null}
      <textarea
        className="textarea__control"
        id={areaId}
        name={name}
        defaultValue={defaultValue}
        aria-invalid={error || overLimit ? true : undefined}
        aria-describedby={describedBy}
        onChange={(event) => setCount(graphemeLength(event.target.value))}
      />
      {error ? <FormError id={errorId}>{error}</FormError> : null}
      {overLimit ? (
        <FormError id={counterId}>{counterText}</FormError>
      ) : (
        <p className="textarea__counter" id={counterId}>
          {counterText}
        </p>
      )}
    </div>
  );
}
