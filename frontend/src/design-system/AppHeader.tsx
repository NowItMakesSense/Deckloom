import { useEffect, useId, useLayoutEffect, useRef, useState, type RefObject } from "react";
import { Link } from "react-router-dom";

import { AccountMenu, type AccountMenuAvatar } from "./AccountMenu";
import { LogInIcon, MenuIcon, MoonIcon, SunIcon, UserPlusIcon, XIcon } from "./icons";
import "./AppHeader.css";

export interface AppHeaderPlayer {
  displayName: string;
  avatar: AccountMenuAvatar;
}

export interface AppHeaderProps {
  /** Ausente = visitante. */
  player?: AppHeaderPlayer;
  /** Sessao ainda sendo lida: o header segue montado, sem acoes de visitante nem de Jogador (R-19). */
  loading?: boolean;
  /** Tema em uso: escolhe a logo e o icone do botao de tema. */
  theme?: "light" | "dark";
  /** Ausente = sem botao de troca de tema. */
  onToggleTheme?: () => void;
  onSignOut?: () => void;
}

const LOGO_LIGHT = "/brand/deckloom/deckloom-logo.svg";
const LOGO_DARK = "/brand/deckloom/deckloom-logo-on-dark.svg";
const SYMBOL = "/brand/deckloom/deckloom-symbol.svg";
/** Altura medida da pilula, lida pelo scroll-padding do documento (AppHeader.css). */
const BLOCK_SIZE_VAR = "--app-header-block-size";

const noop = () => {};

/**
 * Publica a altura da pilula no documento enquanto ela estiver montada: a pilula e fixa no topo e cresce com o texto
 * (200%), entao o scroll-padding precisa da medida real para um campo focado nunca ficar sob ela.
 */
function usePublishedBlockSize(ref: RefObject<HTMLElement | null>) {
  useLayoutEffect(() => {
    const element = ref.current;
    const root = document.documentElement;
    if (!element || typeof ResizeObserver === "undefined") return;
    const publish = () => {
      root.style.setProperty(BLOCK_SIZE_VAR, `${element.getBoundingClientRect().height}px`);
    };
    const observer = new ResizeObserver(publish);
    observer.observe(element);
    return () => {
      observer.disconnect();
      root.style.removeProperty(BLOCK_SIZE_VAR);
    };
  }, [ref]);
}

/**
 * A pilula e fixa no topo: o Chromium nao rola no foco um controle que esta so em parte sob ela (o scroll-padding so
 * vale quando o controle esta fora da janela). Ao receber foco com o topo sob a pilula, o controle e trazido com
 * scrollIntoView, que respeita o scroll-padding. Foco dentro da propria pilula ou de um dialogo (acima dela) nao rola.
 */
function useKeepFocusBelow(ref: RefObject<HTMLElement | null>) {
  useEffect(() => {
    const onFocusIn = (event: FocusEvent) => {
      const header = ref.current;
      const target = event.target;
      if (!header || !(target instanceof HTMLElement) || header.contains(target)) return;
      if (target.closest('[role="dialog"], [role="alertdialog"]')) return;
      if (target.getBoundingClientRect().top < header.getBoundingClientRect().bottom) {
        target.scrollIntoView({ block: "nearest" });
      }
    };
    document.addEventListener("focusin", onFocusIn);
    return () => document.removeEventListener("focusin", onFocusIn);
  }, [ref]);
}

/**
 * Cabecalho das telas da conta: a mesma navegacao "dynamic island" da landing (docs/design-system.md 1.13), uma
 * pilula centralizada em vidro, fixa no topo, com a logo (leva ao inicio), as acoes com icone e a troca de tema.
 * No celular fica numa linha so, como a landing: o visitante guarda o Entrar atras de um botao de menu
 * (aria-expanded) que fecha com Esc (o foco volta ao botao), ao escolher o link e ao tocar fora.
 */
export function AppHeader({
  player,
  loading = false,
  theme = "light",
  onToggleTheme,
  onSignOut = noop,
}: AppHeaderProps) {
  const dark = theme === "dark";
  const visitor = !loading && !player;
  const [open, setOpen] = useState(false);
  const headerRef = useRef<HTMLElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const menuId = useId();

  usePublishedBlockSize(headerRef);
  useKeepFocusBelow(headerRef);

  // Aberto, o menu fecha ao tocar fora e com Esc (o foco volta ao botao), como na LandingNav.
  useEffect(() => {
    if (!open) return;
    const closeOutside = (event: PointerEvent) => {
      if (!headerRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      setOpen(false);
      toggleRef.current?.focus();
    };
    document.addEventListener("pointerdown", closeOutside);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOutside);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  return (
    <header ref={headerRef} className="app-header">
      <Link to="/" className="app-header__logo">
        <picture>
          <source media="(max-width: 479.98px)" srcSet={SYMBOL} />
          <img src={dark ? LOGO_DARK : LOGO_LIGHT} alt="Deckloom" width="132" height="36" />
        </picture>
      </Link>
      <div className="app-header__actions">
        {loading ? (
          <div className="app-header__pending" aria-hidden="true" />
        ) : player ? (
          <AccountMenu
            displayName={player.displayName}
            avatar={player.avatar}
            onSignOut={onSignOut}
          />
        ) : (
          <nav className="app-header__access" aria-label="Acesso">
            <div id={menuId} className="app-header__menu" data-open={open ? "true" : "false"}>
              <Link className="app-header__link" to="/entrar" onClick={() => setOpen(false)}>
                <LogInIcon size={18} />
                Entrar
              </Link>
            </div>
            <Link className="app-header__cta" to="/cadastro">
              <UserPlusIcon size={18} />
              Criar conta
            </Link>
          </nav>
        )}
        {onToggleTheme ? (
          <button
            type="button"
            className="app-header__icon"
            aria-label={dark ? "Usar tema claro" : "Usar tema escuro"}
            onClick={onToggleTheme}
          >
            {dark ? <SunIcon size={20} /> : <MoonIcon size={20} />}
          </button>
        ) : null}
        {visitor ? (
          <button
            ref={toggleRef}
            type="button"
            className="app-header__icon app-header__menu-toggle"
            aria-label="Menu"
            aria-expanded={open}
            aria-controls={menuId}
            onClick={() => setOpen((current) => !current)}
          >
            {open ? <XIcon size={20} /> : <MenuIcon size={20} />}
          </button>
        ) : null}
      </div>
    </header>
  );
}
