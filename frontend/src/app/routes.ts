// T-106: lista de rotas conhecidas, compartilhada por router, guards e testes.
// Nenhuma rota carrega identificador de outro Jogador (REQ-013).
// Landing publica (REQ-L01): fora dos guards; traz a propria navegacao.
export const LANDING_ROUTE = "/";
export const GUEST_ROUTES: readonly string[] = [
  "/cadastro",
  "/entrar",
  "/esqueci-senha",
  "/redefinir-senha",
];
export const HOME_ROUTE = "/inicio";
export const PLAYER_ROUTES: readonly string[] = [HOME_ROUTE, "/perfil", "/perfil/editar"];
// Fluxo do Google (S3 a S5): fora dos guards, pois a reautenticacao ocorre logado.
export const GOOGLE_RETURN_ROUTE = "/entrar/google/retorno";
export const GOOGLE_COMPLETE_ROUTE = "/entrar/google/concluir";
export const GOOGLE_LINK_ROUTE = "/entrar/google/vincular";
export const GOOGLE_ROUTES: readonly string[] = [
  GOOGLE_RETURN_ROUTE,
  GOOGLE_COMPLETE_ROUTE,
  GOOGLE_LINK_ROUTE,
];
// As rotas do fluxo Google ficam fora de KNOWN_ROUTES: nao sao retorno valido (evita laco).
export const KNOWN_ROUTES: readonly string[] = [...GUEST_ROUTES, ...PLAYER_ROUTES];
export const DEFAULT_RETURN_TO = HOME_ROUTE;

// Aceita so caminho interno conhecido (UX-12, A10); query e hash sao descartados.
export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.includes("\\")) {
    return DEFAULT_RETURN_TO;
  }
  const basePath = value.split(/[?#]/, 1)[0];
  return KNOWN_ROUTES.includes(basePath) ? basePath : DEFAULT_RETURN_TO;
}
