import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";

import { expectNoA11yViolations } from "../shared/testing/expectNoA11yViolations";
import { Checkbox } from "./Checkbox";
import { CheckboxGroup } from "./CheckboxGroup";
import { RadioGroup } from "./RadioGroup";
import { Select } from "./Select";
import { Textarea } from "./Textarea";

// T-098 (habilitadora): A11Y-24, A11Y-25, A11Y-27, A11Y-28, REQ-015 (grapheme);
// WC-15, WC-18, MOT-15, ART-05.

const styles = import.meta.glob("./*.css", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>;

const FAMILY = "\u{1F468}‍\u{1F469}‍\u{1F467}";

const formats = [
  { value: "commander", label: "Commander" },
  { value: "modern", label: "Modern" },
  { value: "standard", label: "Standard" },
];

function describedByText(el: HTMLElement): string {
  return (el.getAttribute("aria-describedby") ?? "")
    .split(" ")
    .filter(Boolean)
    .map((id) => document.getElementById(id)?.textContent ?? "")
    .join(" ");
}

describe("Checkbox", () => {
  it("renders a native checkbox with a clickable associated label that toggles it", async () => {
    const user = userEvent.setup();
    const { container } = render(<Checkbox name="terms" label="Aceito os termos" />);

    const box = screen.getByRole("checkbox", { name: "Aceito os termos" });
    expect(box.tagName).toBe("INPUT");
    expect(box).not.toBeChecked();
    await user.click(screen.getByText("Aceito os termos"));
    expect(box).toBeChecked();
    await user.click(screen.getByText("Aceito os termos"));
    expect(box).not.toBeChecked();
    await expectNoA11yViolations(container);
  });

  it("toggles by keyboard with Space", async () => {
    const user = userEvent.setup();
    render(<Checkbox name="terms" label="Aceito os termos" />);

    await user.tab();
    expect(screen.getByRole("checkbox")).toHaveFocus();
    await user.keyboard(" ");
    expect(screen.getByRole("checkbox")).toBeChecked();
  });

  it("does not change state when a link inside the label is activated (A11Y-24/25)", async () => {
    const user = userEvent.setup();
    render(
      <Checkbox
        name="terms"
        label={
          <>
            Aceito os <a href="/termos">Termos de uso (abre em nova aba)</a>
          </>
        }
      />,
    );

    const link = screen.getByRole("link", { name: /Termos de uso/ });
    link.addEventListener("click", (e) => e.preventDefault());
    await user.click(link);

    expect(screen.getByRole("checkbox")).not.toBeChecked();
  });

  it("honors defaultChecked", () => {
    render(<Checkbox name="terms" label="Aceito" defaultChecked />);
    expect(screen.getByRole("checkbox")).toBeChecked();
  });

  it("declares a 44px touch target, wrapping labels and instant change (WC-15, MOT-15)", () => {
    const css = styles["./Checkbox.css"];
    expect(css).toBeDefined();
    expect(css).toContain("--target-min");
    expect(css).toMatch(/overflow-wrap:\s*anywhere/);
    expect(css).not.toMatch(/animation|transition/);
    expect(css).not.toMatch(/https?:\/\/|@import/);
  });

  it("rotulo com links corre como texto, sem virar colunas (rotulo dos Termos no cadastro)", () => {
    const label = styles["./Checkbox.css"].match(/\.checkbox__label\s*\{([^}]*)\}/)?.[1] ?? "";
    expect(label).not.toMatch(/display:\s*(inline-)?flex/);
    expect(label).toMatch(/display:\s*block/);
  });
});

