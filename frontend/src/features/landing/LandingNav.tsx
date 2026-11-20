import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";

import { useTheme } from "@/app/theme/useTheme";
import { LogInIcon, MenuIcon, MoonIcon, SunIcon, UserPlusIcon, XIcon } from "@/design-system/icons";

import "./LandingNav.css";

const LOGO_LIGHT = "/brand/deckloom/deckloom-logo.svg";
const LOGO_DARK = "/brand/deckloom/deckloom-logo-on-dark.svg";
const SYMBOL = "/brand/deckloom/deckloom-symbol.svg";
const MENU_ID = "landing-menu";

/** Links de secao: ancoras da propria pagina, na ordem da jornada (nenhum leva a rota inexistente). */
const SECTION_LINKS = [
  { href: "#como-funciona", label: "Como funciona" },
  { href: "#planos", label: "Planos" },
] as const;

/**
 * Navegacao "dynamic island" (docs/design-system.md 1.13): pilula flutuante centralizada, em vidro. No celular o
 * menu colapsa atras de um botao (aria-expanded) que fecha com Esc, ao escolher um link e ao tocar fora.
 * Criar conta e o botao primario; o controle de tema e um so.
 */
export function LandingNav() {
  const [open, setOpen] = useState(false);
  const { theme, toggle } = useTheme();
  const islandRef = useRef<HTMLElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);

  // Aberto, o menu fecha ao tocar fora e com Esc (o foco volta ao botao).
  useEffect(() => {
    if (!open) return;
    const closeOutside = (event: PointerEvent) => {
      if (!islandRef.current?.contains(event.target as Node)) setOpen(false);
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

  const dark = theme === "dark";
  return (
    <header className="landing-nav">
      <nav ref={islandRef} className="landing-nav__island" aria-label="Principal">
        <Link to="/" className="landing-nav__logo">
          <picture>
            <source media="(max-width: 479.98px)" srcSet={SYMBOL} />
            <img src={dark ? LOGO_DARK : LOGO_LIGHT} alt="Deckloom" width="132" height="36" />
          </picture>
        </Link>
        <div id={MENU_ID} className="landing-nav__menu" data-open={open ? "true" : "false"}>
          <ul className="landing-nav__links">
            {SECTION_LINKS.map((link) => (
              <li key={link.href}>
                <a className="landing-nav__link" href={link.href} onClick={() => setOpen(false)}>
                  {link.label}
                </a>
              </li>
            ))}
          </ul>
          <Link className="landing-nav__link landing-nav__link--enter" to="/entrar">
            <LogInIcon size={18} />
            Entrar
          </Link>
        </div>
        <div className="landing-nav__actions">
          <Link className="landing-cta" to="/cadastro">
            <UserPlusIcon size={18} />
            Criar conta
          </Link>
          <button
            type="button"
            className="landing-nav__icon"
            aria-label={dark ? "Usar tema claro" : "Usar tema escuro"}
            onClick={toggle}
          >
            {dark ? <SunIcon size={20} /> : <MoonIcon size={20} />}
          </button>
          <button
            ref={toggleRef}
            type="button"
            className="landing-nav__icon landing-nav__menu-toggle"
            aria-label="Menu"
            aria-expanded={open}
            aria-controls={MENU_ID}
            onClick={() => setOpen((current) => !current)}
          >
            {open ? <XIcon size={20} /> : <MenuIcon size={20} />}
          </button>
        </div>
      </nav>
    </header>
  );
}
