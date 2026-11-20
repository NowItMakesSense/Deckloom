// T-L01B / AC-L01Ba e AC-L01Bb: icones do lucide-animated (MIT), acessiveis e sem animar com reducao de movimento.
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const started = vi.hoisted(() => ({ calls: [] as string[] }));
vi.mock("motion/react", async (importOriginal) => {
  const actual = await importOriginal<typeof import("motion/react")>();
  return {
    ...actual,
    useAnimation: () => {
      const controls = actual.useAnimation();
      const start = controls.start.bind(controls);
      controls.start = ((def: string) => {
        started.calls.push(String(def));
        return start(def);
      }) as typeof controls.start;
      return controls;
    },
  };
});

import * as icons from "./index";
import { LUCIDE_ANIMATED_ICONS } from "./sources";

const raw = import.meta.glob(["./*.tsx", "./*.ts", "!./*.test.*"], {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

const OWN_FILES = ["./index.ts", "./AnimatedIcon.tsx", "./sources.ts"];

function setReduced(reduced: boolean) {
  vi.stubGlobal(
    "matchMedia",
    vi.fn((query: string) => ({
      matches: reduced && query.includes("prefers-reduced-motion: reduce"),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  );
}

beforeEach(() => {
  started.calls.length = 0;
});
afterEach(() => vi.unstubAllGlobals());

describe("origem dos icones (REQ-L07)", () => {
  it("todo arquivo de icons/ e um icone listado da fonte lucide-animated ou um arquivo proprio", () => {
    const listed = LUCIDE_ANIMATED_ICONS.map((i) => `./${i.file}`);
    const stray = Object.keys(raw).filter((f) => !listed.includes(f) && !OWN_FILES.includes(f));
    expect(stray).toEqual([]);
    listed.forEach((f) => expect(raw[f], `${f} listado mas ausente`).toBeDefined());
  });

  it("cada icone copiado traz cabecalho com a origem e a licenca MIT", () => {
    LUCIDE_ANIMATED_ICONS.forEach(({ file, upstream }) => {
      const head = raw[`./${file}`].slice(0, 600);
      expect(head).toContain("lucide-animated");
      expect(head).toContain("MIT");
      expect(head).toContain(upstream);
    });
  });

  it("nao ha SVG desenhado a mao fora dos arquivos da fonte", () => {
    OWN_FILES.forEach((f) => expect(raw[f]).not.toMatch(/<svg|<path/));
  });

  it("o aviso MIT esta em THIRD_PARTY_NOTICES.md", async () => {
    const { readFileSync } = await import("node:fs");
    const notice = readFileSync("THIRD_PARTY_NOTICES.md", "utf8");
    expect(notice).toContain("lucide-animated");
    expect(notice).toContain("MIT License");
    expect(notice).toContain("pqoqubbw");
  });
});

const exported = LUCIDE_ANIMATED_ICONS.map((i) => i.component);

describe("icones exportados", () => {
  it("exporta exatamente os componentes da lista de origem", () => {
    expect(Object.keys(icons).sort()).toEqual([...exported].sort());
  });

  it.each(exported)("%s e decorativo (aria-hidden) por padrao", (name) => {
    const Icon = (icons as Record<string, React.ComponentType<{ "data-testid"?: string }>>)[name];
    const { container } = render(<Icon />);
    const root = container.firstElementChild as HTMLElement;
    expect(root).toHaveAttribute("aria-hidden", "true");
    expect(container.querySelector("svg")).not.toBeNull();
    expect(container.querySelector("svg")?.getAttribute("stroke")).toBe("currentColor");
  });

  it.each(exported)("%s com label tem nome acessivel e nao e aria-hidden", (name) => {
    const Icon = (icons as Record<string, React.ComponentType<{ label?: string }>>)[name];
    render(<Icon label="Rotulo teste" />);
    const img = screen.getByRole("img", { name: "Rotulo teste" });
    expect(img).not.toHaveAttribute("aria-hidden");
  });
});

describe("movimento", () => {
  it("sem reducao, passar o mouse anima e sair volta ao normal", () => {
    setReduced(false);
    const { container } = render(<icons.UserPlusIcon />);
    const root = container.firstElementChild as HTMLElement;
    fireEvent.mouseEnter(root);
    expect(started.calls).toContain("animate");
    fireEvent.mouseLeave(root);
    expect(started.calls).toContain("normal");
  });

  it.each(LUCIDE_ANIMATED_ICONS.map((i) => i.component))(
    "%s com prefers-reduced-motion nao anima",
    (name) => {
      setReduced(true);
      const Icon = (icons as Record<string, React.ComponentType>)[name];
      const { container } = render(<Icon />);
      const root = container.firstElementChild as HTMLElement;
      fireEvent.mouseEnter(root);
      fireEvent.mouseLeave(root);
      expect(started.calls).not.toContain("animate");
    },
  );

  it("repassa os handlers do chamador", () => {
    setReduced(true);
    const onMouseEnter = vi.fn();
    const { container } = render(<icons.BellIcon onMouseEnter={onMouseEnter} />);
    fireEvent.mouseEnter(container.firstElementChild as HTMLElement);
    expect(onMouseEnter).toHaveBeenCalledTimes(1);
  });
});
