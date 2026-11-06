import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ApiError } from "../api/apiClient";
import { useSubmit } from "./useSubmit";

// T-100 (habilitadora): A11Y-17, WC-12; timeout de 20 s (ADR-0005).

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe("useSubmit", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("starts idle with no notice, data or error", () => {
    const { result } = renderHook(() => useSubmit(() => Promise.resolve("ok")));

    expect(result.current.status).toBe("idle");
    expect(result.current.showRateLimitNotice).toBe(false);
    expect(result.current.data).toBeUndefined();
    expect(result.current.error).toBeUndefined();
  });

  it("marks sending on submit and success with data when the action resolves", async () => {
    const d = deferred<string>();
    const { result } = renderHook(() => useSubmit(() => d.promise));

    act(() => {
      void result.current.submit();
    });
    expect(result.current.status).toBe("sending");

    await act(async () => {
      d.resolve("ok");
      await d.promise;
    });
    expect(result.current.status).toBe("success");
    expect(result.current.data).toBe("ok");
  });

  it("moves to error with the thrown error when the action rejects", async () => {
    const failure = new ApiError(500, "server_error");
    const { result } = renderHook(() => useSubmit(() => Promise.reject(failure)));

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.status).toBe("error");
    expect(result.current.error).toBe(failure);
    expect(result.current.showRateLimitNotice).toBe(false);
  });

  it("signals the rate limit notice only after 2 s of sending (A11Y-17)", () => {
    const d = deferred<string>();
    const { result } = renderHook(() => useSubmit(() => d.promise));

    act(() => {
      void result.current.submit();
    });
    act(() => {
      vi.advanceTimersByTime(1_999);
    });
    expect(result.current.showRateLimitNotice).toBe(false);

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current.showRateLimitNotice).toBe(true);
    expect(result.current.status).toBe("sending");
  });

  it("clears the notice when the response arrives after 3 s (WC-12)", async () => {
    const d = deferred<string>();
    const { result } = renderHook(() => useSubmit(() => d.promise));
    act(() => {
      void result.current.submit();
    });
    act(() => {
      vi.advanceTimersByTime(3_000);
    });
    expect(result.current.showRateLimitNotice).toBe(true);

    await act(async () => {
      d.resolve("ok");
      await d.promise;
    });

    expect(result.current.status).toBe("success");
    expect(result.current.showRateLimitNotice).toBe(false);
  });

  it("still succeeds at 12 s, before the timeout (WC-12)", async () => {
    const d = deferred<string>();
    const { result } = renderHook(() => useSubmit(() => d.promise));
    act(() => {
      void result.current.submit();
    });
    act(() => {
      vi.advanceTimersByTime(12_000);
    });
    expect(result.current.status).toBe("sending");

    await act(async () => {
      d.resolve("ok");
      await d.promise;
    });

    expect(result.current.status).toBe("success");
  });

  it("at 20 s aborts and becomes a generic network_error, never a wrong-login message (WC-12)", () => {
    let received: AbortSignal | undefined;
    const { result } = renderHook(() =>
      useSubmit((signal) => {
        received = signal;
        return new Promise<string>(() => {});
      }),
    );
    act(() => {
      void result.current.submit();
    });

    act(() => {
      vi.advanceTimersByTime(19_999);
    });
    expect(result.current.status).toBe("sending");

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current.status).toBe("error");
    expect(result.current.error).toBeInstanceOf(ApiError);
    expect((result.current.error as ApiError).code).toBe("network_error");
    expect((result.current.error as ApiError).status).toBe(0);
    expect(result.current.showRateLimitNotice).toBe(false);
    expect(received?.aborted).toBe(true);
  });

  it("honours a custom timeout such as 30 s for upload", () => {
    const { result } = renderHook(() =>
      useSubmit(() => new Promise<string>(() => {}), { timeoutMs: 30_000 }),
    );
    act(() => {
      void result.current.submit();
    });

    act(() => {
      vi.advanceTimersByTime(29_999);
    });
    expect(result.current.status).toBe("sending");

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current.status).toBe("error");
  });

  it("does not duplicate the submission while sending (WC-12)", () => {
    const action = vi.fn(() => new Promise<string>(() => {}));
    const { result } = renderHook(() => useSubmit(action));

    act(() => {
      void result.current.submit();
    });
    act(() => {
      void result.current.submit();
      void result.current.submit();
    });

    expect(action).toHaveBeenCalledTimes(1);
  });

  it("clears previous error and notice when submitting again", () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const action = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise);
    const { result } = renderHook(() => useSubmit(action));

    act(() => {
      void result.current.submit();
    });
    act(() => {
      vi.advanceTimersByTime(20_000);
    });
    expect(result.current.status).toBe("error");

    act(() => {
      void result.current.submit();
    });

    expect(action).toHaveBeenCalledTimes(2);
    expect(result.current.status).toBe("sending");
    expect(result.current.error).toBeUndefined();
    expect(result.current.showRateLimitNotice).toBe(false);
  });
});
