import type { ReactNode } from "react";
import { Link as RouterLink, useLocation } from "react-router-dom";

import { Alert } from "@/design-system/Alert";
import { ComingSoonSeal } from "@/design-system/ComingSoonSeal";
import {
  BookTextIcon,
  CartIcon,
  ChartLineIcon,
  ChevronRightIcon,
  LayersIcon,
} from "@/design-system/icons";
import { useSession } from "@/app/SessionProvider";

import "./InicioPage.css";

interface Area {
  name: string;
  hint: string;
  icon: ReactNode;
}

// Areas ainda sem tela: nao sao links (REQ-L04), so o selo "Em breve".
const AREAS: readonly Area[] = [
  { name: "Decks", hint: "Monte e organize seus decks.", icon: <LayersIcon size={24} /> },
  { name: "Coleção", hint: "Registre as cartas que você tem.", icon: <BookTextIcon size={24} /> },
  { name: "Meta", hint: "Veja os decks em alta.", icon: <ChartLineIcon size={24} /> },
  { name: "Lojas", hint: "Compare preços de cartas.", icon: <CartIcon size={24} /> },
];

function successMessage(state: unknown): string | null {
  if (typeof state !== "object" || state === null || !("message" in state)) return null;
  return typeof state.message === "string" && state.message !== "" ? state.message : null;
}

export function InicioPage(): ReactNode {
  const session = useSession();
  const location = useLocation();
  const name = session.status === "player" ? session.player.displayName : "";
  const message = successMessage(location.state);

  return (
    <main id="conteudo" tabIndex={-1} className="inicio">
      <h1>{name ? `Olá, ${name}` : "Início"}</h1>
      {message ? <Alert variant="success">{message}</Alert> : null}
      <ul className="inicio__areas" aria-label="Áreas do Deckloom">
        {AREAS.map((area) => (
          <li key={area.name} className="inicio__area">
            <span className="inicio__icon">{area.icon}</span>
            <div className="inicio__text">
              <h2>{area.name}</h2>
              <p>{area.hint}</p>
            </div>
            <ComingSoonSeal />
          </li>
        ))}
      </ul>
      <RouterLink to="/perfil" className="inicio__profile">
        <span>Meu perfil</span>
        <ChevronRightIcon size={20} />
      </RouterLink>
    </main>
  );
}
