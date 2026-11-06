import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";

import { AppRoutes } from "@/app/AppRoutes";
import { SessionProvider } from "@/app/SessionProvider";
import "@/design-system/tokens.css";

const container = document.getElementById("root");
if (!container) {
  throw new Error("Elemento #root nao encontrado em index.html");
}

createRoot(container).render(
  <StrictMode>
    {/* Sem transicoes: a sessao nova e a navegacao com state (ex.: boas-vindas) renderizam juntas;
        com startTransition o guard redirecionava antes e o state se perdia (T-122). */}
    <BrowserRouter useTransitions={false}>
      <SessionProvider baseUrl="">
        <AppRoutes />
      </SessionProvider>
    </BrowserRouter>
  </StrictMode>,
);
