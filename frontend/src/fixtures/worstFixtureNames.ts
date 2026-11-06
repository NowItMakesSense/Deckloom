export const WORST_FIXTURE_NAMES = [
  "vazio",
  "um",
  "enorme",
  "longo",
  "curto",
  "faltando",
  "estranho",
  "lento",
  "erro",
] as const;

export type WorstFixtureName = (typeof WORST_FIXTURE_NAMES)[number];
