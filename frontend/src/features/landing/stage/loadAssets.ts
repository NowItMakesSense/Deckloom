// Carrega o que o palco precisa antes de montar a cena: as fontes do projeto (para as faces das cartas e as
// fichas de mana), os cinco logos oficiais locais (para os medalhoes) e os modelos glTF (REQ-L14), sob demanda.
// Tudo de arquivos locais.
import { GAMES } from "../games";
import { type StageModels, loadStageModels } from "./engine/deckBoxModel";
import type { GameId } from "./tokens";

export type GameLogos = Record<GameId, HTMLImageElement>;

export interface StageAssets {
  readonly logos: GameLogos;
  readonly models: StageModels;
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error(`logo nao carregou: ${src}`));
    img.src = src;
  });
}

async function loadFonts(): Promise<void> {
  const fonts = globalThis.document?.fonts;
  if (!fonts) return;
  await Promise.all([
    fonts.load('700 21px "Cinzel Decorative"'),
    fonts.load('500 22px "Albert Sans"'),
    fonts.load('600 22px "Albert Sans"'),
    fonts.load("150px Mana", ""),
  ]);
}

async function loadLogos(): Promise<GameLogos> {
  const images = await Promise.all(GAMES.map((game) => loadImage(game.src)));
  return Object.fromEntries(GAMES.map((game, i) => [game.id, images[i]])) as GameLogos;
}

export async function loadStageAssets(): Promise<StageAssets> {
  const [, logos, models] = await Promise.all([loadFonts(), loadLogos(), loadStageModels()]);
  return { logos, models };
}
