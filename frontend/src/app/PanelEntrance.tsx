import { useEffect } from "react";
import { useLocation } from "react-router-dom";

import { riseOnEnter } from "@/design-system/motion";

/** A cada rota nova, o painel da tela (main#conteudo) entra com anime.js; com movimento reduzido nao anima. */
export function PanelEntrance(): null {
  const { pathname } = useLocation();
  useEffect(() => {
    const panel = document.getElementById("conteudo");
    if (panel) riseOnEnter(panel);
  }, [pathname]);
  return null;
}
