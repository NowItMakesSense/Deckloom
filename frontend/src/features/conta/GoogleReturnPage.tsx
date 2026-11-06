import { useEffect, useRef } from "react";
import { useHeadingFocusOnTransition } from "@/shared/hooks/useHeadingFocusOnTransition";
import type { ReactNode } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { safeReturnTo } from "@/app/routes";
import { useRefreshSession } from "@/app/SessionProvider";
import { Spinner } from "@/design-system/Spinner";
import "./pageShell.css";

const GOOGLE_FAILED = "/entrar?aviso=google-falhou";

// S3: so le `resultado` (e `retorno` interno) e redireciona; nada do que vem
// na URL alem disso e usado ou exibido (REQ-006).
const FIXED_DESTINATIONS: Record<string, string> = {
  concluir: "/entrar/google/concluir",
  vincular: "/entrar/google/vincular",
  reautenticado: "/perfil?exclusao=confirmar",
  "reautenticacao-falhou": "/perfil?exclusao=falhou",
};

export function GoogleReturnPage(): ReactNode {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const refreshSession = useRefreshSession();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const resultado = searchParams.get("resultado");
  const retorno = searchParams.get("retorno");

  useHeadingFocusOnTransition(headingRef);

  useEffect(() => {
    let active = true;
    if (resultado === "ok") {
      void refreshSession().then(() => {
        if (active) navigate(safeReturnTo(retorno), { replace: true });
      });
    } else {
      navigate(FIXED_DESTINATIONS[resultado ?? ""] ?? GOOGLE_FAILED, { replace: true });
    }
    return () => {
      active = false;
    };
  }, [resultado, retorno, navigate, refreshSession]);

  return (
    <main id="conteudo" tabIndex={-1} className="google-return-page">
      <h1 ref={headingRef} tabIndex={-1}>
        Concluindo o login com Google
      </h1>
      <div role="status">
        <Spinner label="Aguarde um instante..." />
      </div>
    </main>
  );
}
