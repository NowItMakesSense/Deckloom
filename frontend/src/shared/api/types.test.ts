// T-154 (RED, R-28): tipos compartilhados Avatar/SessionSummary/Profile no barrel de shared/api.
import { readFileSync } from "node:fs";
import { describe, expect, expectTypeOf, it } from "vitest";
import type { Avatar, Profile, SessionSummary } from "@/shared/api";

type PresetAvatar = { kind: "preset"; presetId: string; imageUrl: string; alt: string };
type CustomAvatar = { kind: "custom"; imageUrl: string; alt: string };

describe("shared/api - tipos compartilhados (design.md, Tipos compartilhados)", () => {
  it("Avatar e a uniao PresetAvatar | CustomAvatar", () => {
    expectTypeOf<Avatar>().toEqualTypeOf<PresetAvatar | CustomAvatar>();
  });

  it("SessionSummary.avatar e Avatar (nao unknown)", () => {
    expectTypeOf<SessionSummary["avatar"]>().toEqualTypeOf<Avatar>();
    expectTypeOf<SessionSummary["displayName"]>().toBeString();
    expectTypeOf<SessionSummary["hasPassword"]>().toBeBoolean();
    expectTypeOf<SessionSummary["hasGoogle"]>().toBeBoolean();
  });

  it("Profile.avatar e Avatar e nao tem email", () => {
    expectTypeOf<Profile["avatar"]>().toEqualTypeOf<Avatar>();
    expectTypeOf<"email" extends keyof Profile ? true : false>().toEqualTypeOf<false>();
    expectTypeOf<"email" extends keyof SessionSummary ? true : false>().toEqualTypeOf<false>();
  });

  it("SessionHeader usa o avatar tipado, sem cast manual", () => {
    const source = readFileSync("src/app/SessionHeader.tsx", "utf8");
    expect(source).not.toMatch(/\sas\s+\w+/);
    expect(source).not.toMatch(/raw:\s*unknown/);
  });
});
