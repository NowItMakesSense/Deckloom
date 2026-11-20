// Lista de origem dos icones: todo arquivo de icons/ que nao e proprio precisa estar aqui (REQ-L07).
// Fonte unica: lucide-animated (MIT), repositorio pqoqubbw/icons. Nenhum icone de outra fonte.

export interface LucideAnimatedIcon {
  /** Arquivo copiado nesta pasta. */
  file: string;
  /** Arquivo de origem em pqoqubbw/icons (icons/<upstream>). */
  upstream: string;
  /** Nome exportado por index.ts. */
  component: string;
}

export const LUCIDE_ANIMATED_ICONS: readonly LucideAnimatedIcon[] = [
  { file: "arrow-right.tsx", upstream: "arrow-right.tsx", component: "ArrowRightIcon" },
  { file: "bell.tsx", upstream: "bell.tsx", component: "BellIcon" },
  { file: "book-text.tsx", upstream: "book-text.tsx", component: "BookTextIcon" },
  { file: "cart.tsx", upstream: "cart.tsx", component: "CartIcon" },
  { file: "chart-line.tsx", upstream: "chart-line.tsx", component: "ChartLineIcon" },
  { file: "check.tsx", upstream: "check.tsx", component: "CheckIcon" },
  { file: "chevron-right.tsx", upstream: "chevron-right.tsx", component: "ChevronRightIcon" },
  { file: "layers.tsx", upstream: "layers.tsx", component: "LayersIcon" },
  { file: "log-in.tsx", upstream: "login.tsx", component: "LogInIcon" },
  { file: "menu.tsx", upstream: "menu.tsx", component: "MenuIcon" },
  { file: "moon.tsx", upstream: "moon.tsx", component: "MoonIcon" },
  { file: "sparkles.tsx", upstream: "sparkles.tsx", component: "SparklesIcon" },
  { file: "sun.tsx", upstream: "sun.tsx", component: "SunIcon" },
  { file: "user-plus.tsx", upstream: "user-plus.tsx", component: "UserPlusIcon" },
  { file: "x.tsx", upstream: "x.tsx", component: "XIcon" },
];
