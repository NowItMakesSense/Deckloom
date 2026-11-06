import { describe, expect, it } from "vitest";

import { fieldMessage, GENERIC_ERROR_MESSAGE, messageFor, slowResponseNotice } from "./messages";

// T-105 (habilitadora; A11Y-05, REQ-005, REQ-031, REQ-009, REQ-002, REQ-020, REQ-042).
// Os textos vem do design (tabelas de estados de S1..S11). Nunca ha texto vindo do servidor.

const TOP_LEVEL_CODES = [
  "invalid_credentials",
  "incorrect_password",
  "unauthenticated",
  "email_unavailable",
  "reset_link_invalid",
  "reauth_required",
  "reauth_expired",
  "avatar_rejected",
  "pending_not_found",
  "google_not_completed",
  "concurrency_conflict",
  "csrf_failed",
  "validation_failed",
  "unavailable",
  "network_error",
  "internal_error",
  "unknown_error",
] as const;

const FIELD_CODES = [
  "required",
  "invalid_format",
  "too_short",
  "too_long",
  "length",
  "invalid_characters",
  "invalid",
  "unavailable",
  "version_mismatch",
] as const;

const FIELDS = [
  "email",
  "password",
  "newPassword",
  "displayName",
  "bio",
  "favoriteTcg",
  "formatIds",
  "acceptTerms",
  "adultDeclaration",
  "confirm",
  "presetId",
  "file",
] as const;

const ENGLISH = /\b(the|error|failed|something|went wrong|try again|please)\b/i;
const TECHNICAL =
  /(exception|stack|trace|sql|postgres|npgsql|entity ?framework|ef core|dotnet|\.net|axios|fetch|react|vitest|json|http|\b[45]\d\d\b|null|undefined|Error:)/i;
const RAW_CODE = /[a-z]+_[a-z_]+/;

const allMessages = (): string[] => [
  ...TOP_LEVEL_CODES.map((c) => messageFor(c)),
  ...FIELD_CODES.flatMap((c) => FIELDS.map((f) => fieldMessage(c, f))),
  GENERIC_ERROR_MESSAGE,
  slowResponseNotice(),
];

describe("messageFor (T-105, mensagem por codigo de erro)", () => {
  it.each(TOP_LEVEL_CODES)("returns a non-empty pt-BR message for %s", (code) => {
    const message = messageFor(code);
    expect(message.trim().length).toBeGreaterThan(0);
    expect(message).not.toContain(code);
  });

  it.each([
    ["invalid_credentials", "Email ou senha inválidos."],
    ["email_unavailable", "Não foi possível usar este email. Tente outro ou entre na sua conta."],
    ["reset_link_invalid", "Este link de redefinição não pode ser usado. Solicite um novo."],
    ["reauth_expired", "Sua confirmação expirou. Reautentique com o Google para excluir a conta."],
    [
      "avatar_rejected",
      "Não foi possível usar este arquivo. Envie uma imagem JPEG, PNG ou WebP de até 2 MB.",
    ],
    ["unauthenticated", "Sua sessão expirou. Entre novamente."],
    [
      "pending_not_found",
      "Não foi possível concluir o login com Google. Tente de novo ou entre com email e senha.",
    ],
  ])("uses the exact design text for %s", (code, text) => {
    expect(messageFor(code)).toBe(text);
  });

  it("keeps incorrect_password saying the password is wrong", () => {
    expect(messageFor("incorrect_password")).toMatch(/^Senha incorreta\./);
  });

  it("gives unknown codes the generic message without leaking the raw code", () => {
    const message = messageFor("totally_made_up_code_xyz");
    expect(message).toBe(GENERIC_ERROR_MESSAGE);
    expect(message).not.toContain("totally_made_up_code_xyz");
  });

  it.each(["", "   ", "Internal Server Error", "SELECT * FROM player"])(
    "falls back to the generic message for %j",
    (code) => {
      expect(messageFor(code)).toBe(GENERIC_ERROR_MESSAGE);
    },
  );

  it("ends every message as a full sentence (A11Y-05)", () => {
    for (const code of TOP_LEVEL_CODES) {
      expect(messageFor(code)).toMatch(/[.!]$/);
    }
    expect(GENERIC_ERROR_MESSAGE).toMatch(/Tente/);
  });

  it("exposes the slow response notice without 'muitas tentativas'", () => {
    const notice = slowResponseNotice();
    expect(notice).toBe(
      "Por segurança, esta resposta está demorando um pouco mais. Aguarde, não é necessário enviar de novo.",
    );
    expect(notice.toLowerCase()).not.toContain("muitas tentativas");
  });
});

