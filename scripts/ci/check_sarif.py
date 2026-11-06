"""Portao de achados do CodeQL (CI).

O CI roda o CodeQL sem upload (repositorio privado, sem Code Scanning na conta). O SARIF fica
so como artefato, e este script decide o job: falha se houver qualquer resultado ativo em
qualquer execucao. Regras (fechado por padrao):

- Resultado com `suppressions` nao vazio nao conta; e listado na saida (regra, arquivo e linha), nunca escondido.
- Diretorio sem *.sarif, caminho ausente, JSON invalido, documento sem `runs` (lista nao
  vazia) ou execucao sem `results` (lista) falham: nada medido nao e aprovado.
- Resultado sem regra, arquivo ou linha ainda falha; o campo ausente aparece como "?".

Uso (na raiz do repositorio):
    python scripts/ci/check_sarif.py <diretorio-ou-arquivo.sarif>
"""

import json
import os
import sys
from dataclasses import dataclass, field

UNKNOWN = "?"


class SarifError(Exception):
    """Entrada que nao pode ser lida como SARIF valido: o portao falha."""


@dataclass(frozen=True)
class Finding:
    rule: str
    file: str
    line: str
    message: str

    def render(self):
        return f"{self.rule} em {self.file}:{self.line}: {self.message}"


@dataclass
class Report:
    findings: list = field(default_factory=list)
    suppressed_findings: list = field(default_factory=list)
    files: int = 0

    @property
    def suppressed(self):
        return len(self.suppressed_findings)


def _obj(value):
    return value if isinstance(value, dict) else {}


def read_documents(path):
    """Devolve [(origem, documento)] para um arquivo ou para os *.sarif de um diretorio."""
    if os.path.isdir(path):
        names = sorted(n for n in os.listdir(path) if n.lower().endswith(".sarif"))
        if not names:
            raise SarifError(f"nenhum arquivo .sarif em {path}")
        sources = [os.path.join(path, n) for n in names]
    elif os.path.isfile(path):
        sources = [path]
    else:
        raise SarifError(f"caminho ausente: {path}")

    documents = []
    for source in sources:
        try:
            with open(source, encoding="utf-8") as handle:
                documents.append((source, json.load(handle)))
        except (OSError, ValueError) as error:
            raise SarifError(f"SARIF invalido em {source}: {error}")
    return documents


def _is_suppressed(result):
    suppressions = result.get("suppressions")
    return isinstance(suppressions, list) and len(suppressions) > 0


def _rule_id(run, result):
    rule_id = result.get("ruleId")
    if isinstance(rule_id, str) and rule_id:
        return rule_id
    index = result.get("ruleIndex")
    driver = _obj(_obj(run.get("tool")).get("driver"))
    rules = driver.get("rules")
    if isinstance(rules, list) and isinstance(index, int) and 0 <= index < len(rules):
        return str(_obj(rules[index]).get("id", UNKNOWN))
    return UNKNOWN


def _location(result):
    locations = result.get("locations")
    if not isinstance(locations, list) or not locations:
        return UNKNOWN, UNKNOWN
    physical = _obj(_obj(locations[0]).get("physicalLocation"))
    uri = _obj(physical.get("artifactLocation")).get("uri")
    start_line = _obj(physical.get("region")).get("startLine")
    file_name = uri if isinstance(uri, str) and uri else UNKNOWN
    line = str(start_line) if start_line is not None else UNKNOWN
    return file_name, line


def _message(result):
    text = _obj(result.get("message")).get("text")
    return text if isinstance(text, str) else ""


def analyze(document, source="<sarif>"):
    """Conta os resultados ativos de um documento SARIF. Levanta SarifError se a forma for invalida."""
    runs = document.get("runs") if isinstance(document, dict) else None
    if not isinstance(runs, list) or not runs:
        raise SarifError(f"{source}: documento SARIF sem 'runs' (lista nao vazia)")

    report = Report(files=1)
    for run in runs:
        if not isinstance(run, dict) or not isinstance(run.get("results"), list):
            raise SarifError(f"{source}: execucao sem lista 'results'")
        for result in run["results"]:
            if not isinstance(result, dict):
                raise SarifError(f"{source}: resultado que nao e objeto")
            file_name, line = _location(result)
            finding = Finding(_rule_id(run, result), file_name, line, _message(result))
            if _is_suppressed(result):
                report.suppressed_findings.append(finding)
            else:
                report.findings.append(finding)
    return report


def check_path(path):
    """Junta os resultados de todos os documentos lidos de path."""
    total = Report()
    for source, document in read_documents(path):
        report = analyze(document, source)
        total.findings.extend(report.findings)
        total.suppressed_findings.extend(report.suppressed_findings)
        total.files += report.files
    return total


def main(argv):
    if len(argv) != 2:
        print("Uso: check_sarif.py <diretorio-ou-arquivo.sarif>", file=sys.stderr)
        return 2
    try:
        report = check_path(argv[1])
    except SarifError as error:
        print(f"FALHA: {error}", file=sys.stderr)
        return 1

    print(
        f"CodeQL: {report.files} arquivo(s) SARIF, {len(report.findings)} resultado(s) ativo(s), "
        f"{report.suppressed} suprimido(s) ignorado(s)."
    )
    for finding in report.suppressed_findings:
        print(f"SUPRIMIDO: {finding.render()}")
    for finding in report.findings:
        print(f"FALHA: CodeQL {finding.render()}", file=sys.stderr)
    return 1 if report.findings else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
