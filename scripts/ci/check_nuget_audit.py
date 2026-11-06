"""Auditoria de pacotes NuGet vulneraveis (CI).

`dotnet list ... package --vulnerable` sai com codigo 0 mesmo quando acha vulnerabilidade,
e a saida em texto e localizada (pt-BR, en-US). Por isso o CI roda com `--format json` e
este script falha se houver qualquer `vulnerabilities` nao vazio ou `problems` (erro de
restauracao). JSON invalido tambem falha (fechado por padrao).

Uso (na raiz do repositorio, depois do restore):
    dotnet list backend/Deckloom.sln package --vulnerable --include-transitive --format json > nuget-audit.json
    python scripts/ci/check_nuget_audit.py nuget-audit.json
"""

import json
import sys


def _walk(node, path):
    if isinstance(node, dict):
        for key, value in node.items():
            if key == "vulnerabilities" and isinstance(value, list) and value:
                owner = node.get("id", "?")
                version = node.get("resolvedVersion", "?")
                yield f"{path} -> {owner} {version}: {len(value)} vulnerabilidade(s)"
            else:
                yield from _walk(value, f"{path}/{key}")
    elif isinstance(node, list):
        for index, item in enumerate(node):
            yield from _walk(item, f"{path}[{index}]")


def find_findings(document):
    """Lista achados legiveis; lista vazia significa auditoria limpa."""
    findings = list(_walk(document, "$"))
    problems = document.get("problems") if isinstance(document, dict) else None
    if problems:
        findings.append(f"Problemas de restauracao na auditoria: {len(problems)}")
    return findings


def main_from_text(text):
    try:
        document = json.loads(text)
    except json.JSONDecodeError as error:
        print(f"FALHA: saida da auditoria NuGet nao e JSON valido: {error}", file=sys.stderr)
        return 1
    findings = find_findings(document)
    for finding in findings:
        print(f"FALHA: {finding}", file=sys.stderr)
    if findings:
        return 1
    print("Auditoria NuGet: nenhum pacote vulneravel.")
    return 0


def main(argv):
    if len(argv) != 2:
        print("Uso: check_nuget_audit.py <arquivo.json>", file=sys.stderr)
        return 2
    with open(argv[1], encoding="utf-8") as handle:
        return main_from_text(handle.read())


if __name__ == "__main__":
    sys.exit(main(sys.argv))
