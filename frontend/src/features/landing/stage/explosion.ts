// A explosao do deck (REQ-L10): cada carta tem trajetoria, rotacao e velocidade proprias. Funcoes puras.
import { type Vec3, clamp, rng, smooth } from "./math";

export interface CardBlast {
  /** Direcao unitaria da trajetoria. */
  readonly dir: Vec3;
  /** Distancia final do deslocamento. */
  readonly dist: number;
  /** Atraso (fracao) antes de a carta sair. */
  readonly delay: number;
  /** Rotacao final por eixo (rad). */
  readonly rot: Vec3;
  /** Expoente da aceleracao: velocidade propria. */
  readonly pow: number;
}

/**
 * Tres categorias, em rodizio: para a camera, para o fundo (saem por cima do quadro) e para os lados.
 * Deterministico: a semente e fixa.
 */
export function planExplosion(count: number): CardBlast[] {
  const r = rng(77);
  const list: CardBlast[] = [];
  for (let i = 0; i < count; i++) {
    const cat = i % 3;
    const th = r() * Math.PI * 2;
    const d: Vec3 =
      cat === 0
        ? [Math.cos(th) * 0.5, Math.sin(th) * 0.4 + 0.1, 1]
        : cat === 1
          ? [Math.cos(th) * 0.4, Math.sin(th) * 0.3, -1]
          : [Math.sign(Math.cos(th)) || 1, (r() - 0.4) * 0.5, (r() - 0.5) * 0.5];
    const len = Math.hypot(d[0], d[1], d[2]);
    list.push({
      dir: [d[0] / len, d[1] / len, d[2] / len],
      dist: 16 + r() * 16,
      delay: r() * 0.35,
      rot: [(r() - 0.5) * 14, (r() - 0.5) * 16, (r() - 0.5) * 12],
      pow: 1.6 + r() * 1.2,
    });
  }
  return list;
}

export interface BlastOffset {
  readonly offset: Vec3;
  readonly rotation: Vec3;
  /** Falso quando a carta ja saiu do quadro: a malha deixa de ser desenhada. */
  readonly visible: boolean;
  /** A carta encolhe no fim do voo: some de vista antes de deixar de ser desenhada. */
  readonly scale: number;
}

/** Deslocamento e rotacao a somar a pose da carta, dado o progresso `p` da mesa. */
export function explodedOffset(p: number, blast: CardBlast): BlastOffset {
  const se = clamp((p - 0.06 - blast.delay * 0.16) / 0.26, 0, 1);
  const e = Math.pow(se, blast.pow);
  // as que vao para o fundo sobem e saem por cima do quadro
  const lift = blast.dir[2] < -0.5 ? e * e * 18 : 0;
  return {
    offset: [
      blast.dir[0] * blast.dist * e + 0,
      blast.dir[1] * blast.dist * e + lift + 0,
      blast.dir[2] * blast.dist * e + 0,
    ],
    rotation: [blast.rot[0] * e + 0, blast.rot[1] * e + 0, blast.rot[2] * e + 0],
    visible: se < 1,
    scale: 1 - smooth((se - 0.8) / 0.2),
  };
}
