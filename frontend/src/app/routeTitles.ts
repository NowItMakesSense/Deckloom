// T-128: tabela rota -> texto do <h1> da tela; o <title> e "<h1> - Deckloom" (A11Y-20).
// Inclui o perfil e os passos do fluxo Google (retorno, concluir, vincular), cada um com seu <h1>.
export const APP_NAME = "Deckloom";

export const ROUTE_HEADINGS: Readonly<Record<string, string>> = {
  "/cadastro": "Criar conta",
  "/entrar": "Entrar",
  "/esqueci-senha": "Redefinir senha",
  "/redefinir-senha": "Criar nova senha",
  "/perfil": "Meu perfil",
  "/perfil/editar": "Editar perfil",
  "/entrar/google/retorno": "Concluindo o login com Google",
  "/entrar/google/concluir": "Quase lá",
  "/entrar/google/vincular": "Vincular sua conta Google",
};

export function titleForPath(pathname: string): string | null {
  const heading = ROUTE_HEADINGS[pathname];
  return heading ? `${heading} - ${APP_NAME}` : null;
}