describe("CheckboxGroup", () => {
  it("renders a fieldset with legend and one checkbox per option (A11Y-27)", async () => {
    const { container } = render(
      <CheckboxGroup legend="Formatos preferidos" name="formats" options={formats} />,
    );

    const group = screen.getByRole("group", { name: "Formatos preferidos" });
    expect(group.tagName).toBe("FIELDSET");
    expect(group.querySelector("legend")?.textContent).toBe("Formatos preferidos");
    expect(screen.getAllByRole("checkbox")).toHaveLength(3);
    expect(screen.getByRole("checkbox", { name: "Modern" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });

  it("allows several selections and honors defaultValue", async () => {
    const user = userEvent.setup();
    render(
      <CheckboxGroup
        legend="Formatos preferidos"
        name="formats"
        options={formats}
        defaultValue={["modern"]}
      />,
    );

    expect(screen.getByRole("checkbox", { name: "Modern" })).toBeChecked();
    await user.click(screen.getByRole("checkbox", { name: "Commander" }));
    expect(screen.getByRole("checkbox", { name: "Commander" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Modern" })).toBeChecked();
  });

  it("associates the group error through aria-describedby on the fieldset", async () => {
    const { container } = render(
      <CheckboxGroup
        legend="Formatos preferidos"
        name="formats"
        options={formats}
        error="Escolha ao menos um formato"
      />,
    );

    const group = screen.getByRole("group", { name: "Formatos preferidos" });
    expect(screen.getByText("Escolha ao menos um formato")).toBeInTheDocument();
    expect(describedByText(group)).toContain("Escolha ao menos um formato");
    await expectNoA11yViolations(container);
  });

  it("shows the empty text instead of an empty group (A11Y-27)", () => {
    render(
      <CheckboxGroup
        legend="Formatos preferidos"
        name="formats"
        options={[]}
        emptyText="Nenhum formato disponível no momento."
      />,
    );

    expect(screen.getByText("Nenhum formato disponível no momento.")).toBeInTheDocument();
    expect(screen.queryAllByRole("checkbox")).toHaveLength(0);
  });
});

describe("RadioGroup", () => {
  it("renders a fieldset with legend and a labelled radio per option", async () => {
    const { container } = render(<RadioGroup legend="Formato" name="format" options={formats} />);

    const fieldset = document.querySelector("fieldset");
    expect(fieldset).not.toBeNull();
    expect(fieldset?.querySelector("legend")?.textContent).toBe("Formato");
    expect(screen.getByRole("radiogroup", { name: "Formato" })).toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(3);
    expect(screen.getByRole("radio", { name: "Standard" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });

  it("keeps a single selection", async () => {
    const user = userEvent.setup();
    render(<RadioGroup legend="Formato" name="format" options={formats} />);

    await user.click(screen.getByRole("radio", { name: "Commander" }));
    await user.click(screen.getByRole("radio", { name: "Modern" }));

    expect(screen.getByRole("radio", { name: "Modern" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Commander" })).not.toBeChecked();
  });

  it("moves the selection with arrow keys", async () => {
    const user = userEvent.setup();
    render(
      <RadioGroup legend="Formato" name="format" options={formats} defaultValue="commander" />,
    );

    await user.tab();
    expect(screen.getByRole("radio", { name: "Commander" })).toHaveFocus();
    await user.keyboard("{ArrowDown}");
    expect(screen.getByRole("radio", { name: "Modern" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Modern" })).toHaveFocus();
    await user.keyboard("{ArrowUp}");
    expect(screen.getByRole("radio", { name: "Commander" })).toBeChecked();
  });

  it("associates the error with the group", () => {
    render(
      <RadioGroup legend="Formato" name="format" options={formats} error="Escolha um formato" />,
    );

    const fieldset = document.querySelector("fieldset") as HTMLElement;
    expect(fieldset).not.toBeNull();
    expect(describedByText(fieldset)).toContain("Escolha um formato");
  });

  it("shows the empty text without options", () => {
    render(<RadioGroup legend="Formato" name="format" options={[]} emptyText="Nenhum formato." />);

    expect(screen.getByText("Nenhum formato.")).toBeInTheDocument();
    expect(screen.queryAllByRole("radio")).toHaveLength(0);
  });
});

describe("Select", () => {
  it("associates the label with a native select and lists the options", async () => {
    const { container } = render(<Select label="TCG favorito" name="tcg" options={formats} />);

    const select = screen.getByLabelText("TCG favorito");
    expect(select.tagName).toBe("SELECT");
    expect(screen.getByText("TCG favorito").tagName).toBe("LABEL");
    expect(screen.getAllByRole("option")).toHaveLength(3);
    expect(screen.getByRole("option", { name: "Modern" })).toBeInTheDocument();
    await expectNoA11yViolations(container);
  });

  it("changes the value", async () => {
    const user = userEvent.setup();
    render(<Select label="TCG favorito" name="tcg" options={formats} />);

    await user.selectOptions(screen.getByLabelText("TCG favorito"), "modern");
    expect(screen.getByLabelText("TCG favorito")).toHaveValue("modern");
  });

  it("marks aria-invalid and links the error with aria-describedby", async () => {
    const { container } = render(
      <Select label="TCG favorito" name="tcg" options={formats} error="Escolha uma opção" />,
    );

    const select = screen.getByLabelText("TCG favorito");
    expect(select).toHaveAttribute("aria-invalid", "true");
    expect(describedByText(select)).toContain("Escolha uma opção");
    await expectNoA11yViolations(container);
  });

  it("has no aria-invalid without an error", () => {
    render(<Select label="TCG favorito" name="tcg" options={formats} />);
    expect(screen.getByLabelText("TCG favorito")).not.toHaveAttribute("aria-invalid");
  });
});

describe("Textarea com contador", () => {
  it("associates the label and shows the counter 'N de 280' linked by aria-describedby", async () => {
    const { container } = render(<Textarea label="Bio" name="bio" />);

    const area = screen.getByLabelText("Bio");
    expect(area.tagName).toBe("TEXTAREA");
    expect(describedByText(area)).toContain("0 de 280");
    await expectNoA11yViolations(container);
  });

  it("updates the counter while typing, counting grapheme clusters (REQ-015)", async () => {
    const user = userEvent.setup();
    render(<Textarea label="Bio" name="bio" />);

    const area = screen.getByLabelText("Bio");
    // tecla: o contador acompanha a digitacao e conta grafemas (emoji composto)
    await user.type(area, `ab${FAMILY}`);

    expect(describedByText(area)).toContain("3 de 280");
  });

  it("counts the initial value", () => {
    render(<Textarea label="Bio" name="bio" defaultValue={`oi${FAMILY}`} />);
    expect(describedByText(screen.getByLabelText("Bio"))).toContain("3 de 280");
  });

  it("does not announce on every keystroke (A11Y-28)", async () => {
    const user = userEvent.setup();
    const { container } = render(<Textarea label="Bio" name="bio" />);

    // tecla: o objeto e que nao ha anuncio a cada tecla digitada
    await user.type(screen.getByLabelText("Bio"), "abc");

    expect(container.querySelector("[aria-live='assertive']")).toBeNull();
    expect(container.querySelector("[role='alert']")).toBeNull();
    const counter = screen.getByText("3 de 280");
    expect(counter.closest("[aria-live]")).toBeNull();
    expect(counter.closest("[role='status']")).toBeNull();
  });

  it("does not block typing above the limit and shows '281 de 280' as an error", async () => {
    const user = userEvent.setup();
    render(<Textarea label="Bio" name="bio" defaultValue={"a".repeat(280)} />);

    const area = screen.getByLabelText("Bio");
    expect(area).not.toHaveAttribute("maxlength");
    expect(area).not.toHaveAttribute("aria-invalid");
    // tecla: o objeto e a digitacao acima do limite, que nao e bloqueada
    await user.type(area, "b");

    expect(area).toHaveValue("a".repeat(280) + "b");
    expect(area).toHaveAttribute("aria-invalid", "true");
    const message = screen.getByText("281 de 280");
    expect(message.closest("[role='alert']")).not.toBeNull();
    expect(describedByText(area)).toContain("281 de 280");
  });

  it("formats numbers in pt-BR and never renders NaN or undefined (WC-18)", () => {
    const { container, rerender } = render(
      <Textarea label="Bio" name="bio" limit={1500} defaultValue={"a".repeat(1200)} />,
    );
    expect(describedByText(screen.getByLabelText("Bio"))).toContain("1.200 de 1.500");
    expect(container.textContent).not.toMatch(/NaN|undefined/);

    rerender(<Textarea label="Bio" name="bio" />);
    expect(container.textContent).not.toMatch(/NaN|undefined/);
  });

  it("shows an external error with aria-invalid", () => {
    render(<Textarea label="Bio" name="bio" error="Bio inválida" />);

    const area = screen.getByLabelText("Bio");
    expect(area).toHaveAttribute("aria-invalid", "true");
    expect(describedByText(area)).toContain("Bio inválida");
  });

  it("uses tabular-nums on the counter and no animation (ART-05, MOT-15)", () => {
    const css = styles["./Textarea.css"];
    expect(css).toBeDefined();
    expect(css).toContain("tabular-nums");
    expect(css).toContain("--target-min");
    expect(css).not.toMatch(/animation|transition/);
    expect(css).not.toMatch(/https?:\/\/|@import/);
  });
});
