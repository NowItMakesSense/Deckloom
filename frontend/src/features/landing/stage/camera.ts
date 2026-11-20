// A camera do palco (REQ-L10): uma unica funcao `camPose(raw, t)` continua, do sobrevoo das copas ate o
// epilogo pelo portal e o pan final ate o mural de avisos. Sem three: devolve numeros; a cena aplica ao
// PerspectiveCamera.
import { EPILOGUE_END, boardLayout, panCam } from "./board";
import { type TrackKey, type Vec3, lerp, smooth, trackAt } from "./math";
import { BOXP, DY, END, PAN0, PAN1, PH, SEGL, boxPhases, mesaK, mesaProgress } from "./phases";

export interface CamPose {
  readonly pos: Vec3;
  readonly tgt: Vec3;
  /** Giro em torno do eixo de visao (rad). */
  readonly roll: number;
  /** Indice (0 a 3) do gancho em foco, ou -1 fora dos ganchos. */
  readonly active: number;
  /** Quanto o foco da profundidade de campo vai do alvo para a carta ativa (0 a 1). */
  readonly cardFocus: number;
  /** A fase da caixa foca o interior da deck box, nao o alvo da camera. */
  readonly boxFocus: boolean;
}

interface PosTgt {
  readonly pos: Vec3;
  readonly tgt: Vec3;
}
interface Key extends PosTgt {
  readonly u: number;
}

const BASE: PosTgt = { pos: [0, 2.5 + DY, 9.5], tgt: [0, 2.1 + DY, 0] };
const LEAN: PosTgt = { pos: [0.25, 3.0, 2.3], tgt: [0, 1.7, -0.45] };

/** No celular (tela em pe e estreita) as poses perto da caixa recuam, para a deck box caber inteira na largura. */
const MOBILE_BACK = 1.75;

function boxCam(u: number, mobile = false): PosTgt {
  const e = smooth(u);
  const a = lerp(0.18, -0.35, e);
  const r = lerp(6.6, 3.8, e) * (mobile ? MOBILE_BACK : 1);
  return {
    pos: [Math.sin(a) * r, lerp(1.65, 1.5, e), BOXP[2] + Math.cos(a) * r],
    tgt: [0, lerp(0.95, 1.1, e), BOXP[2]],
  };
}

const B0 = boxCam(0);

/** Pontos-chave da floresta (Catmull-Rom); o ultimo e a primeira pose da orbita da caixa. */
const FOREST: ReadonlyArray<readonly [number, Vec3, Vec3]> = [
  // o alvo avanca sempre na mesma direcao (de 60 ate a caixa): o olhar nao vai e volta durante o voo
  [0.0, [0, 19, 150], [0, 13, 60]],
  [0.18, [6, 18.2, 122], [-1.5, 11, 42]],
  [0.36, [-5, 16.5, 94], [0, 8, 32]],
  [0.52, [1.5, 12, 82], [0, 4.6, 26]],
  [0.68, [-0.8, 4.2, 56], [0, 3.2, 18]],
  [0.82, [0.3, 3.0, 34], [0, 2.2, 6]],
  [0.92, [-0.6, 2.5, 16], [0, 1.5, -0.4]],
  [1.0, B0.pos, B0.tgt],
];

function introCam(u: number, t: number, f: number): { pos: Vec3; tgt: Vec3; roll: number } {
  const e = smooth(u);
  const wob = Math.sin(u * Math.PI * 2) * (1 - u);
  const a = lerp(-0.75, 0, e) + Math.sin(u * Math.PI * 3) * 0.12 * (1 - u);
  const r = lerp(5.4, 9.5, e) - wob * 0.9;
  const h = lerp(3.3, 2.5, e) + Math.sin(u * Math.PI * 3 + 0.6) * 0.35 * (1 - u) + DY;
  const pos: [number, number, number] = [Math.sin(a) * r, h, Math.cos(a) * r];
  const tgt: Vec3 = [Math.sin(u * 5) * 0.15 * (1 - u), 2.1 + DY, -0.35 * (1 - e)];
  pos[0] += Math.sin(t * 0.37) * 0.16 * f;
  pos[1] += Math.sin(t * 0.29 + 1) * 0.08 * f;
  return {
    pos,
    tgt,
    roll: Math.sin(u * Math.PI * 2) * 0.05 * (1 - u) + Math.sin(t * 0.21) * 0.01 * f,
  };
}

function storyCamKeys(k: number, mobile: boolean): Key[] {
  const side = k % 2 ? 1 : -1;
  const inP: PosTgt = mobile
    ? { pos: [0, 2.6 + DY, 8.6], tgt: [0, 2.3 + DY, 4] }
    : { pos: [-0.3, 2.5 + DY, 8.3], tgt: [0.25, 2.45 + DY, 4] };
  return [
    { u: 0, ...BASE },
    { u: 0.27, pos: [side * 0.25, 2.65 + DY, 9.0], tgt: [side * 0.5, 2.6 + DY, 0.8] },
    { u: 0.62, ...inP },
    // a revelacao do gancho: a camera para enquanto a carta brilha (pausa declarada em CAMERA_HOLDS)
    { u: 0.85, ...inP },
    { u: 1, ...BASE },
  ];
}

