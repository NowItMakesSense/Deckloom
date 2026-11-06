import axe from "axe-core";

export async function expectNoA11yViolations(container: HTMLElement): Promise<void> {
  const { violations } = await axe.run(container);
  if (violations.length === 0) {
    return;
  }
  const report = violations
    .map((v) => `${v.id} (${v.impact ?? "n/a"}): ${v.help} [${v.nodes.length} no(s)]`)
    .join("\n");
  throw new Error(`Violacoes de acessibilidade (axe):\n${report}`);
}
