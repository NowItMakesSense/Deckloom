// T-170 (RED): <title> proprio em /perfil, /perfil/editar e no fluxo Google (U-07; A11Y-20, A11Y-34).
// O texto esperado e o <h1> que cada tela renderiza hoje.
import { describe, expect, it } from "vitest";
import { ROUTE_PATTERNS } from "./AppRoutes";
import { GOOGLE_COMPLETE_ROUTE, GOOGLE_LINK_ROUTE, GOOGLE_RETURN_ROUTE } from "./routes";
import { titleForPath } from "./routeTitles";

const EXPECTED_TITLES: ReadonlyArray<readonly [string, string]> = [
  ["/perfil", "Meu perfil - Deckloom"],
  ["/perfil/editar", "Editar perfil - Deckloom"],
  [GOOGLE_RETURN_ROUTE, "Concluindo o login com Google - Deckloom"],
  [GOOGLE_COMPLETE_ROUTE, "Quase lá - Deckloom"],
  [GOOGLE_LINK_ROUTE, "Vincular sua conta Google - Deckloom"],
];

describe("titleForPath (A11Y-20, U-07)", () => {
  it.each(EXPECTED_TITLES)("%s -> %s", (path, title) => {
    expect(titleForPath(path)).toBe(title);
  });

  it.each(ROUTE_PATTERNS)("toda rota de ROUTE_PATTERNS tem titulo: %s", (path) => {
    expect(titleForPath(path)).not.toBeNull();
  });
});
