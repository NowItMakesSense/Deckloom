const segmenter = new Intl.Segmenter(undefined, { granularity: "grapheme" });

export function graphemeLength(text: string): number {
  return Array.from(segmenter.segment(text)).length;
}
