import { safeReturnTo } from "@/app/routes";

const SIGNIN_START_PATH = "/api/v1/auth/google/start?intent=signin";

// Inicia o fluxo OIDC: navegacao completa do navegador para a API, que redireciona ao Google.
// R-34: o retorno (so o caminho interno conhecido; o resto vira /perfil) segue como returnTo e volta no `retorno` de S3.
export function startGoogleSignin(retorno?: string | null) {
  const returnTo = retorno ? `&returnTo=${encodeURIComponent(safeReturnTo(retorno))}` : "";
  window.location.assign(`${SIGNIN_START_PATH}${returnTo}`);
}
