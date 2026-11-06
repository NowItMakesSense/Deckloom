import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterEach } from "vitest";

// O padrao de 1000ms do waitFor/findBy e curto para a suite completa em paralelo (26 ambientes
// jsdom disputando a CPU): a resposta do MSW chega depois do prazo e a assercao falha antes da
// navegacao real. O prazo maior so faz esperar o resultado real; fica abaixo do testTimeout (5s).
// 4000 ms e uma tolerancia documentada para carga de CPU em execucao paralela (R-21, QA-04), aceita
// sem causa raiz fechada; nao e para mascarar lentidao do codigo de producao.
configure({ asyncUtilTimeout: 4000 });

afterEach(() => {
  cleanup();
});
