import { graphemeLength } from "./graphemeLength";

// Conveniencia de UI: o servidor e a autoridade e revalida todas estas regras.
export const LIMITS = {
  emailMax: 254,
  passwordMin: 8,
  passwordMax: 128,
  displayNameMin: 3,
  displayNameMax: 30,
  bioMax: 280,
} as const;

export type ValidationCode =
  "required" | "invalid_format" | "too_short" | "too_long" | "length" | "invalid_characters";

export type ValidationResult = ValidationCode | null;

type Input = string | null | undefined;

// eslint-disable-next-line no-control-regex
const ANY_CONTROL = /[\u0000-\u001F\u007F-\u009F]/;
// eslint-disable-next-line no-control-regex
const CONTROL_EXCEPT_LF = /[\u0000-\u0009\u000B-\u001F\u007F-\u009F]/;

function normalizeAndTrim(text: string): string {
  return text.normalize("NFC").trim();
}

export function validateEmail(input: Input): ValidationResult {
  const value = (input ?? "").trim().toLowerCase();
  if (value.length === 0) return "required";
  if (value.length > LIMITS.emailMax) return "invalid_format";

  const parts = value.split("@");
  if (parts.length !== 2 || parts[0].length === 0 || /\s/.test(value)) return "invalid_format";

  const labels = parts[1].split(".");
  return labels.length >= 2 && labels.every((label) => label.length > 0) ? null : "invalid_format";
}

export function validatePassword(input: Input): ValidationResult {
  if (input == null || input.length === 0) return "required";
  const length = Array.from(input).length;
  if (length < LIMITS.passwordMin) return "too_short";
  return length > LIMITS.passwordMax ? "too_long" : null;
}

export function validateDisplayName(input: Input): ValidationResult {
  if (input == null) return "length";
  if (ANY_CONTROL.test(input)) return "invalid_characters";
  const length = graphemeLength(normalizeAndTrim(input));
  return length < LIMITS.displayNameMin || length > LIMITS.displayNameMax ? "length" : null;
}

export function validateBio(input: Input): ValidationResult {
  if (input == null) return null;
  const text = input.replace(/\r\n/g, "\n");
  if (CONTROL_EXCEPT_LF.test(text)) return "invalid_characters";
  return graphemeLength(normalizeAndTrim(text)) > LIMITS.bioMax ? "too_long" : null;
}
