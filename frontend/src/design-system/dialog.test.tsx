import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useRef, useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { expectNoA11yViolations } from "../shared/testing/expectNoA11yViolations";
import { Dialog } from "./Dialog";

// T-099 (habilitadora): A11Y-06, A11Y-08, A11Y-11, A11Y-12; S10 e S11; MOT-12, MOT-13,
// A11Y-37, A11Y-38, WC-14, ART-01.
// Lacunas: scroll lock do body nao esta fixado na spec (e jsdom nao tem layout/rolagem):
// nao testado aqui, ver relatorio. Clique no scrim em dialog comum: spec omissa, so o
// alertdialog tem contrato (nao confirma, nao fecha).

const styles = import.meta.glob("./*.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

interface HarnessProps {
  role?: "dialog" | "alertdialog";
  busy?: boolean;
  onClose?: () => void;
  withInitialRef?: boolean;
}

function Harness({
  role = "dialog",
  busy = false,
  onClose = () => {},
  withInitialRef = false,
}: HarnessProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLInputElement>(null);
  return (
    <div>
      <main data-testid="app-root">
        <button type="button" onClick={() => setOpen(true)}>
          Excluir conta
        </button>
      </main>
      <Dialog
        open={open}
        onClose={() => {
          onClose();
          setOpen(false);
        }}
        title="Excluir conta"
        description="Esta ação é imediata e não pode ser desfeita."
        role={role}
        busy={busy}
        initialFocusRef={withInitialRef ? ref : undefined}
      >
        <button type="button" data-variant="danger">
          Excluir minha conta definitivamente
        </button>
        <input ref={ref} aria-label="Senha" />
        <button type="button">Cancelar</button>
      </Dialog>
    </div>
  );
}

async function openDialog(role?: "dialog" | "alertdialog", props: Partial<HarnessProps> = {}) {
  const user = userEvent.setup();
  const utils = render(<Harness role={role} {...props} />);
  const trigger = screen.getByRole("button", { name: "Excluir conta" });
  await user.click(trigger);
  return { user, trigger, ...utils };
}

describe("Dialog semantics", () => {
  it("renders role=dialog with aria-modal, label and description", async () => {
    await openDialog();

    const dialog = screen.getByRole("dialog", { name: "Excluir conta" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    const labelledby = dialog.getAttribute("aria-labelledby");
    expect(labelledby).toBeTruthy();
    expect(document.getElementById(labelledby as string)).toHaveTextContent("Excluir conta");
    const describedby = dialog.getAttribute("aria-describedby");
    expect(describedby).toBeTruthy();
    expect(document.getElementById(describedby as string)).toHaveTextContent(
      "Esta ação é imediata e não pode ser desfeita.",
    );
  });

  it("renders role=alertdialog for destructive confirmation", async () => {
    await openDialog("alertdialog");

    const dialog = screen.getByRole("alertdialog", { name: "Excluir conta" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
  });

  it("renders nothing while closed", () => {
    render(<Harness />);

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("makes the page behind inert while open (A11Y-11)", async () => {
    await openDialog();

    expect(screen.getByTestId("app-root")).toHaveAttribute("inert");
  });

  it("has zero axe violations", async () => {
    await openDialog();

    expect(screen.getByRole("dialog", { name: "Excluir conta" })).toBeInTheDocument();
    await expectNoA11yViolations(document.body);
  });
});

describe("Dialog focus management", () => {
  it("moves focus into the dialog on open", async () => {
    await openDialog();

    const dialog = screen.getByRole("dialog");
    expect(dialog).toContainElement(document.activeElement as HTMLElement);
  });

  it("never gives initial focus to the danger button (S11)", async () => {
    await openDialog("alertdialog");

    const danger = screen.getByRole("button", { name: "Excluir minha conta definitivamente" });
    expect(danger).not.toHaveFocus();
    expect(screen.getByRole("alertdialog")).toContainElement(document.activeElement as HTMLElement);
  });

  it("focuses the element given by initialFocusRef", async () => {
    await openDialog("dialog", { withInitialRef: true });

    expect(screen.getByLabelText("Senha")).toHaveFocus();
  });

  it("returns focus to the trigger when closed by Escape", async () => {
    const { user, trigger } = await openDialog();
    expect(screen.getByRole("dialog")).toContainElement(document.activeElement as HTMLElement);

    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("returns focus to the trigger when closed by a button", async () => {
    const { user, trigger } = await openDialog();

    await user.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("traps Tab forward inside the dialog", async () => {
    const { user } = await openDialog();
    const dialog = screen.getByRole("dialog");

    for (let i = 0; i < 8; i += 1) {
      await user.tab();
      expect(dialog).toContainElement(document.activeElement as HTMLElement);
    }
  });

  it("traps Shift+Tab backward inside the dialog", async () => {
    const { user } = await openDialog();
    const dialog = screen.getByRole("dialog");

    for (let i = 0; i < 8; i += 1) {
      await user.tab({ shift: true });
      expect(dialog).toContainElement(document.activeElement as HTMLElement);
    }
  });

  it("wraps from the last focusable back to the first", async () => {
    const { user } = await openDialog("dialog", { withInitialRef: true });
    const first = screen.getByRole("button", { name: "Excluir minha conta definitivamente" });
    const last = screen.getByRole("button", { name: "Cancelar" });

    last.focus();
    await user.tab();

    expect(first).toHaveFocus();
  });
});

describe("Dialog dismissal", () => {
  it("closes on Escape and calls onClose once", async () => {
    const onClose = vi.fn();
    const { user } = await openDialog("dialog", { onClose });

    await user.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("does not close on Escape while busy (S11 enviando)", async () => {
    const onClose = vi.fn();
    const { user } = await openDialog("alertdialog", { busy: true, onClose });

    await user.keyboard("{Escape}");

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
  });

  it("alertdialog: click on the scrim neither closes nor confirms", async () => {
    const onClose = vi.fn();
    const { user } = await openDialog("alertdialog", { onClose });

    await user.click(screen.getByTestId("dialog-scrim"));

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
  });

  it("click inside the panel never closes the dialog", async () => {
    const onClose = vi.fn();
    const { user } = await openDialog("dialog", { onClose });

    await user.click(screen.getByText("Esta ação é imediata e não pode ser desfeita."));

    expect(onClose).not.toHaveBeenCalled();
  });
});

describe("Dialog styles (design G3, MOT-12/13, ART-01)", () => {
  const css = styles["./Dialog.css"] ?? "";

  it("ships a Dialog.css stylesheet", () => {
    expect(styles["./Dialog.css"]).toBeDefined();
  });

  it("paints the scrim with --color-scrim and no shadow or glow", () => {
    const scrimRule = /\.dialog__scrim\s*\{([^}]*)\}/.exec(css)?.[1] ?? "";
    expect(scrimRule).toMatch(/var\(--color-scrim\)/);
    expect(scrimRule).not.toMatch(/box-shadow|filter|text-shadow/);
  });

  it("uses only --shadow-dialog as shadow, on an opaque surface panel without glass", () => {
    const panelRule = /\.dialog__panel\s*\{([^}]*)\}/.exec(css)?.[1] ?? "";
    expect(panelRule).toMatch(/box-shadow:\s*var\(--shadow-dialog\)/);
    expect(panelRule).toMatch(/background:\s*var\(--color-surface\)/);
    expect(panelRule).not.toMatch(/backdrop-filter/);
    expect(css.match(/box-shadow:[^;]+;/g) ?? []).toEqual(["box-shadow: var(--shadow-dialog);"]);
  });

  it("scrolls whole when the panel is taller than the window (WC-14)", () => {
    expect(css).toMatch(/overflow-y:\s*auto/);
    expect(css).toMatch(/max-height:\s*100d?vh/);
  });

  it("animates panel scale/opacity with dialog durations (MOT-12/13)", () => {
    expect(css).toMatch(/--duration-dialog-in/);
    expect(css).toMatch(/--duration-dialog-out/);
    expect(css).toMatch(/scale\(0\.97\)/);
  });

  it("keeps only opacity under prefers-reduced-motion (A11Y-37)", () => {
    expect(css).toMatch(/@media \(prefers-reduced-motion: reduce\)/);
  });

  it("adds a 1px panel border under prefers-contrast: more (A11Y-38)", () => {
    expect(css).toMatch(/@media \(prefers-contrast: more\)/);
    expect(css).toMatch(/border:\s*1px solid/);
  });
});
