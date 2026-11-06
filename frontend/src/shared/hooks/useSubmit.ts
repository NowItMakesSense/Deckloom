import { useCallback, useEffect, useRef, useState } from "react";

import { ApiError } from "../api/apiClient";
import { RATE_LIMIT_NOTICE_DELAY_MS } from "../../design-system/RateLimitNotice";

export type SubmitStatus = "idle" | "sending" | "success" | "error";

export interface UseSubmitOptions {
  /** Padrão 20_000. */
  timeoutMs?: number;
}

export interface UseSubmitResult<T> {
  status: SubmitStatus;
  /** true após 2 s em "sending". */
  showRateLimitNotice: boolean;
  data: T | undefined;
  error: Error | undefined;
  submit: () => Promise<void>;
}

const DEFAULT_TIMEOUT_MS = 20_000;

interface SubmitState<T> {
  status: SubmitStatus;
  showRateLimitNotice: boolean;
  data: T | undefined;
  error: Error | undefined;
}

const IDLE_STATE: SubmitState<never> = {
  status: "idle",
  showRateLimitNotice: false,
  data: undefined,
  error: undefined,
};

export function useSubmit<T>(
  action: (signal: AbortSignal) => Promise<T>,
  options?: UseSubmitOptions,
): UseSubmitResult<T> {
  const timeoutMs = options?.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const [state, setState] = useState<SubmitState<T>>(IDLE_STATE);
  const sendingRef = useRef(false);
  const cleanupRef = useRef<(() => void) | undefined>(undefined);

  useEffect(() => () => cleanupRef.current?.(), []);

  const submit = useCallback(async (): Promise<void> => {
    if (sendingRef.current) {
      return;
    }
    sendingRef.current = true;
    const controller = new AbortController();
    let settled = false;

    const noticeTimer = setTimeout(() => {
      setState((previous) => ({ ...previous, showRateLimitNotice: true }));
    }, RATE_LIMIT_NOTICE_DELAY_MS);

    const finish = (next: SubmitState<T>) => {
      settled = true;
      sendingRef.current = false;
      clearTimeout(noticeTimer);
      clearTimeout(timeoutTimer);
      cleanupRef.current = undefined;
      setState(next);
    };

    const timeoutTimer = setTimeout(() => {
      if (settled) {
        return;
      }
      controller.abort();
      finish({
        ...IDLE_STATE,
        status: "error",
        error: new ApiError(0, "network_error"),
      });
    }, timeoutMs);

    cleanupRef.current = () => {
      settled = true;
      clearTimeout(noticeTimer);
      clearTimeout(timeoutTimer);
    };

    setState({ ...IDLE_STATE, status: "sending" });

    try {
      const data = await action(controller.signal);
      if (!settled) {
        finish({ ...IDLE_STATE, status: "success", data });
      }
    } catch (caught) {
      if (!settled) {
        const error = caught instanceof Error ? caught : new Error(String(caught));
        finish({ ...IDLE_STATE, status: "error", error });
      }
    }
  }, [action, timeoutMs]);

  return { ...state, submit };
}
