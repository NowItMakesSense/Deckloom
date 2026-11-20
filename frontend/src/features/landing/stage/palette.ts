// Paleta unica do palco (REQ-L10, AC-L02h): toda cor da floresta, do ceu, da caixa e dos tokens vive aqui.
// O renderer nao usa tonemapping: as silhuetas aparecem exatamente nestas cores (o ACES as lavava).

/** Camadas de silhueta da floresta, da mais distante (claro, azulado) a mais proxima (quase preto). */
export const FOREST = {
  mount: "#7a68b2",
  city: "#5a4c96",
  giants: "#3b3f73",
  ring: "#283153",
  ruin: "#1f2944",
  moss: "#2f4c3e",
  portal: "#141b2b",
  ruins2: "#121a2b",
  corridor: "#0f1726",
  canopyB: "#0d1421",
  canopyA: "#0a101b",
  fern: "#1d3326",
  trunk: "#0a0f18",
  gold: "#f3dca6",
  rune: "#d9b45a",
  window: "#f3dca6",
  windowMount: "#f6e2b0",
  windowGiant: "#e9c77a",
} as const;

/**
 * A clareira a noite (previa v5): as silhuetas, a nevoa e as samambaias saem multiplicadas por NIGHT; as janelas
 * acesas (cores que comecam por #f) continuam claras. As velas e as tochas viram a luz principal.
 */
export const NIGHT = 0.6;

export const SKY = {
  /** Fundo do palco antes do primeiro quadro (cor do canvas). */
  stage: "#0b0a1c",
  top: "#090618",
  mid: "#261b4f",
  horizon: "#8a5d3e",
  fog: "#1a1634",
  below: [0.08, 0.06, 0.15],
  sunLight: "#ffd9a0",
  /** Luar: a luz principal da cena (frio). */
  moonLight: "#c4d0ff",
} as const;

export const WORLD = {
  /** Terra batida do caminho (multiplica o scan do chao). */
  dirt: "#7a5a40",
  ground: "#132018",
  slabSide: "#4a4558",
  hemiSky: "#9f8fe6",
  hemiGround: "#0d1612",
  frontLight: "#ffe6c0",
  rim: "#9a77ff",
  runeGlow: "#9a77ff",
  ringOpen: "#e9f5a8",
  ringBurst: "#f3dca6",
  shaft: "#bccbff",
  dust: "#f3dca6",
  leaf: "#8fbf6a",
  firefly: "#d6f28a",
  magic: "#f6e7a6",
  innerLight: "#e2f2a0",
} as const;

/**
 * Faixas de bruma (decisao do usuario: a nevoa volta, a floresta continua de noite): azul frio e dessaturado, um pouco
 * mais claro que as silhuetas da noite e mais escuro que o horizonte, com mistura normal; `opacity` multiplica a
 * opacidade sorteada de cada faixa (0,16 a 0,26).
 */
export const MIST = {
  color: "#30344f",
  opacity: 1.3,
} as const;

/** Intensidades das luzes da noite (a cena le daqui; o fogo tem as suas em FIRE). */
export const LIGHTS = {
  hemi: 0.3,
  sun: 0.95,
  rim: 18,
  shaft: 0.06,
  shaftWave: 0.025,
  dust: 0.55,
} as const;

/** Velas, tochas e brasas. */
export const FIRE = {
  wrapGlow: "#ff6a1a",
  haloCandle: "#ffb468",
  haloTorch: "#ff9a44",
  lightCandle: "#ffae5c",
  lightTorch: "#ff9a4a",
  lightBoard: "#ffa65a",
  brass: "#c8a04a",
  /** Intensidade e alcance das luzes pontuais. */
  candle: { i0: 6.5, dist: 6.5 },
  torch: { i0: 26, dist: 14 },
  portal: { i0: 16, dist: 12 },
  board: { i0: 9, dist: 9 },
} as const;

/** O mural de avisos dos planos: madeira, telhado, placa entalhada e pergaminhos. */
export const BOARD = {
  beam: "#a0805f",
  post: "#8c6c50",
  beamBase: "#4a3222",
  shingleBase: "#1e140e",
  signBase: "#3a2618",
  signInk: "#e3bf72",
  parchment: ["#f3e7c6", "#e4d1a2", "#b8945a"],
  ink: "#24160b",
  red: "#7a1f14",
  gold: "#7a5512",
  seal: "#3d1d8f",
  sealLight: "#7a52e0",
  sealDark: "#33187a",
  /** Brilho proprio dos avisos (para serem lidos a noite). */
  noticeGlow: "#ffffff",
} as const;

export const WOOD = {
  base: "#2b1a10",
  metal: "#b8913f",
  chipSide: "#2b2233",
} as const;

export const CARD = {
  backFrom: "#3d1d96",
  backTo: "#1b0f45",
  gold: "#b88a2e",
  goldSoft: "#e0b866",
  face: "#fbf9ff",
  ink: "#1b1230",
  muted: "#4a3f6b",
  line: "#d9d0ee",
  band: "#ece6fb",
  brand: "#5b2fc4",
} as const;

/** Fichas de mana (branco, azul, preto, vermelho, verde), na ordem da fonte Mana. */
export const MANA = ["#f0f2c0", "#b5cde3", "#aca29a", "#db8664", "#93b483"] as const;

/** Cores do chao e do caminho na imagem estatica da clareira. */
export const STATIC_GROUND = {
  skyBelow: "#1a1a33",
  horizon: "#2b2a50",
  near: "#1a2626",
  far: "#0c140f",
  clearing: "#24352a",
  path: "#3b3a4c",
  pathLine: "rgba(10,10,20,.55)",
  skyStops: [
    ["0%", "#140c34"],
    ["22%", "#2a1d5e"],
    ["38%", "#4a3584"],
    ["47%", "#b98a72"],
    ["50%", "#e7b475"],
  ],
} as const;
