import { WORST_FIXTURE_NAMES } from "./worstFixtureNames";
import type { WorstFixtureName } from "./worstFixtureNames";
import type { WorstFixture } from "./worstFixtures";

export function resolveWorstFixtureName(
  search: string,
  isDevelopment: boolean,
): WorstFixtureName | null {
  if (!isDevelopment) return null;
  const params = new URLSearchParams(search);
  if (params.get("data") !== "worst") return null;
  const name = params.get("fixture");
  return WORST_FIXTURE_NAMES.find((n) => n === name) ?? null;
}

export async function loadWorstFixture(
  search: string,
  isDevelopment: boolean,
): Promise<WorstFixture | null> {
  const name = resolveWorstFixtureName(search, isDevelopment);
  if (name === null) return null;
  const { getWorstFixture } = await import("./worstFixtures");
  return getWorstFixture(name);
}
