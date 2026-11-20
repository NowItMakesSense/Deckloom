// A clareira como imagem estatica (REQ-L10): o fallback sem WebGL ou com prefers-reduced-motion e tambem o
// cartaz que aparece enquanto o palco carrega. SVG desenhado em codigo com as mesmas silhuetas e a mesma paleta
// do palco, visto da ultima pose da camera do sobrevoo. Decorativo (aria-hidden).
import { useId } from "react";

import { STATIC_VIEW, clearingGround, projectLayer, toSvgPath } from "./clearing";
import { FOREST, SKY, STATIC_GROUND, WOOD, WORLD } from "./palette";
import { CLEARING_Z, STATIC_CAMERA, heroLayers } from "./silhouettes";

const LAYERS = heroLayers();
const GROUND = clearingGround(STATIC_VIEW, STATIC_CAMERA);
const BOX_LAYER = { id: "deck-box", z: CLEARING_Z, W: 4, H: 5 } as const;
const BOX_PLACEMENT = projectLayer(BOX_LAYER, STATIC_CAMERA, STATIC_VIEW);

const points = (pts: ReadonlyArray<readonly [number, number]>) =>
  pts.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(" ");

/** A deck box em silhueta direto no chao do altar, com o brilho tenue das runas. */
function BoxSilhouette({ glowId }: { glowId: string }) {
  if (!BOX_PLACEMENT) return null;
  const { tx, ty, scale } = BOX_PLACEMENT;
  return (
    <g
      transform={`translate(${tx.toFixed(1)} ${ty.toFixed(1)}) scale(${scale.toFixed(3)}) translate(4 5)`}
    >
      <ellipse cx="0" cy="-1.25" rx="2.6" ry="2" fill={`url(#${glowId})`} />
      <rect
        x="-0.75"
        y="-2.3"
        width="1.5"
        height="2.3"
        rx=".05"
        fill={WOOD.base}
        stroke={WOOD.metal}
        strokeWidth=".06"
      />
      <rect
        x="-0.78"
        y="-2.4"
        width="1.56"
        height=".32"
        fill={WOOD.base}
        stroke={WOOD.metal}
        strokeWidth=".05"
      />
      <g fill={FOREST.windowGiant} opacity=".55">
        <circle cx="-.35" cy="-1.25" r=".05" />
        <circle cx="0" cy="-1.5" r=".05" />
        <circle cx=".35" cy="-1.25" r=".05" />
        <circle cx="0" cy="-1" r=".05" />
      </g>
    </g>
  );
}

export function StaticClearing({ className }: { className?: string }) {
  const uid = useId().replaceAll(":", "");
  const sky = `sky-${uid}`;
  const sun = `sun-${uid}`;
  const ground = `ground-${uid}`;
  const glow = `glow-${uid}`;
  const { w, h } = STATIC_VIEW;
  return (
    <svg
      className={className}
      viewBox={`0 0 ${w} ${h}`}
      preserveAspectRatio="xMidYMid slice"
      aria-hidden="true"
      focusable="false"
      data-testid="static-clearing"
    >
      <defs>
        <linearGradient id={sky} x1="0" y1="0" x2="0" y2="1">
          {STATIC_GROUND.skyStops.map(([offset, color]) => (
            <stop key={offset} offset={offset} stopColor={color} />
          ))}
          <stop offset="50.2%" stopColor={SKY.fog} />
          <stop offset="100%" stopColor={STATIC_GROUND.skyBelow} />
        </linearGradient>
        <radialGradient id={sun}>
          <stop offset="0" stopColor="rgb(255,230,170)" stopOpacity=".9" />
          <stop offset=".3" stopColor="rgb(255,200,130)" stopOpacity=".35" />
          <stop offset=".7" stopColor="rgb(255,180,120)" stopOpacity="0" />
        </radialGradient>
        <linearGradient id={ground} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor={STATIC_GROUND.horizon} />
          <stop offset=".08" stopColor={STATIC_GROUND.near} />
          <stop offset="1" stopColor={STATIC_GROUND.far} />
        </linearGradient>
        <radialGradient id={glow}>
          <stop offset="0" stopColor={WORLD.runeGlow} stopOpacity=".45" />
          <stop offset="1" stopColor={WORLD.runeGlow} stopOpacity="0" />
        </radialGradient>
      </defs>
      <rect width={w} height={h} fill={`url(#${sky})`} />
      <circle cx={w * 0.3} cy={h * 0.5} r={Math.min(w, h) * 0.35} fill={`url(#${sun})`} />
      <rect y={GROUND.horizonY} width={w} height={h - GROUND.horizonY} fill={`url(#${ground})`} />
      <polygon points={points(GROUND.clearing)} fill={STATIC_GROUND.clearing} />
      <polygon points={points(GROUND.path)} fill={STATIC_GROUND.path} />
      {GROUND.stoneLines.map(([a, b]) => (
        <line
          key={a[1]}
          x1={a[0]}
          y1={a[1]}
          x2={b[0]}
          y2={b[1]}
          stroke={STATIC_GROUND.pathLine}
          strokeWidth="1"
        />
      ))}
      {LAYERS.map((layer) => {
        const place = projectLayer(layer, STATIC_CAMERA, STATIC_VIEW);
        if (!place) return null;
        return (
          <g
            key={layer.id}
            transform={`translate(${place.tx.toFixed(1)} ${place.ty.toFixed(1)}) scale(${place.scale.toFixed(4)}) translate(${layer.W} ${layer.H})`}
          >
            <path d={toSvgPath(layer.body)} fill={layer.color} />
            {layer.moss ? (
              <path d={toSvgPath({ polys: [], circles: layer.moss })} fill={layer.mossColor} />
            ) : null}
            {layer.accents && layer.accentColor ? (
              <path d={toSvgPath(layer.accents)} fill={layer.accentColor} opacity=".9" />
            ) : null}
          </g>
        );
      })}
      <BoxSilhouette glowId={glow} />
    </svg>
  );
}
