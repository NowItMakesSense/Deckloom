import type { ReactNode } from "react";

import "./Tag.css";

export interface TagProps {
  children: ReactNode;
}

export function Tag({ children }: TagProps) {
  return <span className="tag">{children}</span>;
}
