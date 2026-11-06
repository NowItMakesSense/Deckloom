// T-105: mapa unico de mensagens pt-BR por codigo de erro. Nunca ha texto vindo do servidor
// (A11Y-05, REQ-009): codigo desconhecido ou tecnico cai na mensagem generica.

export const GENERIC_ERROR_MESSAGE = "Algo deu errado. Tente novamente.";

const SLOW_RESPONSE_NOTICE =
  "Por segurança, esta resposta está demorando um pouco mais. Aguarde, não é necessário enviar de novo.";

const EMAIL_UNAVAILABLE = "Não foi possível usar este email. Tente outro ou entre na sua conta.";
const PASSWORD_TOO_SHORT = "A senha precisa ter pelo menos 8 caracteres.";
const PASSWORD_TOO_LONG = "A senha pode ter no máximo 128 caracteres.";
const PASSWORD_TOO_COMMON = "Essa senha é muito comum. Escolha uma senha menos previsível.";

const MESSAGES_BY_CODE: Readonly<Record<string, string>> = {
  invalid_credentials: "Email ou senha inválidos.",
  incorrect_password: "Senha incorreta. Confira a senha e tente de novo.",
  too_many_attempts: "Muitas tentativas. Aguarde alguns minutos e tente de novo.",
  unauthenticated: "Sua sessão expirou. Entre novamente.",
  email_unavailable: EMAIL_UNAVAILABLE,
  reset_link_invalid: "Este link de redefinição não pode ser usado. Solicite um novo.",
  reauth_required: "Confirme sua identidade com o Google para excluir a conta.",
  reauth_expired: "Sua confirmação expirou. Reautentique com o Google para excluir a conta.",
  avatar_rejected:
    "Não foi possível usar este arquivo. Envie uma imagem JPEG, PNG ou WebP de até 2 MB.",
  pending_not_found:
    "Não foi possível concluir o login com Google. Tente de novo ou entre com email e senha.",
  google_not_completed:
    "O login com Google não foi concluído. Tente de novo ou entre com email e senha.",
  concurrency_conflict:
    "Estes dados foram alterados em outro lugar. Recarregue a página e tente de novo.",
  csrf_failed: "Sua verificação de segurança expirou. Recarregue a página e tente de novo.",
  validation_failed: "Alguns dados não são válidos. Revise os campos indicados.",
  unavailable: "O serviço está indisponível no momento. Tente novamente em instantes.",
  network_error: "Não foi possível conectar. Verifique sua conexão e tente novamente.",
  internal_error: GENERIC_ERROR_MESSAGE,
  unknown_error: GENERIC_ERROR_MESSAGE,
};

const FIELD_LABELS: Readonly<Record<string, string>> = {
  email: "o email",
  password: "a senha",
  newPassword: "a nova senha",
  displayName: "o nome de exibição",
  bio: "a bio",
  favoriteTcg: "o TCG favorito",
  formatIds: "os formatos",
  acceptTerms: "o aceite dos termos",
  adultDeclaration: "a declaração de maioridade",
  confirm: "a confirmação",
  presetId: "o avatar",
  file: "o arquivo",
};

// Textos do design (S1..S11), por "codigo:campo".
const SPECIFIC_FIELD_MESSAGES: Readonly<Record<string, string>> = {
  "required:email": "Informe seu email.",
  "invalid_format:email": "Informe um email válido, como nome@exemplo.com.",
  "unavailable:email": EMAIL_UNAVAILABLE,
  "required:password": "Informe uma senha.",
  "too_short:password": PASSWORD_TOO_SHORT,
  "too_long:password": PASSWORD_TOO_LONG,
  "too_short:newPassword": PASSWORD_TOO_SHORT,
  "too_long:newPassword": PASSWORD_TOO_LONG,
  "too_common:password": PASSWORD_TOO_COMMON,
  "too_common:newPassword": PASSWORD_TOO_COMMON,
  "length:displayName": "O nome de exibição precisa ter de 3 a 30 caracteres.",
  "invalid_characters:displayName":
    "O nome de exibição não pode ter quebras de linha nem caracteres de controle.",
  "too_long:bio": "A bio pode ter no máximo 280 caracteres.",
  "invalid_characters:bio": "A bio não pode ter caracteres de controle.",
  "invalid:favoriteTcg": "Escolha um TCG favorito válido.",
  "invalid:formatIds": "Escolha apenas formatos da lista.",
  "required:acceptTerms": "Aceite os Termos de Uso e a Política de Privacidade para criar a conta.",
  "version_mismatch:acceptTerms":
    "Os Termos de Uso ou a Política de Privacidade foram atualizados. Revise e aceite novamente.",
  "required:adultDeclaration": "Confirme que você tem 18 anos ou mais para criar a conta.",
  "required:confirm": "Marque a caixa para confirmar que entende que a exclusão é definitiva.",
};

// Combinacoes livres (nao fixadas pelo design): frase montada com o rotulo do campo.
const FIELD_TEMPLATES: Readonly<Record<string, (label: string) => string>> = {
  required: (label) => `Preencha ${label}.`,
  invalid_format: (label) => `Verifique ${label}: o formato não é válido.`,
  too_short: (label) => `Verifique ${label}: está curto demais.`,
  too_long: (label) => `Verifique ${label}: está longo demais.`,
  length: (label) => `Verifique ${label}: o tamanho não é permitido.`,
  invalid_characters: (label) => `Verifique ${label}: há caracteres não permitidos.`,
  invalid: (label) => `Verifique ${label}: o valor não é válido.`,
  unavailable: (label) => `Não foi possível usar ${label}. Tente outro.`,
  version_mismatch: (label) => `Revise ${label} e tente novamente.`,
};

function lookup(table: Readonly<Record<string, string>>, key: string): string | undefined {
  return Object.hasOwn(table, key) ? table[key] : undefined;
}

export function messageFor(code: string): string {
  return lookup(MESSAGES_BY_CODE, code) ?? GENERIC_ERROR_MESSAGE;
}

export function fieldMessage(code: string, field: string): string {
  const specific = lookup(SPECIFIC_FIELD_MESSAGES, `${code}:${field}`);
  if (specific !== undefined) return specific;

  const label = lookup(FIELD_LABELS, field);
  const template = Object.hasOwn(FIELD_TEMPLATES, code) ? FIELD_TEMPLATES[code] : undefined;
  if (label === undefined || template === undefined) return GENERIC_ERROR_MESSAGE;
  return template(label);
}

export function slowResponseNotice(): string {
  return SLOW_RESPONSE_NOTICE;
}