const STORY_KEYS = [false, true].map((mobile) => [0, 1, 2, 3].map((k) => storyCamKeys(k, mobile)));

// reagrupar: a camera ja segue, sem voltar, para o mergulho da mesa (ida e volta aqui era uma parada)
const REGROUP: Key[] = [
  { u: 0, ...BASE },
  { u: 0.5, pos: [-0.25, 2.62 + DY, 8.6], tgt: [-0.2, 2.15 + DY, 0] },
];

/** A mesa: a camera mergulha, circula sobre o altar e se aproxima dos tokens. */
const MESA: ReadonlyArray<readonly [number, Vec3, Vec3]> = [
  [0.0, [0, 2.5 + DY, 9.5], [0, 2.1 + DY, 0]],
  [0.12, [0.3, 3.0 + DY, 6.0], [0, 2.4 + DY, 0]],
  [0.27, [0, 3.4 + DY, 2.0], [0, 3.0 + DY, -3]],
  [0.4, [0.8, 5.6, 8.0], [0, 0, -0.2]],
  [0.55, [-1.6, 4.4, 6.6], [0.4, 0, 0.1]],
  [0.7, [1.2, 5.0, 6.8], [-0.2, 0, 0]],
  [0.86, [0.3, 3.6, 5.2], [0, 0.1, 0]],
  [1.0, [-0.8, 3.0, 4.4], [0.1, 0.1, -0.1]],
];

/** Lente da camera (fov vertical, graus): tela em pe usa lente mais aberta. */
export function fovFor(aspect: number): number {
  return aspect < 0.8 ? 62 : 42;
}

const BOARDS = [boardLayout(false), boardLayout(true)];

/**
 * Pausas declaradas do trilho (a camera fica parada de proposito): atras do portal antes do pan e no mural, no fim.
 * Fora delas a camera nunca para (continuity.test.ts).
 */
export const CAMERA_HOLDS: ReadonlyArray<readonly [number, number]> = [
  // a revelacao de cada gancho (chaves 0,62 e 0,85 do segmento): a camera para enquanto a carta brilha
  ...[0, 1, 2, 3].map(
    (k) => [PH.story0 + (k + 0.62) * SEGL, PH.story0 + (k + 0.85) * SEGL] as const,
  ),
  [1, PAN0],
  [PAN1, END],
];

interface CamKey {
  readonly t: number;
  readonly pos: Vec3;
  readonly tgt: Vec3;
  readonly roll: number;
}

/** Todas as chaves da landing numa linha so (progresso do scroll), da entrada nas copas ate o mural. */
function cameraKeys(mobile: boolean, aspect: number): CamKey[] {
  const keys: CamKey[] = [];
  const add = (t: number, pt: PosTgt, roll = 0) => keys.push({ t, pos: pt.pos, tgt: pt.tgt, roll });
  // floresta (a ultima chave e a primeira pose da orbita da caixa)
  for (const [u, pos, tgt] of FOREST.slice(0, -1))
    add(u * PH.forest, { pos, tgt }, u < 0.6 ? 0.025 : 0);
  // a caixa: orbita curta, depois se inclina sobre a tampa
  for (const u of [0, 0.5, 1]) add(PH.forest + u * (PH.awake - PH.forest), boxCam(u, mobile));
  add(PH.open, mobile ? { pos: [0.25, 3.6, 4.7], tgt: [0, 1.4, -0.45] } : LEAN);
  add(
    PH.open + 0.45 * (PH.rise - PH.open),
    mobile
      ? { pos: [0.1, 4.1, 6.4], tgt: [0, 2.9, -0.3] }
      : { pos: [0.1, 3.6, 3.6], tgt: [0, 2.9, -0.3] },
  );
  // o deck sobe e a camera recua orbitando ate a pose-base
  for (const u of [0, 0.35, 0.7, 1]) {
    const c = introCam(u, 0, 0);
    if (mobile) {
      // a orbita de apresentacao tambem recua no celular (a caixa e o deck cabem na largura)
      const k = lerp(1.3, 1, smooth(u));
      for (const j of [0, 2] as const)
        (c.pos as unknown as number[])[j] = c.tgt[j] + (c.pos[j] - c.tgt[j]) * k;
    }
    add(PH.rise + u * (PH.intro - PH.rise), c, c.roll);
  }
  // os quatro ganchos: aproxima, revela e volta a pose-base
  for (let k = 0; k < 4; k++) {
    const side = k % 2 ? 1 : -1;
    for (const key of STORY_KEYS[mobile ? 1 : 0][k].slice(1)) {
      const roll = key.u > 0.2 && key.u < 0.7 ? -side * 0.035 : 0;
      add(PH.story0 + (k + key.u) * SEGL, key, roll);
    }
  }
  for (const key of REGROUP.slice(1)) add(PH.story1 + key.u * (PH.regroup - PH.story1), key);
  // a mesa comeca no fim do reagrupamento: a primeira chave dela (MESA[0]) e a pose-base e fica de fora
  // a mesa: mergulha, circula sobre o altar e recua quando os tokens caem (o celular recua mais)
  const mk = mesaK(mobile);
  for (const [u, pos, tgt] of MESA.slice(1)) {
    const kk = mk > 1 ? 1 + (mk - 1) * smooth((u - 0.36) / 0.16) : 1;
    const p = [0, 1, 2].map((j) => tgt[j] + (pos[j] - tgt[j]) * kk) as unknown as Vec3;
    add(PH.regroup + u * (PH.mesa - PH.regroup), { pos: p, tgt });
  }
  // epilogo pelo caminho ate atras do portal; pausa; pan ate o mural; pausa no mural
  add(PH.mesa + 0.45 * (1 - PH.mesa), { pos: [0.2, 3.2, 24], tgt: [0, 1.5, 0] });
  add(1, EPILOGUE_END);
  add(PAN0, EPILOGUE_END);
  const board = BOARDS[mobile ? 1 : 0];
  for (const u of [0.25, 0.5, 0.75, 1])
    add(PAN0 + u * (PAN1 - PAN0), panCam(u, board, fovFor(aspect), aspect, mobile));
  const fin = keys[keys.length - 1];
  add(END, { pos: fin.pos, tgt: fin.tgt });
  return keys;
}

