import "./Spinner.css";

export interface SpinnerProps {
  label: string;
}

export function Spinner({ label }: SpinnerProps) {
  return (
    <span className="spinner">
      <span className="spinner__ring" aria-hidden="true" />
      <span>{label}</span>
    </span>
  );
}
