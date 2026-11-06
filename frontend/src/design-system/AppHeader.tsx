import { Link } from "react-router-dom";

import { AccountMenu, type AccountMenuAvatar } from "./AccountMenu";
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
  onSignOut?: () => void;
}

const noop = () => {};

export function AppHeader({ player, loading = false, onSignOut = noop }: AppHeaderProps) {
  return (
    <header className="app-header">
      <span className="app-header__wordmark">Deckloom</span>
      {loading ? (
        <div className="app-header__actions app-header__actions--pending" aria-hidden="true" />
      ) : player ? (
        <AccountMenu
          displayName={player.displayName}
          avatar={player.avatar}
          onSignOut={onSignOut}
        />
      ) : (
        <nav className="app-header__actions" aria-label="Acesso">
          <Link className="app-header__link" to="/entrar">
            Entrar
          </Link>
          <Link className="app-header__link" to="/cadastro">
            Criar conta
          </Link>
        </nav>
      )}
    </header>
  );
}
