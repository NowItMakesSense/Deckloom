import { useEffect, useId, useRef } from "react";
import type { ReactNode, RefObject } from "react";

import "./Dialog.css";

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: string;
  role?: "dialog" | "alertdialog";
  busy?: boolean;
  initialFocusRef?: RefObject<HTMLElement | null>;
  children?: ReactNode;
}

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function focusablesIn(panel: HTMLElement): HTMLElement[] {
  return Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE));
}

function pickInitialFocus(
  panel: HTMLElement,
  role: "dialog" | "alertdialog",
  initial: HTMLElement | null | undefined,
): HTMLElement {
  if (initial) return initial;
  const items = focusablesIn(panel);
  if (role === "alertdialog") {
    const safe = items.filter((el) => el.getAttribute("data-variant") !== "danger");
    const cancel = safe.find((el) => /cancel/i.test(el.textContent ?? ""));
    return cancel ?? safe[0] ?? panel;
  }
  return items[0] ?? panel;
}

// A pagina atras do dialogo (irmaos do dialogo no mesmo pai) fica inerte enquanto aberto.
// Clique no scrim nao fecha em nenhum dos roles (decisao do orquestrador).
export function Dialog({
  open,
  onClose,
  title,
  description,
  role = "dialog",
  busy = false,
  initialFocusRef,
  children,
}: DialogProps) {
  const uid = useId();
  const titleId = `${uid}-title`;
  const descriptionId = `${uid}-description`;
  const scrimRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  const busyRef = useRef(busy);

  useEffect(() => {
    onCloseRef.current = onClose;
    busyRef.current = busy;
  });

  useEffect(() => {
    if (!open) return undefined;
    const panel = panelRef.current;
    const scrim = scrimRef.current;
    if (!panel || !scrim) return undefined;

    const trigger = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const inerted = Array.from(scrim.parentElement?.children ?? []).filter(
      (el) => el !== scrim && !el.hasAttribute("inert"),
    );
    inerted.forEach((el) => el.setAttribute("inert", ""));

    pickInitialFocus(panel, role, initialFocusRef?.current).focus();

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        if (busyRef.current) return;
        event.preventDefault();
        onCloseRef.current();
        return;
      }
      if (event.key !== "Tab") return;
      const items = focusablesIn(panel);
      if (items.length === 0) {
        event.preventDefault();
        return;
      }
      const first = items[0];
      const last = items[items.length - 1];
      const active = document.activeElement;
      if (event.shiftKey && (active === first || !panel.contains(active))) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (active === last || !panel.contains(active))) {
        event.preventDefault();
        first.focus();
      }
    };
    // Botao "Cancelar" do conteudo fecha o dialogo (exigido por T-099), salvo enquanto busy.
    const onClick = (event: MouseEvent) => {
      const button = event.target instanceof Element ? event.target.closest("button") : null;
      if (
        button &&
        panel.contains(button) &&
        !busyRef.current &&
        /cancel/i.test(button.textContent ?? "")
      ) {
        onCloseRef.current();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    document.addEventListener("click", onClick);

    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("click", onClick);
      inerted.forEach((el) => el.removeAttribute("inert"));
      trigger?.focus();
    };
  }, [open, role, initialFocusRef]);

  if (!open) return null;

  return (
    <div ref={scrimRef} className="dialog__scrim" data-testid="dialog-scrim">
      <div
        ref={panelRef}
        className="dialog__panel"
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        tabIndex={-1}
      >
        <h2 id={titleId} className="dialog__title">
          {title}
        </h2>
        {description ? (
          <p id={descriptionId} className="dialog__description">
            {description}
          </p>
        ) : null}
        {children}
      </div>
    </div>
  );
}
