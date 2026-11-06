// T-164 (RED): primitivo announceAfterMount do Alert/FocusedAlert (R-46, A11Y-31, REQ-042).
// A regiao role="alert" nasce vazia e o texto entra num efeito, como filho React.
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { useState } from "react";
import { renderToString } from "react-dom/server";
import { afterEach, describe, expect, it, vi } from "vitest";
import { stubControlledFrames } from "../shared/testing/controlledFrames";
import { Alert } from "./Alert";
import { FocusedAlert } from "./FocusedAlert";

const TEXT = "Sua confirmação expirou.";
const NEXT_TEXT = "Reautenticação necessária.";

function parse(html: string) {
  const container = document.createElement("div");
  container.innerHTML = html;
  return container;
}

describe("Alert com announceAfterMount (R-46, A11Y-31)", () => {
  it("primeiro render: a regiao alert existe vazia e o texto nao esta no HTML", () => {
    const container = parse(
      renderToString(
        <Alert variant="error" announceAfterMount>
          {TEXT}
        </Alert>,
      ),
    );
    const region = container.querySelector('[role="alert"]');
    expect(region).not.toBeNull();
    expect(region?.textContent?.trim()).toBe("");
    expect(container.textContent).not.toContain(TEXT);
  });

  it("depois do efeito o texto aparece na regiao alert", async () => {
    render(
      <Alert variant="error" announceAfterMount>
        {TEXT}
      </Alert>,
    );
    const region = await screen.findByRole("alert");
    await waitFor(() => expect(region).toHaveTextContent(TEXT));
  });

  it("repassa o id opcional para a regiao alert (no primeiro render e depois)", async () => {
    const html = renderToString(
      <Alert variant="error" announceAfterMount id="aviso-1">
        {TEXT}
      </Alert>,
    );
    expect(parse(html).querySelector('[role="alert"]')?.id).toBe("aviso-1");
    render(
      <Alert variant="error" announceAfterMount id="aviso-2">
        {TEXT}
      </Alert>,
    );
    const region = await screen.findByRole("alert");
    expect(region.id).toBe("aviso-2");
    await waitFor(() => expect(region).toHaveTextContent(TEXT));
  });

  it("o texto e filho React: re-render com novos children atualiza a regiao sem remontar", async () => {
    const { rerender } = render(
      <Alert variant="error" announceAfterMount>
        {TEXT}
      </Alert>,
    );
    const region = await screen.findByRole("alert");
    await waitFor(() => expect(region).toHaveTextContent(TEXT));
    rerender(
      <Alert variant="error" announceAfterMount>
        {NEXT_TEXT}
      </Alert>,
    );
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(NEXT_TEXT));
    expect(screen.getByRole("alert")).toBe(region);
    expect(screen.getByRole("alert")).not.toHaveTextContent(TEXT);
  });

  it("sem a prop, o comportamento atual nao muda: texto ja no primeiro render", () => {
    const container = parse(renderToString(<Alert variant="error">{TEXT}</Alert>));
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(TEXT);
  });
});

describe("FocusedAlert com announceAfterMount (R-46, A11Y-31)", () => {
  it("repassa a prop: regiao alert vazia no primeiro render e texto depois do efeito", async () => {
    const container = parse(
      renderToString(
        <FocusedAlert variant="error" focus={false} announceAfterMount>
          {TEXT}
        </FocusedAlert>,
      ),
    );
    expect(container.querySelector('[role="alert"]')).not.toBeNull();
    expect(container.textContent).not.toContain(TEXT);

    render(
      <FocusedAlert variant="error" focus={false} announceAfterMount>
        {TEXT}
      </FocusedAlert>,
    );
    const region = await screen.findByRole("alert");
    await waitFor(() => expect(region).toHaveTextContent(TEXT));
  });

  it("repassa o id para a regiao alert", async () => {
    render(
      <FocusedAlert variant="error" focus={false} announceAfterMount id="aviso-3">
        {TEXT}
      </FocusedAlert>,
    );
    const region = await screen.findByRole("alert");
    expect(region.id).toBe("aviso-3");
  });

  it("sem a prop, o texto vem no primeiro render e o foco segue indo ao alerta", async () => {
    const container = parse(renderToString(<FocusedAlert variant="error">{TEXT}</FocusedAlert>));
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(TEXT);

    render(<FocusedAlert variant="error">{TEXT}</FocusedAlert>);
    const region = await screen.findByRole("alert");
    await waitFor(() => expect(region).toHaveFocus());
    expect(region).toHaveTextContent(TEXT);
  });
});

