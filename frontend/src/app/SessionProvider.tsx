import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { createApiClient } from "@/shared/api";
import type { ApiClient, SessionSummary } from "@/shared/api";

export type SessionState =
  { status: "loading" } | { status: "guest" } | { status: "player"; player: SessionSummary };

export interface SessionProviderProps {
  baseUrl: string;
  children: ReactNode;
}

interface SessionContextValue {
  session: SessionState;
  client: ApiClient;
  refresh: () => Promise<void>;
  clear: () => void;
}

const SessionContext = createContext<SessionContextValue | null>(null);
const SESSION_PATH = "/api/v1/auth/session";

export function SessionProvider({ baseUrl, children }: SessionProviderProps): ReactNode {
  const [session, setSession] = useState<SessionState>({ status: "loading" });

  const client = useMemo(
    () =>
      createApiClient({
        baseUrl,
        isDevelopment: import.meta.env.DEV,
        onSessionExpired: () => setSession({ status: "guest" }),
      }),
    [baseUrl],
  );

  const refresh = useCallback(async (): Promise<void> => {
    try {
      const player = await client.get<SessionSummary>(SESSION_PATH);
      setSession({ status: "player", player });
    } catch {
      setSession({ status: "guest" });
    }
  }, [client]);

  useEffect(() => {
    let active = true;
    client
      .get<SessionSummary>(SESSION_PATH)
      .then((player) => {
        if (active) setSession({ status: "player", player });
      })
      .catch(() => {
        if (active) setSession({ status: "guest" });
      });
    return () => {
      active = false;
    };
  }, [client]);

  const clear = useCallback(() => setSession({ status: "guest" }), []);

  const value = useMemo(
    () => ({ session, client, refresh, clear }),
    [session, client, refresh, clear],
  );
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

function useSessionContext(): SessionContextValue {
  const context = useContext(SessionContext);
  if (!context) throw new Error("SessionProvider ausente");
  return context;
}

export function useSession(): SessionState {
  return useSessionContext().session;
}

export function useApiClient(): ApiClient {
  return useSessionContext().client;
}

/** Descarta a sessao local (sem chamada de rede): a sessao e a unica fonte de verdade. */
export function useClearSession(): () => void {
  return useSessionContext().clear;
}

export function useRefreshSession(): () => Promise<void> {
  return useSessionContext().refresh;
}
