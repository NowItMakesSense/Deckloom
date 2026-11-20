// T-L01C / REQ-L03 v5, REQ-L09: o painel da tela entra com anime.js a cada troca de rota; movimento reduzido nao anima.
import { render } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useNavigate } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const motion = vi.hoisted(() => ({ riseOnEnter: vi.fn() }));
vi.mock("@/design-system/motion", () => ({ riseOnEnter: motion.riseOnEnter }));

import { PanelEntrance } from "./PanelEntrance";

function Go() {
  const navigate = useNavigate();
  return (
    <button type="button" onClick={() => navigate("/b")}>
      ir
    </button>
  );
}

function setup() {
  return render(
    <MemoryRouter initialEntries={["/a"]}>
      <PanelEntrance />
      <Go />
      <Routes>
        <Route path="*" element={<main id="conteudo" />} />
      </Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => motion.riseOnEnter.mockClear());

describe("PanelEntrance", () => {
  it("anima o painel (main#conteudo) ao montar e a cada rota nova", () => {
    const { getByText } = setup();
    expect(motion.riseOnEnter).toHaveBeenCalledTimes(1);
    expect(motion.riseOnEnter.mock.calls[0][0]).toBe(document.getElementById("conteudo"));

    getByText("ir").click();
    return vi.waitFor(() => expect(motion.riseOnEnter).toHaveBeenCalledTimes(2));
  });
});
