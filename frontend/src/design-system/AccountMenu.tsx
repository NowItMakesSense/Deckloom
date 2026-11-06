import { useEffect, useId, useRef, useState } from "react";

import { Link } from "react-router-dom";

import { Avatar } from "./Avatar";
import "./AccountMenu.css";

export interface AccountMenuAvatar {
  src: string;
  label?: string;
  custom?: boolean;
}

export interface AccountMenuProps {
  displayName: string;
  avatar: AccountMenuAvatar;
  onSignOut: () => void;
}

/** Padrao disclosure (A11Y-33): botao com aria-expanded e painel com link e botao, sem role=menu. */
export function AccountMenu({ displayName, avatar, onSignOut }: AccountMenuProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const firstItemRef = useRef<HTMLAnchorElement>(null);
  const panelId = useId();

  function closeAndReturnFocus() {
    setOpen(false);
    triggerRef.current?.focus();
  }

  useEffect(() => {
    if (!open) return;
    firstItemRef.current?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    }
    function onPointerDown(event: Event) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }
    document.addEventListener("keydown", onKeyDown);
    document.addEventListener("mousedown", onPointerDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("mousedown", onPointerDown);
    };
  }, [open]);

  return (
    <div
      className="account-menu"
      ref={rootRef}
      onBlur={(event) => {
        const next = event.relatedTarget as Node | null;
        if (open && next && !rootRef.current?.contains(next)) setOpen(false);
      }}
    >
      <button
        ref={triggerRef}
        type="button"
        className="account-menu__trigger"
        aria-label={`Menu da conta: ${displayName}`}
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        onClick={() => setOpen((value) => !value)}
      >
        <Avatar {...avatar} name={displayName} decorative />
        <span className="account-menu__name" aria-hidden="true">
          {displayName}
        </span>
      </button>
      {open && (
        <div id={panelId} className="account-menu__panel" data-account-menu-panel>
          <p className="account-menu__full-name">{displayName}</p>
          <Link
            ref={firstItemRef}
            className="account-menu__item"
            to="/perfil"
            onClick={closeAndReturnFocus}
          >
            Meu perfil
          </Link>
          <button
            type="button"
            className="account-menu__item"
            onClick={() => {
              onSignOut();
              closeAndReturnFocus();
            }}
          >
            Sair
          </button>
        </div>
      )}
    </div>
  );
}
