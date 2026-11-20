// Ceu noturno (pedido do usuario, 2026-11-15): a lua baixa a esquerda, visivel no voo de entrada e na clareira, e o
// luar como luz principal da cena. So numeros (sem three): o shader do ceu e a luz leem daqui.
import type { Vec3 } from "./math";

const ELEV = (10.5 * Math.PI) / 180;
const AZ = (-15 * Math.PI) / 180;

/** Direcao da lua (unitaria): 10,5 graus acima do horizonte, 15 graus a esquerda de quem olha para a clareira (-z). */
export const MOON_DIR: Vec3 = [
  Math.sin(AZ) * Math.cos(ELEV),
  Math.sin(ELEV),
  -Math.cos(AZ) * Math.cos(ELEV),
];

/** Raio aparente do disco da lua (graus): maior que o real, como numa ilustracao. */
export const MOON_RADIUS_DEG = 1.7;

/** Onde por a luz principal (direcional) para ela vir da lua, apontada para `target`. */
export function moonLightPosition(target: Vec3, dist: number): Vec3 {
  return [
    target[0] + MOON_DIR[0] * dist,
    target[1] + MOON_DIR[1] * dist,
    target[2] + MOON_DIR[2] * dist,
  ];
}
