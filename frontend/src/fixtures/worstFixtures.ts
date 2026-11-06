import { WORST_FIXTURE_NAMES } from "./worstFixtureNames";
import type { WorstFixtureName } from "./worstFixtureNames";

export { WORST_FIXTURE_NAMES };
export type { WorstFixtureName };

export interface WorstFixture {
  displayName: string;
  bio?: string;
  email: string;
  password?: string;
  selectedFormats: string[];
  catalogFormats: string[];
  avatarUrl?: string;
  uploadFileName?: string;
  responseDelaysMs: number[];
  failures: ("http500" | "network" | "http401")[];
}

const FAMILY_EMOJI = "\u{1F468}‍\u{1F469}‍\u{1F467}";
const REGULAR_CATALOG = ["Commander", "Standard", "Modern", "Pioneer", "Legacy"];

function numberedFormats(count: number): string[] {
  return Array.from({ length: count }, (_, i) => `Formato ${String(i + 1).padStart(2, "0")}`);
}

const base: WorstFixture = {
  displayName: "Jogador Teste",
  bio: "Jogo Commander nos fins de semana.",
  email: "jogador@example.com",
  selectedFormats: ["Commander"],
  catalogFormats: REGULAR_CATALOG,
  responseDelaysMs: [],
  failures: [],
};

const fixtures: Record<WorstFixtureName, WorstFixture> = {
  vazio: { ...base, bio: "", selectedFormats: [], catalogFormats: [] },
  um: { ...base, displayName: "Zed", selectedFormats: ["Commander"] },
  enorme: {
    ...base,
    bio: "b".repeat(280),
    email: `${"e".repeat(242)}@example.com`,
    password: "p".repeat(128),
    selectedFormats: numberedFormats(50),
    catalogFormats: numberedFormats(50),
  },
  longo: {
    ...base,
    displayName: FAMILY_EMOJI.repeat(30),
    bio: "a".repeat(280),
    uploadFileName: `${"f".repeat(196)}.png`,
    catalogFormats: [...REGULAR_CATALOG, "F".repeat(60)],
  },
  curto: { ...base, displayName: "Ana" },
  faltando: {
    ...base,
    bio: undefined,
    selectedFormats: [],
    uploadFileName: undefined,
    avatarUrl: "https://avatars.invalid/nao-chega.png",
  },
  estranho: {
    ...base,
    displayName: "<b>x</b> &amp; **texto**",
    bio: "<b>x</b> &amp; **texto** <script>alert(1)</script>",
  },
  lento: { ...base, responseDelaysMs: [3000, 12000, 20000] },
  erro: { ...base, failures: ["http500", "network", "http401"] },
};

export function getWorstFixture(name: WorstFixtureName): WorstFixture {
  return structuredClone(fixtures[name]);
}
