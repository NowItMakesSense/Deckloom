import { useCallback, useEffect, useState } from "react";
import { useApiClient } from "@/app/SessionProvider";
import type { Profile } from "@/shared/api";

const PROFILE_PATH = "/api/v1/me/profile";

export type ProfileState =
  { status: "loading" } | { status: "error" } | { status: "ready"; profile: Profile };

// Sem cache: cada entrada na tela le o perfil do servidor (design.md, sessao e perfil nao sao cacheados).
// Um 401 ja derruba a sessao no cliente de API e o guard leva a S2; aqui nao ha tela de erro para ele.
export function useProfile(): { state: ProfileState; retry: () => void } {
  const client = useApiClient();
  const [state, setState] = useState<ProfileState>({ status: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    client
      .get<Profile>(PROFILE_PATH)
      .then((profile) => {
        if (active) setState({ status: "ready", profile });
      })
      .catch(() => {
        if (active) setState({ status: "error" });
      });
    return () => {
      active = false;
    };
  }, [client, attempt]);

  const retry = useCallback(() => {
    setState({ status: "loading" });
    setAttempt((value) => value + 1);
  }, []);

  return { state, retry };
}