describe("fieldMessage (T-105, mensagem por codigo de campo)", () => {
  it.each(FIELD_CODES.flatMap((c) => FIELDS.map((f) => [c, f] as const)))(
    "returns a non-empty message for %s on %s",
    (code, field) => {
      const message = fieldMessage(code, field);
      expect(message.trim().length).toBeGreaterThan(0);
      expect(message).not.toContain(code);
    },
  );

  it.each([
    ["required", "email", "Informe seu email."],
    ["invalid_format", "email", "Informe um email válido, como nome@exemplo.com."],
    ["required", "password", "Informe uma senha."],
    ["too_short", "password", "A senha precisa ter pelo menos 8 caracteres."],
    ["too_long", "password", "A senha pode ter no máximo 128 caracteres."],
    ["too_short", "newPassword", "A senha precisa ter pelo menos 8 caracteres."],
    ["too_long", "newPassword", "A senha pode ter no máximo 128 caracteres."],
    ["length", "displayName", "O nome de exibição precisa ter de 3 a 30 caracteres."],
    [
      "invalid_characters",
      "displayName",
      "O nome de exibição não pode ter quebras de linha nem caracteres de controle.",
    ],
    ["too_long", "bio", "A bio pode ter no máximo 280 caracteres."],
    ["invalid_characters", "bio", "A bio não pode ter caracteres de controle."],
    ["invalid", "favoriteTcg", "Escolha um TCG favorito válido."],
    ["invalid", "formatIds", "Escolha apenas formatos da lista."],
    [
      "required",
      "acceptTerms",
      "Aceite os Termos de Uso e a Política de Privacidade para criar a conta.",
    ],
    [
      "version_mismatch",
      "acceptTerms",
      "Os Termos de Uso ou a Política de Privacidade foram atualizados. Revise e aceite novamente.",
    ],
    ["required", "adultDeclaration", "Confirme que você tem 18 anos ou mais para criar a conta."],
    [
      "required",
      "confirm",
      "Marque a caixa para confirmar que entende que a exclusão é definitiva.",
    ],
    [
      "unavailable",
      "email",
      "Não foi possível usar este email. Tente outro ou entre na sua conta.",
    ],
  ])("%s on %s -> exact design text", (code, field, text) => {
    expect(fieldMessage(code, field)).toBe(text);
  });

  it("states the numeric limits where the spec requires them", () => {
    expect(fieldMessage("too_short", "password")).toContain("8");
    expect(fieldMessage("too_long", "password")).toContain("128");
    expect(fieldMessage("length", "displayName")).toMatch(/3 a 30/);
    expect(fieldMessage("too_long", "bio")).toContain("280");
  });

  it("does not leak unknown field codes or field names", () => {
    const message = fieldMessage("weird_code_abc", "secretField");
    expect(message).toBe(GENERIC_ERROR_MESSAGE);
    expect(message).not.toContain("weird_code_abc");
    expect(message).not.toContain("secretField");
  });
});

describe("hygiene of every message (A11Y-05, REQ-009)", () => {
  it("never contains technical details, library names or english", () => {
    for (const message of allMessages()) {
      expect(message).not.toMatch(TECHNICAL);
      expect(message).not.toMatch(ENGLISH);
      expect(message).not.toMatch(RAW_CODE);
    }
  });

  it("keeps correct accents on spec keywords", () => {
    expect(messageFor("invalid_credentials")).toContain("inválidos");
    expect(messageFor("reset_link_invalid")).toContain("redefinição");
    expect(messageFor("reauth_expired")).toContain("confirmação");
    expect(messageFor("unauthenticated")).toContain("sessão");
    expect(fieldMessage("length", "displayName")).toContain("exibição");
    expect(fieldMessage("too_long", "password")).toContain("máximo");
    expect(slowResponseNotice()).toContain("segurança");
    const joined = allMessages().join(" ");
    for (const unaccented of [
      "invalidos",
      "redefinicao",
      "confirmacao",
      "sessao",
      "exibicao",
      "maximo",
    ]) {
      expect(joined).not.toContain(unaccented);
    }
  });
});