interface Tracks {
  readonly pos: TrackKey[];
  readonly tgt: TrackKey[];
  readonly roll: TrackKey[];
}

const TRACKS = new Map<string, Tracks>();
function tracks(mobile: boolean, aspect: number): Tracks {
  const id = `${mobile}:${aspect.toFixed(3)}`;
  let tr = TRACKS.get(id);
  if (!tr) {
    const keys = cameraKeys(mobile, aspect);
    tr = {
      pos: keys.map((k) => ({ t: k.t, v: k.pos })),
      tgt: keys.map((k) => ({ t: k.t, v: k.tgt })),
      roll: keys.map((k) => ({ t: k.t, v: [k.roll, 0, 0] as Vec3 })),
    };
    TRACKS.set(id, tr);
  }
  return tr;
}

/**
 * A pose da camera para o progresso `raw` (0 a END) e o tempo `t` (s) da respiracao ambiente. Um trilho so, com
 * velocidade continua por toda a landing (sem parar a cada chave); a respiracao depende so do tempo e e lenta.
 * `aspect` e o formato da tela: o enquadramento final do mural depende dele.
 */
export function camPose(
  raw: number,
  t: number,
  mobile: boolean,
  aspect: number = mobile ? 375 / 812 : 1366 / 768,
): CamPose {
  const tr = tracks(mobile, aspect);
  const pos = [...trackAt(tr.pos, raw)] as [number, number, number];
  const tgt = [...trackAt(tr.tgt, raw)] as [number, number, number];
  const roll = trackAt(tr.roll, raw)[0];
  // respiracao ambiente: lenta (abaixo de 0,1 Hz) e pequena, so enquanto o deck flutua e na mesa
  const ph = boxPhases(raw, t);
  const live = Math.max(
    ph.float,
    smooth((raw - PH.regroup) / 0.02) * (1 - smooth((raw - PH.mesa) / 0.02)),
  );
  pos[0] += Math.sin(t * 0.37) * 0.12 * live;
  pos[1] += Math.sin(t * 0.29 + 1) * 0.06 * live;

  let active = -1;
  let cardFocus = 0;
  if (raw >= PH.story0 && raw < PH.story1) {
    active = Math.min(3, Math.floor((raw - PH.story0) / SEGL));
    const uc = (raw - PH.story0 - active * SEGL) / SEGL;
    cardFocus = smooth((uc - 0.3) / 0.3) * (1 - smooth((uc - 0.86) / 0.1));
  }
  const boxFocus = raw >= PH.forest && raw < PH.rise;
  return { pos, tgt, roll, active, cardFocus, boxFocus };
}

/** Abertura da profundidade de campo: ampla na clareira e na caixa, fechando ao sair pelo portal. */
export function aperture(raw: number): number {
  const p = mesaProgress(raw);
  if (raw < PH.forest) return lerp(2.5, 8, smooth((raw / PH.forest - 0.75) / 0.25));
  if (raw < PH.intro) return 12;
  if (raw < PH.regroup) return 10;
  if (raw >= PH.mesa) return lerp(4, 1.2, smooth((raw - PH.mesa) / 0.05));
  return p < 0.3 ? 3 : 10;
}
