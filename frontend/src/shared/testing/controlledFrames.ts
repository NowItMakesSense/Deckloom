// Helper de teste (T-167): requestAnimationFrame/cancelAnimationFrame controlados, deterministicos.
// O frame so roda quando o teste chama flush(); desfazer com vi.unstubAllGlobals().
import { act } from "@testing-library/react";
import { vi } from "vitest";

export interface ControlledFrames {
  /** Dispara todos os frames pendentes (dentro de act). */
  flush: () => void;
  /** Quantidade de frames pedidos e ainda nao disparados nem cancelados. */
  pending: () => number;
  /** Ids passados a cancelAnimationFrame, na ordem. */
  cancelled: number[];
  /** Ids devolvidos por requestAnimationFrame, na ordem. */
  requested: number[];
}

export function stubControlledFrames(): ControlledFrames {
  let next = 1;
  const queue = new Map<number, FrameRequestCallback>();
  const cancelled: number[] = [];
  const requested: number[] = [];
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => {
    const id = next++;
    requested.push(id);
    queue.set(id, callback);
    return id;
  });
  vi.stubGlobal("cancelAnimationFrame", (id: number) => {
    cancelled.push(id);
    queue.delete(id);
  });
  return {
    flush: () => {
      act(() => {
        const entries = [...queue.values()];
        queue.clear();
        entries.forEach((callback) => callback(0));
      });
    },
    pending: () => queue.size,
    cancelled,
    requested,
  };
}
