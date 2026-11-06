import { fireEvent, render as rtlRender, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

import { expectNoA11yViolations } from "../shared/testing/expectNoA11yViolations";
import { Alert } from "./Alert";
import { Avatar } from "./Avatar";
import { Button } from "./Button";
import { Divider } from "./Divider";
import { GoogleButton } from "./GoogleButton";
import { Link } from "./Link";
import { Skeleton } from "./Skeleton";
import { Spinner } from "./Spinner";
import { Tag } from "./Tag";

// Link, AppHeader e AccountMenu usam o router (R-19): todo render ganha um MemoryRouter.
const render = (ui: ReactElement) => rtlRender(ui, { wrapper: MemoryRouter });

// T-096 (habilitadora): A11Y-03, A11Y-15, A11Y-16, A11Y-24, A11Y-30, A11Y-36; U15.

describe("Button", () => {
  it("renders an accessible button with its label", async () => {
    const { container } = render(<Button>Entrar</Button>);

    expect(screen.getByRole("button", { name: "Entrar" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });

  it("keeps the label and sets aria-busy while loading", () => {
    render(<Button loading>Entrar</Button>);

    const button = screen.getByRole("button", { name: /Entrar/ });
    expect(button).toHaveAttribute("aria-busy", "true");
    expect(button).toHaveTextContent("Entrar");
  });

  it("does not set aria-busy when idle", () => {
    render(<Button>Entrar</Button>);

    expect(screen.getByRole("button")).not.toHaveAttribute("aria-busy", "true");
  });

  it("does not fire onClick while loading (no duplicate submit)", async () => {
    const onClick = vi.fn();
    render(
      <Button loading onClick={onClick}>
        Entrar
      </Button>,
    );

    await userEvent.click(screen.getByRole("button"));

    expect(onClick).not.toHaveBeenCalled();
  });

  it("fires onClick once when idle", async () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Entrar</Button>);

    await userEvent.click(screen.getByRole("button"));

    expect(onClick).toHaveBeenCalledTimes(1);
  });
});

describe("GoogleButton (U15)", () => {
  it("has the fixed label Continuar com Google", () => {
    render(<GoogleButton />);

    expect(screen.getByRole("button", { name: "Continuar com Google" })).toBeInTheDocument();
  });

  it("uses an inline local SVG and no external resource", () => {
    const { container } = render(<GoogleButton />);

    expect(container.querySelector("svg")).not.toBeNull();
    expect(container.querySelector("img")).toBeNull();
    expect(container.innerHTML).not.toMatch(/https?:\/\//);
  });

  it("hides the decorative SVG from assistive tech", () => {
    const { container } = render(<GoogleButton />);

    expect(container.querySelector("svg")).toHaveAttribute("aria-hidden", "true");
  });

  it("has no axe violations", async () => {
    const { container } = render(<GoogleButton />);

    expect(screen.getByRole("button", { name: "Continuar com Google" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("Link (A11Y-24)", () => {
  it("shows (abre em nova aba) for external links", () => {
    render(
      <Link href="https://exemplo.com/termos" external>
        Termos
      </Link>,
    );

    const link = screen.getByRole("link", { name: /Termos.*\(abre em nova aba\)/ });
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
  });

  it("does not open a new tab for internal links", () => {
    render(<Link href="/entrar">Entrar</Link>);

    const link = screen.getByRole("link", { name: "Entrar" });
    expect(link).not.toHaveAttribute("target");
  });

  it("has no axe violations", async () => {
    const { container } = render(
      <Link href="https://exemplo.com" external>
        Termos
      </Link>,
    );

    expect(screen.getByRole("link", { name: /Termos/ })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("Alert (A11Y-03, A11Y-16)", () => {
  it("info uses role=status", () => {
    render(<Alert variant="info">Dica</Alert>);

    expect(screen.getByRole("status")).toHaveTextContent("Dica");
  });

  it("success uses role=status", () => {
    render(<Alert variant="success">Salvo</Alert>);

    expect(screen.getByRole("status")).toHaveTextContent("Salvo");
  });

  it("page error uses role=alert", () => {
    render(<Alert variant="error">Falhou</Alert>);

    expect(screen.getByRole("alert")).toHaveTextContent("Falhou");
  });

  it("pairs a decorative icon with the text (not color alone)", () => {
    render(<Alert variant="error">Falhou</Alert>);

    const icon = screen.getByRole("alert").querySelector("svg");
    expect(icon).not.toBeNull();
    expect(icon).toHaveAttribute("aria-hidden", "true");
  });

  it("has no axe violations in any variant", async () => {
    const { container } = render(
      <>
        <Alert variant="info">Dica</Alert>
        <Alert variant="success">Salvo</Alert>
        <Alert variant="error">Falhou</Alert>
      </>,
    );

    expect(screen.getAllByRole("status")).toHaveLength(2);
    expect(screen.getByRole("alert")).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("Spinner and Skeleton (A11Y-30)", () => {
  it("Spinner is accompanied by visible text", () => {
    render(<Spinner label="Carregando perfil" />);

    expect(screen.getByText("Carregando perfil")).toBeVisible();
  });

  it("Skeleton exposes aria-busy", () => {
    const { container } = render(<Skeleton />);

    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it("have no axe violations", async () => {
    const { container } = render(
      <>
        <Spinner label="Carregando" />
        <Skeleton />
      </>,
    );

    expect(screen.getByText("Carregando")).toBeInTheDocument();
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
    await expectNoA11yViolations(container);
  });
});

describe("Tag", () => {
  it("is plain text, not a button", () => {
    render(<Tag>Commander</Tag>);

    expect(screen.getByText("Commander")).toBeInTheDocument();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("has no axe violations", async () => {
    const { container } = render(<Tag>Commander</Tag>);

    expect(screen.getByText("Commander")).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("Avatar (A11Y-15)", () => {
  it("preset avatar has alt 'Avatar: {rotulo}'", () => {
    render(<Avatar src="/presets/lobo.webp" label="Lobo" name="Ana" />);

    expect(screen.getByRole("img", { name: "Avatar: Lobo" })).toBeInTheDocument();
  });

  it("custom avatar has alt 'Seu avatar personalizado'", () => {
    render(<Avatar src="/me/avatar/custom" custom name="Ana" />);

    expect(screen.getByRole("img", { name: "Seu avatar personalizado" })).toBeInTheDocument();
  });

  it("is decorative (alt empty) next to the name of the player", () => {
    const { container } = render(
      <span>
        <Avatar src="/presets/lobo.webp" label="Lobo" name="Ana" decorative />
        <span>Ana</span>
      </span>,
    );

    expect(container.querySelector("img")).toHaveAttribute("alt", "");
    expect(screen.queryByRole("img")).toBeNull();
  });

  it("has no axe violations", async () => {
    const { container } = render(<Avatar src="/presets/lobo.webp" label="Lobo" name="Ana" />);

    expect(screen.getByRole("img", { name: "Avatar: Lobo" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });
});

describe("Avatar fallback (WC-10)", () => {
  it("without a loadable image shows the first letter of the name", () => {
    render(<Avatar src="/presets/lobo.webp" label="Lobo" name="Ana" />);

    fireEvent.error(screen.getByRole("img", { name: "Avatar: Lobo" }));

    expect(screen.getByText("A")).toBeInTheDocument();
  });

  it("keeps the same accessible name after the image fails", () => {
    render(<Avatar src="/presets/lobo.webp" label="Lobo" name="Ana" />);

    fireEvent.error(screen.getByRole("img", { name: "Avatar: Lobo" }));

    expect(screen.getByRole("img", { name: "Avatar: Lobo" })).toBeInTheDocument();
  });

  it("uses the first letter of the player name for a custom avatar", () => {
    render(<Avatar src="/me/avatar/custom" custom name="Bruno" />);

    fireEvent.error(screen.getByRole("img", { name: "Seu avatar personalizado" }));

    expect(screen.getByText("B")).toBeInTheDocument();
  });

  it("reserves fixed width and height while the image is slow (no layout shift)", () => {
    const { container } = render(<Avatar src="/presets/lobo.webp" label="Lobo" name="Ana" />);

    const image = container.querySelector("img");
    expect(image).not.toBeNull();
    expect(image).toHaveAttribute("width");
    expect(image).toHaveAttribute("height");
  });
});

describe("Divider", () => {
  it("is decorative and hidden from screen readers", () => {
    const { container } = render(<Divider />);

    const divider = container.firstElementChild;
    expect(divider).not.toBeNull();
    expect(divider).toHaveAttribute("aria-hidden", "true");
  });
});
