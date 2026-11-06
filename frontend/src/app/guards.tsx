import type { ReactNode } from "react";
import { Navigate, Outlet, useLocation } from "react-router-dom";
import { DEFAULT_RETURN_TO } from "./routes";
import { useSession } from "./SessionProvider";

// Durante "loading" a spec nao define UI: nada e renderizado.
export function PlayerRoute(): ReactNode {
  const session = useSession();
  const location = useLocation();
  if (session.status === "loading") return null;
  if (session.status === "guest") {
    const retorno = encodeURIComponent(location.pathname);
    return <Navigate to={`/entrar?aviso=sessao-expirada&retorno=${retorno}`} replace />;
  }
  return <Outlet />;
}

export function GuestRoute(): ReactNode {
  const session = useSession();
  if (session.status === "loading") return null;
  if (session.status === "player") return <Navigate to={DEFAULT_RETURN_TO} replace />;
  return <Outlet />;
}
