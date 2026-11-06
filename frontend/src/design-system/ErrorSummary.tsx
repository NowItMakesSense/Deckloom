import { useEffect, useRef, type MouseEvent } from "react";

import "./ErrorSummary.css";

export interface ErrorSummaryItem {
  id: string;
  message: string;
}

export interface ErrorSummaryProps {
  errors: ErrorSummaryItem[];
}

export function ErrorSummary({ errors }: ErrorSummaryProps) {
  const ref = useRef<HTMLDivElement>(null);
  const hasErrors = errors.length > 0;

  useEffect(() => {
    if (hasErrors) ref.current?.focus();
  }, [hasErrors]);

  if (!hasErrors) return null;

  function focusField(event: MouseEvent<HTMLAnchorElement>, id: string) {
    const field = document.getElementById(id);
    if (!field) return;
    event.preventDefault();
    field.focus();
  }

  return (
    <div className="error-summary" role="alert" tabIndex={-1} ref={ref}>
      <ul className="error-summary__list">
        {errors.map((error) => (
          <li key={error.id}>
            <a href={`#${error.id}`} onClick={(event) => focusField(event, error.id)}>
              {error.message}
            </a>
          </li>
        ))}
      </ul>
    </div>
  );
}