// T-167 (RED): U-06 e U-04. O texto so entra depois da pintura (requestAnimationFrame) e onAnnounced avisa.
describe("Alert announceAfterMount: insercao depois da pintura (U-06, A11Y-31)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("a regiao alert fica vazia depois do commit e so recebe o texto quando o frame dispara", () => {
    const frames = stubControlledFrames();
    render(
      <Alert variant="error" announceAfterMount>
        {TEXT}
      </Alert>,
    );
    const region = screen.getByRole("alert");
    expect(region).not.toHaveTextContent(TEXT);
    expect(frames.pending()).toBe(1);
    frames.flush();
    expect(region).toHaveTextContent(TEXT);
  });

  it("desmontar antes do frame cancela o frame pelo id e nunca atualiza estado", () => {
    const frames = stubControlledFrames();
    const errors = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const { unmount } = render(
      <Alert variant="error" announceAfterMount>
        {TEXT}
      </Alert>,
    );
    expect(frames.requested).toHaveLength(1);
    unmount();
    expect(frames.cancelled).toEqual(frames.requested);
    expect(frames.pending()).toBe(0);
    frames.flush();
    expect(errors).not.toHaveBeenCalled();
  });

  it("montagem disparada por clique tambem passa pela fase vazia", async () => {
    const frames = stubControlledFrames();
    function Toggle() {
      const [shown, setShown] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setShown(true)}>
            Mostrar
          </button>
          {shown ? (
            <Alert variant="error" announceAfterMount>
              {TEXT}
            </Alert>
          ) : null}
        </>
      );
    }
    const user = userEvent.setup();
    render(<Toggle />);
    await user.click(screen.getByRole("button", { name: "Mostrar" }));
    const region = screen.getByRole("alert");
    expect(region).not.toHaveTextContent(TEXT);
    frames.flush();
    expect(region).toHaveTextContent(TEXT);
  });

  it("rerender com novos children atualiza o texto depois do anuncio", () => {
    const frames = stubControlledFrames();
    const { rerender } = render(
      <Alert variant="error" announceAfterMount>
        {TEXT}
      </Alert>,
    );
    frames.flush();
    rerender(
      <Alert variant="error" announceAfterMount>
        {NEXT_TEXT}
      </Alert>,
    );
    expect(screen.getByRole("alert")).toHaveTextContent(NEXT_TEXT);
  });

  it("sem a prop o texto esta presente na montagem e nenhum frame e pedido", () => {
    const frames = stubControlledFrames();
    render(<Alert variant="error">{TEXT}</Alert>);
    expect(screen.getByRole("alert")).toHaveTextContent(TEXT);
    expect(frames.requested).toHaveLength(0);
  });

  it("announceAfterMount de true para false antes do frame mostra o texto (R-52)", () => {
    const frames = stubControlledFrames();
    const { rerender } = render(
      <Alert variant="error" announceAfterMount>
        {TEXT}
      </Alert>,
    );
    expect(screen.getByRole("alert")).not.toHaveTextContent(TEXT);
    rerender(
      <Alert variant="error" announceAfterMount={false}>
        {TEXT}
      </Alert>,
    );
    expect(screen.getByRole("alert")).toHaveTextContent(TEXT);
    frames.flush();
    expect(screen.getByRole("alert")).toHaveTextContent(TEXT);
  });

  it("higiene: Alert.tsx nao tem eslint-disable", () => {
    const source = readFileSync(resolve(__dirname, "Alert.tsx"), "utf8");
    expect(source).not.toMatch(/eslint-disable/);
  });
});

