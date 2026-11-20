// Os cinco jogos do MVP, juntos e na ordem do REQ-L01 (nenhum vem antes dos outros). Logos oficiais locais em
// public/brand/games (origem e licenca em SOURCES.md; revisao juridica obrigatoria antes de publicar, RISK-6).

export interface GameLogo {
  readonly id: string;
  /** Nome do jogo: texto alternativo do logo. */
  readonly name: string;
  readonly src: string;
  /** Tamanho natural do arquivo, para o navegador reservar o espaco. */
  readonly width: number;
  readonly height: number;
  /** Logos de contorno escuro precisam de uma placa clara nos dois temas, sem alterar as cores oficiais. */
  readonly plate: "clear" | "none";
}

export const GAMES: readonly GameLogo[] = [
  {
    id: "magic",
    name: "Magic: The Gathering",
    src: "/brand/games/magic-the-gathering.svg",
    width: 202,
    height: 73,
    plate: "clear",
  },
  {
    id: "pokemon",
    name: "Pokémon TCG",
    src: "/brand/games/pokemon-tcg.svg",
    width: 312,
    height: 160,
    plate: "clear",
  },
  {
    id: "yugioh",
    name: "Yu-Gi-Oh!",
    src: "/brand/games/yu-gi-oh.png",
    width: 1274,
    height: 446,
    plate: "clear",
  },
  {
    id: "onepiece",
    name: "One Piece Card Game",
    src: "/brand/games/one-piece-card-game.webp",
    width: 504,
    height: 130,
    plate: "clear",
  },
  {
    id: "lorcana",
    name: "Disney Lorcana",
    src: "/brand/games/disney-lorcana.png",
    width: 474,
    height: 210,
    plate: "clear",
  },
];

/** Nota de marcas do rodape (REQ-L03). Texto provisorio, sujeito a revisao juridica antes de publicar. */
export const TRADEMARK_NOTE =
  "Magic: The Gathering, Pokémon TCG, Yu-Gi-Oh!, One Piece Card Game e Disney Lorcana são marcas de seus titulares. O Deckloom não é afiliado nem endossado por eles.";
