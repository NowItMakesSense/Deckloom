// Matematica pura do palco (REQ-L10): sem three, sem DOM. Tudo deterministico.

export type Vec3 = readonly [number, number, number];

/** Gerador pseudoaleatorio deterministico (mulberry32): a mesma semente da sempre a mesma floresta. */
export function rng(seed: number): () => number {
  let state = seed | 0;
  return () => {
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function clamp(x: number, a: number, b: number): number {
  return Math.max(a, Math.min(b, x));
}

/** smoothstep de 0 a 1: sem degrau nas pontas. */
export function smooth(x: number): number {
  const c = clamp(x, 0, 1);
  return c * c * (3 - 2 * c);
}

export function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

export function lerp3(a: Vec3, b: Vec3, t: number): Vec3 {
  return [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
}

/** Interpola os campos vetoriais entre chaves ordenadas por `u`, com suavizacao em cada trecho. */
export function keyed<F extends string>(
  keys: ReadonlyArray<{ readonly u: number } & Readonly<Record<F, Vec3>>>,
  u: number,
  fields: readonly F[],
): Record<F, Vec3> {
  let i = 0;
  while (i < keys.length - 2 && u > keys[i + 1].u) i++;
  const a = keys[i];
  const b = keys[i + 1];
  const s = smooth((u - a.u) / (b.u - a.u));
  const out = {} as Record<F, Vec3>;
  for (const f of fields) out[f] = lerp3(a[f], b[f], s);
  return out;
}

/** Catmull-Rom uniforme: curva que passa por p1 (u = 0) e p2 (u = 1). */
export function catmull(p0: number, p1: number, p2: number, p3: number, u: number): number {
  const u2 = u * u;
  const u3 = u2 * u;
  return (
    0.5 *
    (2 * p1 +
      (-p0 + p2) * u +
      (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 +
      (-p0 + 3 * p1 - 3 * p2 + p3) * u3)
  );
}

/** Chave de um trilho: valor `v` no instante `t` (o progresso do scroll). */
export interface TrackKey {
  readonly t: number;
  readonly v: Vec3;
}

const same = (a: Vec3, b: Vec3) => a[0] === b[0] && a[1] === b[1] && a[2] === b[2];

/** Tangente (por unidade de t) na chave i: diferenca ponderada pelos dois lados; zero junto de uma pausa. */
function tangent(keys: readonly TrackKey[], i: number): Vec3 {
  const p = keys[i];
  const prev = keys[i - 1];
  const next = keys[i + 1];
  if ((prev && same(prev.v, p.v)) || (next && same(next.v, p.v))) return [0, 0, 0];
  if (!prev) {
    const h = next.t - p.t;
    return [(next.v[0] - p.v[0]) / h, (next.v[1] - p.v[1]) / h, (next.v[2] - p.v[2]) / h];
  }
  if (!next) {
    const h = p.t - prev.t;
    return [(p.v[0] - prev.v[0]) / h, (p.v[1] - prev.v[1]) / h, (p.v[2] - prev.v[2]) / h];
  }
  const h0 = p.t - prev.t;
  const h1 = next.t - p.t;
  const out = [0, 0, 0] as [number, number, number];
  for (let j = 0; j < 3; j++) {
    const d0 = (p.v[j] - prev.v[j]) / h0;
    const d1 = (next.v[j] - p.v[j]) / h1;
    out[j] = (d0 * h1 + d1 * h0) / (h0 + h1);
  }
  return out;
}

/**
 * Trilho continuo por chaves (Hermite cubico com nos nao uniformes, tipo Catmull-Rom): passa por todas as chaves e a
 * velocidade e continua nelas, entao o movimento nao para e reacelera a cada chave (o `keyed` parava). Duas chaves
 * iguais seguidas sao uma pausa: parado entre elas, chegando e saindo com velocidade zero.
 */
export function trackAt(keys: readonly TrackKey[], t: number): Vec3 {
  if (t <= keys[0].t) return keys[0].v;
  const last = keys[keys.length - 1];
  if (t >= last.t) return last.v;
  let i = 0;
  while (i < keys.length - 2 && t > keys[i + 1].t) i++;
  const a = keys[i];
  const b = keys[i + 1];
  if (same(a.v, b.v)) return a.v;
  const h = b.t - a.t;
  const s = (t - a.t) / h;
  const s2 = s * s;
  const s3 = s2 * s;
  const h00 = 2 * s3 - 3 * s2 + 1;
  const h10 = s3 - 2 * s2 + s;
  const h01 = -2 * s3 + 3 * s2;
  const h11 = s3 - s2;
  const ma = tangent(keys, i);
  const mb = tangent(keys, i + 1);
  return [0, 1, 2].map(
    (j) => h00 * a.v[j] + h10 * h * ma[j] + h01 * b.v[j] + h11 * h * mb[j],
  ) as unknown as Vec3;
}

/** Quaternion [x, y, z, w]. */
export type Quat = readonly [number, number, number, number];

/** Euler na ordem XYZ (a mesma do three) para quaternion. */
export function eulerToQuat(r: Vec3): Quat {
  const c1 = Math.cos(r[0] / 2);
  const c2 = Math.cos(r[1] / 2);
  const c3 = Math.cos(r[2] / 2);
  const s1 = Math.sin(r[0] / 2);
  const s2 = Math.sin(r[1] / 2);
  const s3 = Math.sin(r[2] / 2);
  return [
    s1 * c2 * c3 + c1 * s2 * s3,
    c1 * s2 * c3 - s1 * c2 * s3,
    c1 * c2 * s3 + s1 * s2 * c3,
    c1 * c2 * c3 - s1 * s2 * s3,
  ];
}

/** Quaternion para Euler XYZ (a mesma conta do three). */
export function quatToEuler(q: Quat): Vec3 {
  const [x, y, z, w] = q;
  const m11 = 1 - 2 * (y * y + z * z);
  const m12 = 2 * (x * y - z * w);
  const m13 = 2 * (x * z + y * w);
  const m22 = 1 - 2 * (x * x + z * z);
  const m23 = 2 * (y * z - x * w);
  const m32 = 2 * (y * z + x * w);
  const m33 = 1 - 2 * (x * x + y * y);
  const ry = Math.asin(clamp(m13, -1, 1));
  if (Math.abs(m13) < 0.9999999) return [Math.atan2(-m23, m33), ry, Math.atan2(-m12, m11)];
  return [Math.atan2(m32, m22), ry, 0];
}

/** Interpolacao esferica pelo caminho mais curto. */
export function slerp(a: Quat, b: Quat, s: number): Quat {
  let dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
  const bb = dot < 0 ? (b.map((v) => -v) as unknown as Quat) : b;
  dot = Math.abs(dot);
  if (dot > 0.9995) {
    const out = a.map((v, i) => v + (bb[i] - v) * s);
    const len = Math.hypot(...out);
    return out.map((v) => v / len) as unknown as Quat;
  }
  const th = Math.acos(dot);
  const sn = Math.sin(th);
  const ka = Math.sin((1 - s) * th) / sn;
  const kb = Math.sin(s * th) / sn;
  return a.map((v, i) => v * ka + bb[i] * kb) as unknown as Quat;
}
