// T-L02 / AC-L02f e AC-L02b: os quatro ganchos, na ordem do REQ-L01 v3, e os recursos com selo "em breve".
import { describe, expect, it } from "vitest";

import { HOOKS } from "./hooks";

describe("HOOKS (REQ-L01 v3, ordem fixa)", () => {
  it("sao quatro, na ordem dos ganchos do palco", () => {
    expect(HOOKS.map((hook) => hook.id)).toEqual(["quanto-do-deck", "meta", "lojas", "ia"]);
  });

  it("os titulos seguem a ordem: quanto do deck, meta x colecao, so o que falta, IA", () => {
    expect(HOOKS.map((hook) => hook.title)).toEqual([
      "Quanto do deck você já tem",
      "O meta dos torneios contra a sua coleção",
      "Compre só o que falta",
      "IA que explica o porquê de cada carta",
    ]);
  });

  it("cada gancho tem texto e ao menos um recurso (todos ainda indisponiveis)", () => {
    for (const hook of HOOKS) {
      expect(hook.text.length, hook.id).toBeGreaterThan(20);
      expect(hook.features.length, hook.id).toBeGreaterThan(0);
    }
  });

  it("os recursos cobrem colecao, decks, sugestoes com IA, meta e lojas (REQ-L02)", () => {
    expect(HOOKS.flatMap((hook) => hook.features).sort()).toEqual(
      ["Coleção", "Decks", "Lojas", "Meta", "Sugestões com IA"].sort(),
    );
  });

  it("nada de preco do produto, 'gratis' nem prova inventada nos textos", () => {
    const text = HOOKS.map((hook) => `${hook.title} ${hook.text}`).join(" ");

    expect(text).not.toMatch(/gr[aá]t|R\$|por m[eê]s|milh|depoimento/i);
  });
});
