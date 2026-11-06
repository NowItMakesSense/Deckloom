import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { RATE_LIMIT_NOTICE_TEXT, RateLimitNotice } from "./RateLimitNotice";

// T-100 (habilitadora): A11Y-17, MOT-06, WC-12.

describe("RateLimitNotice", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("keeps a role=status region in the DOM from the start, with no text (A11Y-17)", () => {
    render(<RateLimitNotice sending={false} />);

    const region = screen.getByRole("status");
    expect(region).toBeInTheDocument();
    expect(region).toBeEmptyDOMElement();
  });

  it("does not show the wait notice before 2 s of sending (A11Y-17)", () => {
    render(<RateLimitNotice sending />);

    act(() => {
      vi.advanceTimersByTime(1_999);
    });

    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByText(RATE_LIMIT_NOTICE_TEXT)).not.toBeInTheDocument();
  });

  it("shows the exact pt-BR wait text inside the status region after 2 s (A11Y-17)", () => {
    render(<RateLimitNotice sending />);

    act(() => {
      vi.advanceTimersByTime(2_000);
    });

    expect(RATE_LIMIT_NOTICE_TEXT).toBe(
      "Por segurança, esta resposta está demorando um pouco mais. Aguarde, não é necessário enviar de novo.",
    );
    expect(screen.getByRole("status")).toHaveTextContent(RATE_LIMIT_NOTICE_TEXT);
  });

  it("has no countdown and no blocked-account wording (U14)", () => {
    render(<RateLimitNotice sending />);

    act(() => {
      vi.advanceTimersByTime(12_000);
    });

    const text = screen.getByRole("status").textContent ?? "";
    expect(text).not.toMatch(/\d/);
    expect(text).not.toMatch(/bloquead|muitas tentativas/i);
  });

  it("removes the text when sending ends, keeping the region (A11Y-17)", () => {
    const { rerender } = render(<RateLimitNotice sending />);
    act(() => {
      vi.advanceTimersByTime(3_000);
    });
    expect(screen.getByRole("status")).toHaveTextContent(RATE_LIMIT_NOTICE_TEXT);

    rerender(<RateLimitNotice sending={false} />);

    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  });

  it("restarts the 2 s wait on a new sending cycle", () => {
    const { rerender } = render(<RateLimitNotice sending />);
    act(() => {
      vi.advanceTimersByTime(3_000);
    });
    rerender(<RateLimitNotice sending={false} />);
    rerender(<RateLimitNotice sending />);

    act(() => {
      vi.advanceTimersByTime(1_500);
    });

    expect(screen.queryByText(RATE_LIMIT_NOTICE_TEXT)).not.toBeInTheDocument();
  });
});
