// Os quatro ganchos da landing, na ordem do REQ-L01 v6. Texto derivado do requisito; exemplos com numeros do
// mock (deck-mock.ts). Todos os recursos citados ainda nao existem: levam o selo "Em breve" (REQ-L02).
export type HookId = "quanto-do-deck" | "meta" | "lojas" | "ia";

export interface HookData {
  readonly id: HookId;
  /** Faixa de titulo da carta. */
  readonly title: string;
  /** Recursos do produto que o gancho apresenta (linha de tipo); ainda indisponiveis. */
  readonly features: readonly string[];
  /** Caixa de texto da carta. */
  readonly text: string;
}

export const HOOKS: readonly HookData[] = [
  {
    id: "quanto-do-deck",
    title: "Quanto do deck você já tem",
    features: ["Coleção", "Decks"],
    text: "Veja quanto de cada deck você já tem com as cartas da sua coleção e o que ainda falta.",
  },
  {
    id: "meta",
    title: "O meta dos torneios contra a sua coleção",
    features: ["Meta"],
    text: "Compare os decks em alta nos torneios com a sua coleção e veja de qual você está mais perto.",
  },
  {
    id: "lojas",
    title: "Compre só o que falta",
    features: ["Lojas"],
    text: "Compre só o que falta, nas lojas parceiras, comparando preço.",
  },
  {
    id: "ia",
    title: "IA que explica o porquê de cada carta",
    features: ["Sugestões com IA"],
    text: "Peça em linguagem natural e receba, para cada carta, o porquê de ela estar no deck. A IA também avisa quando o preço de uma carta que falta cai.",
  },
];
