// Pedras com musgo (rocks.glb: Rock0 a Rock3) e tocos (stump.glb), modelos do Poly Haven (CC0) reduzidos em
// design/3d/import_ph.py. So posicoes (sem three): o caminho, a borda da clareira e o pe do portal.

/** Quantas pedras diferentes o rocks.glb traz (nos Rock0 ... Rock3). */
export const ROCK_VARIANTS = 4;

export interface Prop {
  readonly kind: "rock" | "stump";
  /** Qual pedra do rocks.glb (so para kind = "rock"). */
  readonly variant: number;
  readonly x: number;
  readonly z: number;
  /** Giro em torno de y (rad) e escala uniforme. */
  readonly yaw: number;
  readonly scale: number;
}

const rock = (variant: number, x: number, z: number, yaw: number, scale: number): Prop => ({
  kind: "rock",
  variant,
  x,
  z,
  yaw,
  scale,
});
const stump = (x: number, z: number, yaw: number, scale: number): Prop => ({
  kind: "stump",
  variant: 0,
  x,
  z,
  yaw,
  scale,
});

export const PROPS: readonly Prop[] = [
  rock(0, 6.5, 45.5, 0.4, 0.7), // pe do portal, a direita (o corredor do mural fica a esquerda)
  rock(2, 4.2, 33, 2.1, 0.6),
  rock(3, -3.6, 27, 1.2, 0.8),
  rock(1, 7.5, 14.5, 0.3, 0.5),
  rock(1, -9.5, 2.0, 2.8, 0.7), // borda da clareira
  rock(0, 10.5, -2.5, 4.0, 0.6),
  // nas bordas da clareira (atras do altar, contra o fundo escuro, pareciam flutuar)
  rock(2, -11.0, -6.0, 0.9, 0.9),
  rock(3, 12.0, 2.0, 5.1, 1.0),
  stump(-8.6, 18.5, 0.7, 0.9),
  stump(10.2, 29, 2.3, 0.8),
];