describe("onAnnounced (U-04, A11Y-31)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("Alert: chamada uma vez, depois de o texto estar no DOM e nao antes do frame", () => {
    const frames = stubControlledFrames();
    const seen: string[] = [];
    const onAnnounced = vi.fn(() => {
      seen.push(document.querySelector('[role="alert"]')?.textContent ?? "");
    });
    render(
      <Alert variant="error" announceAfterMount onAnnounced={onAnnounced}>
        {TEXT}
      </Alert>,
    );
    expect(onAnnounced).not.toHaveBeenCalled();
    frames.flush();
    expect(onAnnounced).toHaveBeenCalledTimes(1);
    expect(seen[0]).toContain(TEXT);
  });

  it("Alert: um rerender depois do anuncio nao chama onAnnounced de novo", () => {
    const frames = stubControlledFrames();
    const onAnnounced = vi.fn();
    const { rerender } = render(
      <Alert variant="error" announceAfterMount onAnnounced={onAnnounced}>
        {TEXT}
      </Alert>,
    );
    frames.flush();
    rerender(
      <Alert variant="error" announceAfterMount onAnnounced={onAnnounced}>
        {NEXT_TEXT}
      </Alert>,
    );
    expect(onAnnounced).toHaveBeenCalledTimes(1);
  });

  it("FocusedAlert: repassa onAnnounced, chamada depois de o texto estar no DOM", () => {
    const frames = stubControlledFrames();
    const seen: string[] = [];
    const onAnnounced = vi.fn(() => {
      seen.push(document.querySelector('[role="alert"]')?.textContent ?? "");
    });
    render(
      <FocusedAlert variant="error" focus={false} announceAfterMount onAnnounced={onAnnounced}>
        {TEXT}
      </FocusedAlert>,
    );
    expect(onAnnounced).not.toHaveBeenCalled();
    frames.flush();
    expect(onAnnounced).toHaveBeenCalledTimes(1);
    expect(seen[0]).toContain(TEXT);
  });
});

// T-168 (RED): U-05. O texto do Alert quebra palavras longas a 320 px com texto a 200% (A11Y-31, REQ-042).
describe("Alert: coluna do texto quebra palavras longas (U-05)", () => {
  const variants = ["info", "success", "error"] as const;

  it.each(variants)(
    "variante %s: o div do texto tem a classe alert__text e o icone fica",
    (variant) => {
      const container = parse(renderToString(<Alert variant={variant}>{TEXT}</Alert>));
      const text = container.querySelector(".alert > .alert__text");
      expect(text).not.toBeNull();
      expect(text?.textContent).toBe(TEXT);
      expect(container.querySelector(".alert > .alert__icon")).not.toBeNull();
    },
  );

  it("com announceAfterMount: a coluna existe vazia no primeiro render e recebe o texto depois", async () => {
    const container = parse(
      renderToString(
        <Alert variant="error" announceAfterMount>
          {TEXT}
        </Alert>,
      ),
    );
    expect(container.querySelector('[role="alert"] > .alert__text')?.textContent).toBe("");

    render(
      <Alert variant="error" announceAfterMount>
        {TEXT}
      </Alert>,
    );
    const region = await screen.findByRole("alert");
    await waitFor(() =>
      expect(region.querySelector(":scope > .alert__text")).toHaveTextContent(TEXT),
    );
  });

  it("Alert.css: a regra .alert__text declara min-width: 0 e overflow-wrap: anywhere", () => {
    const css = readFileSync(resolve(__dirname, "Alert.css"), "utf8");
    const bodies = [...css.matchAll(/([^{}]+)\{([^}]*)\}/g)]
      .filter((m) => m[1].trim() === ".alert__text")
      .map((m) => m[2]);
    expect(bodies.length).toBeGreaterThan(0);
    const body = bodies.join("\n");
    expect(body).toMatch(/min-width:\s*0\s*;/);
    expect(body).toMatch(/overflow-wrap:\s*anywhere\s*;/);
  });
});
