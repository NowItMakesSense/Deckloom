// Fases da floresta e da deck box como funcoes puras do progresso `raw` (0 a END) do scroll (REQ-L10, AC-L02m).
// Nada aqui conhece three: a cena so le os numeros e os aplica aos objetos.
import { type Vec3, clamp, lerp, smooth } from "./math";

/** Linha do tempo do palco, em fracao do scroll total. */
export const PH = {
  forest: 0.2,
  awake: 0.25,
  open: 0.29,
  rise: 0.33,
  intro: 0.38,
  story0: 0.38,
  story1: 0.6,
  regroup: 0.62,
  mesa: 0.92,
} as const;

/**
 * O fim da linha do tempo passa de 1: em 1 a camera para atras do portal (epilogo) e, entre PAN0 e PAN1, desliza
 * para a esquerda ate o mural de avisos com os planos. O scroll total vai de 0 a END.
 */
export const END = 1.1;
export const PAN0 = 1.005;
export const PAN1 = 1.075;

/** Recuo da camera da mesa: no celular (tela em pe) ela recua mais para caber todos os tokens. */
export function mesaK(mobile: boolean): number {
  return mobile ? 3.3 : 1.45;
}

/** Tamanho de cada um dos quatro segmentos dos ganchos. */
export const SEGL = (PH.story1 - PH.story0) / 4;
/** Altura do deck em destaque acima do altar. */
export const DY = 1.2;
export const BOXP: Vec3 = [0, 0, -0.4];
export const BOXT: Vec3 = [0, 1.0, -0.4];
export const BOX_HEIGHT = 1.85;

/** A explosao do deck corre dentro da fase da mesa: progresso 0 a 1 entre reagrupar e a mesa. */
export function mesaProgress(raw: number): number {
  return clamp((raw - PH.regroup) / (PH.mesa - PH.regroup), 0, 1);
}

/** Pulso da explosao (0 a 1): a floresta reage (folhas, nevoa, altar). */
export function burstAt(p: number): number {
  return smooth((p - 0.06) / 0.05) * (1 - smooth((p - 0.14) / 0.25));
}

/** Runa k do arco do portal: acende quando a camera passa por ela e fica acesa. */
export function archRuneGlow(raw: number, camZ: number, k: number): number {
  return raw > PH.forest ? 0.95 : 0.95 * smooth((72 - k * 2.6 - camZ) / 3);
}

/** Rastro de runas do caminho: acende depois do portal, a frente da camera, e fica aceso. */
export function pathRuneGlow(raw: number, camZ: number, runeZ: number, t: number): number {
  const after = raw > PH.forest;
  const past = after ? 1 : smooth((46 - camZ) / 4);
  const ahead = after ? 1 : smooth((16 - (camZ - runeZ)) / 5);
  return 0.9 * past * ahead * (0.8 + 0.2 * Math.sin(t * 2 + runeZ));
}

interface Ring {
  readonly scale: number;
  readonly opacity: number;
}

export interface BoxPhases {
  /** 0 a 1: as runas da caixa despertam. */
  readonly awake: number;
  /** 0 a 1: progresso da abertura (fecho, travas, tampa). */
  readonly openU: number;
  /** 0 a 1: as cartas sobem da caixa ate o deck. */
  readonly riseU: number;
  /** Rotacao X da tampa na dobradica (rad, negativo abre). */
  readonly lid: number;
  /** Rotacao X do fecho (rad). */
  readonly clasp: number;
  /** Rotacao Z da trava esquerda; a direita e o oposto (rad). */
  readonly latch: number;
  /** Luz dourada e verde que sai da caixa (0 a 1). */
  readonly light: number;
  /** Brilho das runas entalhadas (intensidade emissiva). */
  readonly runeGlow: number;
  /** Intensidade emissiva do altar. */
  readonly altarGlow: number;
  readonly ringOpen: Ring;
  readonly ringBurst: Ring;
  /** Pulso da explosao. */
  readonly burst: number;
  /** Progresso da mesa (0 a 1). */
  readonly p: number;
  /** Convergencia dos vaga-lumes para a caixa (0 a 1). */
  readonly conv: number;
  /** Fator de flutuacao ambiente do deck (0 a 1). */
  readonly float: number;
}

export function boxPhases(raw: number, t: number): BoxPhases {
  const p = mesaProgress(raw);
  const awake = smooth((raw - PH.forest) / (PH.awake - PH.forest));
  const openU = clamp((raw - PH.awake) / (PH.open - PH.awake), 0, 1);
  const riseU = clamp((raw - PH.open) / (PH.rise - PH.open), 0, 1);
  const lidA = smooth((openU - 0.28) / 0.72);
  const light = smooth((openU - 0.3) / 0.5) * (1 - 0.7 * smooth((raw - PH.intro) / 0.05));
  const float =
    smooth((raw - (PH.rise - 0.02)) / 0.02) * (1 - smooth((raw - (PH.intro - 0.015)) / 0.015));
  const burst = burstAt(p);
  const altarU = clamp((raw - PH.awake - 0.008) / 0.035, 0, 1);
  const bu = clamp((p - 0.06) / 0.12, 0, 1);
  return {
    awake,
    openU,
    riseU,
    lid: -lidA * 1.95 + Math.sin(lidA * Math.PI) * 0.03,
    // o fecho gira para fora e para baixo (o sinal fica em applyDeckBoxPose); as travas so destravam
    clasp: smooth(openU / 0.3) * 0.5,
    latch: smooth((openU - 0.1) / 0.2) * 0.45,
    light,
    runeGlow: awake * (1.4 + Math.sin(t * 2.4) * 0.25) + light * 1.2,
    altarGlow: smooth(altarU) * (0.9 + 0.15 * Math.sin(t * 1.6)) + burst * 1.2,
    ringOpen: {
      scale: lerp(1, 4.6, smooth(altarU)),
      opacity: Math.sin(Math.PI * altarU) * 0.85,
    },
    ringBurst: { scale: lerp(1, 9, smooth(bu)), opacity: Math.sin(Math.PI * bu) * 0.9 },
    burst,
    p,
    conv: smooth((raw - PH.forest) / (PH.open - PH.forest)),
    float,
  };
}
