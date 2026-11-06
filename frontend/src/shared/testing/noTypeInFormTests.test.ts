// T-157 (RED, QA-04): higiene - testes nao preenchem campos com user.type (tecla a tecla, ~30 ms por tecla,
// estoura o testTimeout de 5 s sob carga). Para preencher valores use user.paste (ou fillField).
// user.type so e permitido onde a tecla e o objeto do teste (mascara, contador, Enter, foco), e precisa
// de um comentario "// tecla: <motivo>" na linha anterior ou na mesma linha.
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

const SRC = "src";
const SELF = "noTypeInFormTests.test.ts";
const TYPE_CALL = /\b(?:user|userEvent)\.type\(/;
const JUSTIFICATION = /\/\/\s*tecla:\s*\S+/;

function testFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return testFiles(path);
    return /\.test\.(ts|tsx)$/.test(name) && name !== SELF ? [path] : [];
  });
}

function unjustifiedTypeCalls(): string[] {
  const offenders: string[] = [];
  for (const file of testFiles(SRC)) {
    const lines = readFileSync(file, "utf8").split(/\r?\n/);
    lines.forEach((line, index) => {
      if (!TYPE_CALL.test(line)) return;
      const nearby = `${lines[index - 1] ?? ""}\n${line}`;
      if (!JUSTIFICATION.test(nearby)) offenders.push(`${file}:${index + 1}`);
    });
  }
  return offenders;
}

describe("higiene dos testes (QA-04)", () => {
  it("todo user.type tem a justificativa '// tecla: <motivo>'; valores se preenchem com user.paste", () => {
    expect(unjustifiedTypeCalls()).toEqual([]);
  });
});
