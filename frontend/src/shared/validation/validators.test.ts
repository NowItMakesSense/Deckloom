import { describe, expect, it } from "vitest";

import { graphemeLength } from "./graphemeLength";
import { validateBio, validateDisplayName, validateEmail, validatePassword } from "./validators";

// T-104 (REQ-003, REQ-015, REQ-038, REQ-039, REQ-040).
// ATENCAO: estes validadores sao apenas conveniencia de UI (feedback imediato).
// O servidor e a autoridade: toda regra aqui e revalidada no backend e nunca
// substitui aquela validacao. As fronteiras espelham os dominios do backend.

const FAMILY = "\u{1F468}‍\u{1F469}‍\u{1F467}"; // emoji composto = 1 grapheme
const EMOJI = "\u{1F600}"; // 2 UTF-16 units, 1 code point

describe("graphemeLength (cliente e conveniencia de UI)", () => {
  it("counts a ZWJ emoji sequence as one grapheme", () => {
    expect(graphemeLength(FAMILY)).toBe(1);
  });

  it("counts two letters plus a composite emoji as 3", () => {
    expect(graphemeLength(`ab${FAMILY}`)).toBe(3);
  });

  it("counts a combining sequence as one grapheme", () => {
    expect(graphemeLength("é")).toBe(1);
  });

  it("returns 0 for empty text", () => {
    expect(graphemeLength("")).toBe(0);
  });
});

describe("validateEmail (REQ-003, cliente e conveniencia de UI)", () => {
  it("accepts a well formed email", () => {
    expect(validateEmail("jogador@deckloom.com")).toBeNull();
  });

  it("is case insensitive and ignores surrounding spaces", () => {
    expect(validateEmail("  Jogador@Deckloom.COM ")).toBeNull();
  });

  it.each([null, undefined, "", "   "])("requires a value for %j", (value) => {
    expect(validateEmail(value)).toBe("required");
  });

  it.each(["semarroba", "a@b", "@dominio.com", "a@@b.com", "a b@c.com", "a@b..com", "a@b."])(
    "rejects invalid format %j",
    (value) => {
      expect(validateEmail(value)).toBe("invalid_format");
    },
  );

  it("rejects an email longer than 254 characters", () => {
    const local = "a".repeat(254 - "@b.co".length + 1);
    expect(validateEmail(`${local}@b.co`)).toBe("invalid_format");
  });
});

describe("validatePassword (REQ-003, code points, cliente e conveniencia de UI)", () => {
  it.each([null, undefined, ""])("requires a value for %j", (value) => {
    expect(validatePassword(value)).toBe("required");
  });

  it("rejects 7 code points as too_short", () => {
    expect(validatePassword("a".repeat(7))).toBe("too_short");
  });

  it("accepts 8 code points", () => {
    expect(validatePassword("a".repeat(8))).toBeNull();
  });

  it("accepts 128 code points", () => {
    expect(validatePassword("a".repeat(128))).toBeNull();
  });

  it("rejects 129 code points as too_long", () => {
    expect(validatePassword("a".repeat(129))).toBe("too_long");
  });

  it("counts astral characters as one code point each (8 emojis are valid)", () => {
    expect(validatePassword(EMOJI.repeat(8))).toBeNull();
    expect(validatePassword(EMOJI.repeat(7))).toBe("too_short");
  });

  it("counts 128 astral emojis as valid and 129 as too_long", () => {
    expect(validatePassword(EMOJI.repeat(128))).toBeNull();
    expect(validatePassword(EMOJI.repeat(129))).toBe("too_long");
  });
});

describe("validateDisplayName (REQ-015, graphemes, cliente e conveniencia de UI)", () => {
  it("rejects missing input as length", () => {
    expect(validateDisplayName(null)).toBe("length");
    expect(validateDisplayName(undefined)).toBe("length");
  });

  it('counts "  Bi  " as 2 graphemes after trim (length error)', () => {
    expect(validateDisplayName("  Bi  ")).toBe("length");
  });

  it("rejects 2 and accepts 3 graphemes", () => {
    expect(validateDisplayName("ab")).toBe("length");
    expect(validateDisplayName("abc")).toBeNull();
  });

  it("accepts 30 and rejects 31 graphemes", () => {
    expect(validateDisplayName("a".repeat(30))).toBeNull();
    expect(validateDisplayName("a".repeat(31))).toBe("length");
  });

  it("counts two letters plus a composite emoji as 3 (valid)", () => {
    expect(validateDisplayName(`ab${FAMILY}`)).toBeNull();
  });

  it("counts 30 composite emojis as valid and 31 as length error", () => {
    expect(validateDisplayName(FAMILY.repeat(30))).toBeNull();
    expect(validateDisplayName(FAMILY.repeat(31))).toBe("length");
  });

  it("trims only the ends, keeping inner spaces", () => {
    expect(validateDisplayName("  a b  ")).toBeNull();
  });

  it.each(["abc\n", "a\nbc", "ab\tc", "ab\u0000c", "ab\rc"])(
    "rejects control characters in %j",
    (value) => {
      expect(validateDisplayName(value)).toBe("invalid_characters");
    },
  );
});

describe("validateBio (REQ-038/039/040, graphemes, cliente e conveniencia de UI)", () => {
  it("accepts null, undefined and blank text as an empty bio", () => {
    expect(validateBio(null)).toBeNull();
    expect(validateBio(undefined)).toBeNull();
    expect(validateBio("   \n  ")).toBeNull();
  });

  it("accepts 280 and rejects 281 graphemes", () => {
    expect(validateBio("a".repeat(280))).toBeNull();
    expect(validateBio("a".repeat(281))).toBe("too_long");
  });

  it("counts composite emojis as one grapheme each", () => {
    expect(validateBio(FAMILY.repeat(280))).toBeNull();
    expect(validateBio(FAMILY.repeat(281))).toBe("too_long");
  });

  it("allows line breaks (LF and CRLF) in the bio", () => {
    expect(validateBio("linha 1\nlinha 2")).toBeNull();
    expect(validateBio("linha 1\r\nlinha 2")).toBeNull();
  });

  it("counts CRLF as a single line break against the limit", () => {
    expect(validateBio(`${"a".repeat(139)}\r\n${"b".repeat(140)}`)).toBeNull();
  });

  it.each(["ab\tc", "ab\u0000c", "ab\u0007c", "ab\rc"])(
    "rejects control characters other than line feed in %j",
    (value) => {
      expect(validateBio(value)).toBe("invalid_characters");
    },
  );
});
