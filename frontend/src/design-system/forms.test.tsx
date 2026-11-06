import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { expectNoA11yViolations } from "../shared/testing/expectNoA11yViolations";
import { ErrorSummary } from "./ErrorSummary";
import { FormError } from "./FormError";
import { PasswordField } from "./PasswordField";
import { TextField } from "./TextField";
import { fillField } from "@/shared/testing/fillField";

// T-097 (habilitadora): A11Y-01, A11Y-02, A11Y-03, A11Y-04, A11Y-16, A11Y-22, A11Y-26, A11Y-36;
// MOT-04, WC-06, ART-08.

const styles = import.meta.glob("./*.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

describe("TextField", () => {
  it("associates a visible label with the input", async () => {
    const { container } = render(<TextField label="E-mail" name="email" />);

    const input = screen.getByLabelText("E-mail");
    expect(input).toBeInTheDocument();
    expect(input.tagName).toBe("INPUT");
    const label = screen.getByText("E-mail");
    expect(label.tagName).toBe("LABEL");
    expect(label).toHaveAttribute("for", input.id);
    expect(input.id).not.toBe("");
    await expectNoA11yViolations(container);
  });

  it("does not use the placeholder as the label", () => {
    render(<TextField label="E-mail" name="email" placeholder="voce@exemplo.com" />);

    expect(screen.getByLabelText("E-mail")).toBeInTheDocument();
    expect(screen.getByText("E-mail").tagName).toBe("LABEL");
  });

  it("points aria-describedby to the error and marks aria-invalid when there is an error", async () => {
    const { container } = render(<TextField label="E-mail" name="email" error="E-mail invalido" />);

    const input = screen.getByLabelText("E-mail");
    const error = screen.getByText("E-mail invalido");
    expect(error).toBeInTheDocument();
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(error.closest("[id]")?.id).toBeTruthy();
    const describedBy = (input.getAttribute("aria-describedby") ?? "").split(" ");
    expect(describedBy).toContain(error.closest("[id]")?.id);
    await expectNoA11yViolations(container);
  });

  it("includes the hint in aria-describedby alongside the error", () => {
    render(<TextField label="E-mail" name="email" hint="Usado para entrar" error="Obrigatorio" />);

    const input = screen.getByLabelText("E-mail");
    const hint = screen.getByText("Usado para entrar");
    const describedBy = (input.getAttribute("aria-describedby") ?? "").split(" ");
    expect(describedBy).toContain(hint.closest("[id]")?.id);
    expect(describedBy.length).toBe(2);
  });

  it("has no aria-invalid and no error description without an error", () => {
    render(<TextField label="E-mail" name="email" />);

    const input = screen.getByLabelText("E-mail");
    expect(input).not.toHaveAttribute("aria-invalid");
    expect(input).not.toHaveAttribute("aria-describedby");
  });

  it("removes aria-invalid and the error description once the error is corrected", () => {
    const { rerender } = render(<TextField label="E-mail" name="email" error="Obrigatorio" />);
    expect(screen.getByLabelText("E-mail")).toHaveAttribute("aria-invalid", "true");

    rerender(<TextField label="E-mail" name="email" />);

    const input = screen.getByLabelText("E-mail");
    expect(input).not.toHaveAttribute("aria-invalid");
    expect(input).not.toHaveAttribute("aria-describedby");
    expect(screen.queryByText("Obrigatorio")).not.toBeInTheDocument();
  });

  it("forwards autocomplete to the input", () => {
    render(<TextField label="E-mail" name="email" autoComplete="email" />);

    expect(screen.getByLabelText("E-mail")).toHaveAttribute("autocomplete", "email");
  });

  it("declares a minimum 44px touch target and no external resource", () => {
    const css = styles["./TextField.css"];
    expect(css).toBeDefined();
    expect(css).toContain("--target-min");
    expect(css).not.toMatch(/https?:\/\/|@import/);
    const { container } = render(<TextField label="E-mail" name="email" />);
    expect(container.innerHTML).not.toMatch(/https?:\/\//);
  });
});

describe("PasswordField", () => {
  it("renders a password input with a labelled show/hide button", async () => {
    const { container } = render(
      <PasswordField label="Senha" name="password" autoComplete="current-password" />,
    );

    const input = screen.getByLabelText("Senha");
    expect(input).toHaveAttribute("type", "password");
    const toggle = screen.getByRole("button", { name: /mostrar senha/i });
    expect(toggle).toBeInTheDocument();
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    await expectNoA11yViolations(container);
  });

  it("shows then hides the password, keeping the value and the type password again", async () => {
    render(<PasswordField label="Senha" name="password" autoComplete="current-password" />);
    const input = screen.getByLabelText("Senha");
    await fillField(userEvent.setup(), input, "segredo123");

    await userEvent.click(screen.getByRole("button", { name: /mostrar senha/i }));
    expect(input).toHaveAttribute("type", "text");
    expect(input).toHaveValue("segredo123");
    const hide = screen.getByRole("button", { name: /ocultar senha/i });
    expect(hide).toHaveAttribute("aria-pressed", "true");

    await userEvent.click(hide);
    expect(input).toHaveAttribute("type", "password");
    expect(input).toHaveValue("segredo123");
  });

  it("allows pasting into the field", async () => {
    render(<PasswordField label="Senha" name="password" autoComplete="new-password" />);
    const input = screen.getByLabelText("Senha");

    await userEvent.click(input);
    await userEvent.paste("colada-123");

    expect(input).toHaveValue("colada-123");
  });

  it("uses the autocomplete given by the prop", () => {
    const { rerender } = render(
      <PasswordField label="Senha" name="password" autoComplete="current-password" />,
    );
    expect(screen.getByLabelText("Senha")).toHaveAttribute("autocomplete", "current-password");

    rerender(<PasswordField label="Senha" name="password" autoComplete="new-password" />);
    expect(screen.getByLabelText("Senha")).toHaveAttribute("autocomplete", "new-password");
  });

  it("exposes the error with aria-invalid and aria-describedby", () => {
    render(
      <PasswordField
        label="Senha"
        name="password"
        autoComplete="new-password"
        error="Senha muito curta"
      />,
    );

    const input = screen.getByLabelText("Senha");
    const error = screen.getByText("Senha muito curta");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect((input.getAttribute("aria-describedby") ?? "").split(" ")).toContain(
      error.closest("[id]")?.id,
    );
  });

  it("declares a minimum 44px touch target and no external resource", () => {
    const css = styles["./PasswordField.css"];
    expect(css).toBeDefined();
    expect(css).toContain("--target-min");
    expect(css).not.toMatch(/https?:\/\/|@import/);
  });
});

describe("FormError", () => {
  it("renders the text with role alert and a decorative icon", async () => {
    const { container } = render(<FormError>Senha invalida</FormError>);

    const alert = screen.getByRole("alert");
    expect(alert).toBeInTheDocument();
    expect(alert).toHaveTextContent("Senha invalida");
    const icon = alert.querySelector("svg");
    expect(icon).not.toBeNull();
    expect(icon).toHaveAttribute("aria-hidden", "true");
    expect(container.innerHTML).not.toMatch(/https?:\/\//);
    await expectNoA11yViolations(container);
  });

  it("keeps the given id so fields can reference it", () => {
    render(<FormError id="email-error">Obrigatorio</FormError>);

    expect(screen.getByText("Obrigatorio").closest("[id]")).toHaveAttribute("id", "email-error");
  });
});

describe("ErrorSummary", () => {
  const errors = [
    { id: "email", message: "E-mail invalido" },
    { id: "password", message: "Senha muito curta" },
  ];

  it("lists the errors as links anchored to the field ids", async () => {
    const { container } = render(
      <>
        <ErrorSummary errors={errors} />
        <TextField label="E-mail" name="email" id="email" />
        <TextField label="Senha" name="password" id="password" />
      </>,
    );

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
    expect(screen.getByRole("link", { name: "E-mail invalido" })).toHaveAttribute("href", "#email");
    expect(screen.getByRole("link", { name: "Senha muito curta" })).toHaveAttribute(
      "href",
      "#password",
    );
    await expectNoA11yViolations(container);
  });

  it("is focusable programmatically (tabindex -1) and receives focus when it appears", async () => {
    render(<ErrorSummary errors={errors} />);

    const summary = screen.getByRole("alert");
    expect(summary).toHaveAttribute("tabindex", "-1");
    await waitFor(() => expect(summary).toHaveFocus());
  });

  it("focuses the corresponding field when a link is activated", async () => {
    render(
      <>
        <ErrorSummary errors={errors} />
        <TextField label="E-mail" name="email" id="email" />
        <TextField label="Senha" name="password" id="password" />
      </>,
    );

    await userEvent.click(screen.getByRole("link", { name: "Senha muito curta" }));

    expect(screen.getByLabelText("Senha")).toHaveFocus();
  });

  it("renders nothing when there are no errors", () => {
    const { container } = render(<ErrorSummary errors={[]} />);

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(container).toBeEmptyDOMElement();
  });

  it("has no external resource and uses no animation", () => {
    const { container } = render(<ErrorSummary errors={errors} />);

    expect(container.querySelector("[role='alert']")).not.toBeNull();
    expect(container.innerHTML).not.toMatch(/https?:\/\//);
    const css = styles["./ErrorSummary.css"];
    expect(css).toBeDefined();
    expect(css).not.toMatch(/animation|transition/);
  });
});
