import { startTransition, useCallback, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { AppHeader } from "@/design-system/AppHeader";
import type { AccountMenuAvatar } from "@/design-system/AccountMenu";
import type { Avatar } from "@/shared/api";
import { useApiClient, useClearSession, useSession } from "./SessionProvider";

const LOGOUT_PATH = "/api/v1/auth/logout";
const SIGNED_OUT_ROUTE = "/entrar?aviso=saiu";

function toMenuAvatar(avatar: Avatar): AccountMenuAvatar {
  return { src: avatar.imageUrl, label: avatar.alt, custom: avatar.kind === "custom" };
}

/** Header global ligado a sessao; "Sair" encerra no servidor mas limpa o estado local mesmo se falhar. */
export function SessionHeader(): ReactNode {
  const session = useSession();
  const client = useApiClient();
  const clear = useClearSession();
  const navigate = useNavigate();

  const signOut = useCallback(async () => {
    try {
      await client.post(LOGOUT_PATH);
    } catch {
      // O logout e sempre 204 no contrato; em falha a sessao local cai do mesmo jeito.
    }
    // No mesmo tick: sessao guest e navegacao entram juntas, sem o guard redirecionar.
    startTransition(() => {
      clear();
      navigate(SIGNED_OUT_ROUTE, { replace: true });
    });
  }, [client, clear, navigate]);

  const player =
    session.status === "player"
      ? { displayName: session.player.displayName, avatar: toMenuAvatar(session.player.avatar) }
      : undefined;

  return (
    <AppHeader
      player={player}
      loading={session.status === "loading"}
      onSignOut={() => void signOut()}
    />
  );
}
