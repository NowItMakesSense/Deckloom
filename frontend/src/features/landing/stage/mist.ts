// As faixas de nevoa da floresta (REQ-L10) como funcoes puras. Decisao do usuario (2026-11-13): a nevoa volta e a
// floresta continua de noite. A nevoa e bruma entre as camadas de silhueta (profundidade a la Firewatch): cor fria e
// dessaturada, um pouco mais clara que as silhuetas e mais escura que o horizonte, mistura normal, opacidade moderada,
// deriva lenta. Afina na explosao do deck.
import { rng, smooth } from "./math";
import { MIST } from "./palette";

export interface MistBand {
  readonly w: number;
  readonly h: number;
  /** Opacidade de sorteio (0,16 a 0,26), antes do fator da paleta. */
  readonly opacity: number;
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly phase: number;
}

/** Doze faixas ao longo da floresta; nenhuma atravessa o mural de avisos (z perto de 46). */
export function mistBands(): MistBand[] {
  const r = rng(140);
  return Array.from({ length: 12 }, (_, i) => {
    const w = 90 + r() * 60;
    const h = 7 + r() * 5;
    const opacity = 0.16 + r() * 0.1;
    const x = (r() - 0.5) * 30;
    const y = 2.4 + r() * 2;
    let z = -90 + i * 17;
    if (Math.abs(z - 46) < 6) z = 37;
    return { w, h, opacity, x, y, z, phase: r() * 6.28 };
  });
}

/** Teto da opacidade de uma faixa (a maior opacidade de sorteio vezes o fator da paleta). */
export const MIST_MAX_OPACITY = 0.26 * MIST.opacity;
/** Alfa do miolo do sprite da faixa (o degrade radial vai de 0,9 no centro a 0 na borda). */
export const MIST_SPRITE_PEAK = 0.9;

/** Posicao x da faixa no tempo `t` (s): deriva lenta de ate 6 unidades para cada lado. */
export function mistDrift(band: MistBand, t: number): number {
  return band.x + Math.sin(t * 0.05 + band.phase) * 6;
}

/**
 * Perto da camera a faixa some (de 4 a 14 unidades de distancia em z): dentro da bruma nao se ve o veu, e uma faixa
 * colada na lente lavaria o quadro inteiro.
 */
export function mistNearFade(dz: number): number {
  return smooth((Math.abs(dz) - 4) / 10);
}

/**
 * Opacidade da faixa no quadro: a explosao do deck afina a nevoa pela metade no pico; `camDz` e a distancia em z da
 * camera ate a faixa (sem ela, a faixa esta longe).
 */
export function mistOpacity(band: MistBand, burst: number, camDz = Infinity): number {
  return band.opacity * MIST.opacity * (1 - 0.5 * burst) * mistNearFade(camDz);
}

/** Luminancia (0 a 255, pesos Rec. 709 sobre sRGB) de uma cor hexadecimal. */
export function lumaOf(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** Banco de nevoa baixa (pedido do usuario, 2026-11-15): um veu macio que sempre encara a camera. */
export interface FogBank {
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly w: number;
  readonly h: number;
  readonly opacity: number;
  readonly phase: number;
}

/**
 * Bancos de nevoa volumetrica: camadas baixas ao longo de todo o caminho (da entrada ao altar), entre as arvores dos
 * dois lados e em volta do altar, que fica parcialmente envolto. Deterministico.
 */
export function fogBanks(): FogBank[] {
  const r = rng(141);
  const out: FogBank[] = [];
  // ao longo do caminho: um banco a cada ~3 m, alternando os lados, alguns cruzando o caminho
  for (let z = 4; z <= 72; z += 3) {
    const x = (r() - 0.5) * 9;
    out.push({
      x,
      y: 0.4 + r() * 1.2,
      z: z + (r() - 0.5) * 2,
      w: 7 + r() * 6,
      h: 2.2 + r() * 1.6,
      opacity: 0.3 + r() * 0.2,
      phase: r() * 6.28,
    });
  }
  // entre as arvores, dos dois lados, ate 40 m do caminho
  for (let k = 0; k < 40; k++) {
    const side = k % 2 ? 1 : -1;
    out.push({
      x: side * (8 + r() * 32),
      y: 0.6 + r() * 2.2,
      z: -30 + r() * 110,
      w: 10 + r() * 10,
      h: 3 + r() * 2,
      opacity: 0.3 + r() * 0.25,
      phase: r() * 6.28,
    });
  }
  // em volta do altar: o altar fica parcialmente envolto
  for (let k = 0; k < 8; k++) {
    const a = (k / 8) * Math.PI * 2 + r() * 0.4;
    const rad = 5 + r() * 2.5;
    out.push({
      x: Math.cos(a) * rad,
      y: 0.3 + r() * 0.6,
      z: Math.sin(a) * rad,
      w: 6 + r() * 3,
      h: 1.8 + r() * 0.8,
      opacity: 0.28 + r() * 0.12,
      phase: r() * 6.28,
    });
  }
  return out;
}

/** Opacidade do banco pela distancia ate a camera: some de perto (sem parede de nevoa na cara da camera). */
export function fogBankOpacity(b: FogBank, dist: number): number {
  const t = Math.min(1, Math.max(0, (dist - 1.5) / 6.5));
  return b.opacity * t * t * (3 - 2 * t);
}
