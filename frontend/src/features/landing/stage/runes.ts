// A escrita das runas do Deckloom: a mesma nas laterais da deck box, no arco do portal, no caminho e no altar.
// Cada runa e uma lista de tracos (x1, y1, x2, y2) em [0, 1]; deterministica.
import { rng } from "./math";

export type RuneStroke = readonly [number, number, number, number];
export type Rune = readonly RuneStroke[];

export const RUNE_COUNT = 40;

export const RUNES: readonly Rune[] = (() => {
  const r = rng(9);
  const list: Rune[] = [];
  for (let i = 0; i < RUNE_COUNT; i++) {
    const strokes: RuneStroke[] = [];
    const n = 3 + Math.floor(r() * 2);
    for (let k = 0; k < n; k++) strokes.push([r(), r(), r(), r()]);
    list.push(strokes);
  }
  return list;
})();
