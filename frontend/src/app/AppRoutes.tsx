import { useEffect, type ReactNode } from "react";
import { Route, Routes, useLocation } from "react-router-dom";
import { CadastroPage } from "@/features/conta/CadastroPage";
import { EditProfilePage } from "@/features/conta/EditProfilePage";
import { ForgotPasswordPage } from "@/features/conta/ForgotPasswordPage";
import { GoogleLinkPage } from "@/features/conta/GoogleLinkPage";
import { GoogleReturnPage } from "@/features/conta/GoogleReturnPage";
import { GoogleSignupPage } from "@/features/conta/GoogleSignupPage";
import { LoginPage } from "@/features/conta/LoginPage";
import { ProfilePage } from "@/features/conta/ProfilePage";
import { ResetPasswordPage } from "@/features/conta/ResetPasswordPage";
import { SkipLink } from "@/design-system/SkipLink";
import { GuestRoute, PlayerRoute } from "./guards";
import {
  GOOGLE_COMPLETE_ROUTE,
  GOOGLE_LINK_ROUTE,
  GOOGLE_RETURN_ROUTE,
  GOOGLE_ROUTES,
  GUEST_ROUTES,
  PLAYER_ROUTES,
} from "./routes";
import { titleForPath } from "./routeTitles";
import { SessionHeader } from "./SessionHeader";

export const ROUTE_PATTERNS: readonly string[] = [
  ...GUEST_ROUTES,
  ...PLAYER_ROUTES,
  ...GOOGLE_ROUTES,
];

function playerElement(path: string): ReactNode {
  if (path === "/perfil") return <ProfilePage />;
  if (path === "/perfil/editar") return <EditProfilePage />;
  return null;
}

function guestElement(path: string): ReactNode {
  if (path === "/cadastro") return <CadastroPage />;
  if (path === "/entrar") return <LoginPage />;
  if (path === "/esqueci-senha") return <ForgotPasswordPage />;
  if (path === "/redefinir-senha") return <ResetPasswordPage />;
  return null;
}

// Atualiza o <title> a cada transicao de rota (T-128, A11Y-20).
function RouteTitle(): null {
  const { pathname } = useLocation();
  useEffect(() => {
    const title = titleForPath(pathname);
    if (title) document.title = title;
  }, [pathname]);
  return null;
}

// As telas entram nas tarefas T-107 a T-120; aqui so a estrutura e os guards.
export function AppRoutes(): ReactNode {
  return (
    <>
      <SkipLink />
      <RouteTitle />
      <SessionHeader />
      <RoutesTree />
    </>
  );
}

function RoutesTree(): ReactNode {
  return (
    <Routes>
      <Route element={<GuestRoute />}>
        {GUEST_ROUTES.map((path) => (
          <Route key={path} path={path} element={guestElement(path)} />
        ))}
      </Route>
      <Route path={GOOGLE_RETURN_ROUTE} element={<GoogleReturnPage />} />
      <Route path={GOOGLE_COMPLETE_ROUTE} element={<GoogleSignupPage />} />
      <Route path={GOOGLE_LINK_ROUTE} element={<GoogleLinkPage />} />
      <Route element={<PlayerRoute />}>
        {PLAYER_ROUTES.map((path) => (
          <Route key={path} path={path} element={playerElement(path)} />
        ))}
      </Route>
    </Routes>
  );
}
