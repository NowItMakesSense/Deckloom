import { useState } from "react";
import type { CSSProperties } from "react";

import "./Avatar.css";

export interface AvatarProps {
  src: string;
  /** Rotulo do preset; ausente quando o avatar e customizado. */
  label?: string;
  custom?: boolean;
  /** Nome exibido ao lado: o avatar fica decorativo (alt vazio). */
  decorative?: boolean;
  name: string;
  /** Lado em px; o padrao e 40. */
  size?: number;
}

const DEFAULT_SIZE = 40;

function accessibleName({ custom, label }: AvatarProps): string {
  return custom ? "Seu avatar personalizado" : `Avatar: ${label ?? ""}`;
}

export function Avatar(props: AvatarProps) {
  const { src, decorative = false, name, size = DEFAULT_SIZE } = props;
  const style = { "--avatar-size": `${size}px` } as CSSProperties;
  const [failed, setFailed] = useState(false);

  if (failed) {
    const initial = Array.from(name.trim())[0]?.toUpperCase() ?? "";
    return (
      <span
        className="avatar avatar--fallback"
        style={style}
        role={decorative ? undefined : "img"}
        aria-label={decorative ? undefined : accessibleName(props)}
        aria-hidden={decorative ? "true" : undefined}
      >
        <span aria-hidden="true">{initial}</span>
      </span>
    );
  }

  return (
    <img
      className="avatar"
      src={src}
      alt={decorative ? "" : accessibleName(props)}
      style={style}
      width={size}
      height={size}
      onError={() => setFailed(true)}
    />
  );
}
