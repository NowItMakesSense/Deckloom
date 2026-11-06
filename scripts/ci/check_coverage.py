"""Portao de cobertura do backend (CI).

Le os relatorios cobertura gerados pelo coletor XPlat Code Coverage (coverlet) e falha
se algum pacote de PRODUTO ficar abaixo do limite. Regras:

- Pacotes de produto (PRODUCT_PACKAGES) precisam aparecer nos relatorios e ter cobertura
  de linhas >= limite. Produto ausente conta como falha (nada medido, nada aprovado).
- Pacotes de apoio a teste (TEST_SUPPORT_PACKAGES) e nomes de teste (*.Tests, *.Testing,
  *.E2E.Host) sao ignorados.
- Pacote nao classificado falha: um projeto novo precisa entrar em uma das listas.
- A mesma linha medida por varios projetos de teste conta uma vez; coberta se algum
  relatorio a cobriu (uniao, como o ReportGenerator faz).
- Codigo gerado (Migrations, *.g.cs, obj/) ja e excluido por backend/coverlet.runsettings (QA-07).

Uso (na raiz do repositorio):
    python scripts/ci/check_coverage.py --reports-dir backend/TestResults --threshold 80
"""

import argparse
import os
import sys
import xml.etree.ElementTree as ET

PRODUCT_PACKAGES = (
    "Deckloom.Accounts.Application",
    "Deckloom.Accounts.Domain",
    "Deckloom.Accounts.Infrastructure",
    "Deckloom.Api",
    "Deckloom.DeckSuggestion.Infrastructure",
    "Deckloom.SharedKernel",
)

TEST_SUPPORT_PACKAGES = (
    "Deckloom.Accounts.Testing",
    "Deckloom.Api.E2E.Host",
)

TEST_SUFFIXES = (".Tests", ".Testing", ".E2E.Host", "ArchitectureTests")


class GateResult:
    def __init__(self, ok, messages, table):
        self.ok = ok
        self.messages = messages
        self.table = table


def _is_test_support(package):
    return package in TEST_SUPPORT_PACKAGES or package.endswith(TEST_SUFFIXES)


def normalize_file(package, filename):
    """Caminho do arquivo relativo a pasta do pacote.

    O coletor grava o mesmo arquivo como "src/Modules/.../Pacote/A.cs" num relatorio e
    "Modules/.../Pacote/A.cs" noutro (depende do projeto de teste). Sem normalizar, a
    mesma linha entraria duas vezes na uniao, uma delas sempre descoberta.
    """
    parts = [p for p in filename.replace("\\", "/").split("/") if p not in ("", ".")]
    if package in parts:
        last = len(parts) - 1 - parts[::-1].index(package)
        rest = parts[last + 1:]
        if rest:
            return "/".join(rest)
    while parts and parts[0] == "src":
        parts = parts[1:]
    return "/".join(parts)


def load_reports(reports_dir):
    """Une as linhas de todos os *.xml cobertura sob reports_dir.

    Devolve {(pacote, arquivo normalizado, linha): coberta?}. Coberta se algum relatorio tiver hits > 0.
    """
    merged = {}
    for root_dir, _dirs, files in os.walk(reports_dir):
        for name in files:
            if not name.lower().endswith(".xml"):
                continue
            tree = ET.parse(os.path.join(root_dir, name))
            for package in tree.getroot().iter("package"):
                package_name = package.get("name", "")
                for klass in package.iter("class"):
                    filename = normalize_file(package_name, klass.get("filename", ""))
                    for line in klass.iter("line"):
                        key = (package_name, filename, line.get("number", ""))
                        covered = int(line.get("hits", "0")) > 0
                        merged[key] = merged.get(key, False) or covered
    return merged


def aggregate(line_map):
    """Devolve {pacote: (linhas_validas, linhas_cobertas)}."""
    stats = {}
    for (package, _filename, _number), covered in line_map.items():
        valid, hit = stats.get(package, (0, 0))
        stats[package] = (valid + 1, hit + (1 if covered else 0))
    return stats


def run(reports_dir, threshold, product_packages=PRODUCT_PACKAGES):
    line_map = load_reports(reports_dir)
    if not line_map:
        return GateResult(False, [f"Nenhum relatorio de cobertura encontrado em {reports_dir}."], [])

    stats = aggregate(line_map)
    messages = []
    table = []

    for package in sorted(stats):
        if package not in product_packages and not _is_test_support(package):
            messages.append(
                f"Pacote sem classificacao: {package}. Inclua em PRODUCT_PACKAGES ou TEST_SUPPORT_PACKAGES."
            )

    for package in product_packages:
        if package not in stats:
            messages.append(f"Pacote de produto sem medicao: {package}.")
            continue
        valid, hit = stats[package]
        percent = (hit * 100.0 / valid) if valid else 0.0
        table.append(f"{package}: {percent:.2f}% ({hit}/{valid} linhas)")
        # Comparacao exata em inteiros: 8/10 com limite 80 passa; 79/100 falha.
        if valid == 0 or hit * 100 < threshold * valid:
            messages.append(f"{package} abaixo do limite de {threshold:g}%: {percent:.2f}%.")

    return GateResult(not messages, messages, table)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Portao de cobertura de linhas dos projetos de produto.")
    parser.add_argument("--reports-dir", default="backend/TestResults")
    parser.add_argument("--threshold", type=float, default=80.0)
    args = parser.parse_args(argv)

    result = run(args.reports_dir, args.threshold)
    for line in result.table:
        print(line)
    for message in result.messages:
        print(f"FALHA: {message}", file=sys.stderr)
    if result.ok:
        print(f"Cobertura de produto >= {args.threshold:g}%: OK.")
        return 0
    return 1


if __name__ == "__main__":
    sys.exit(main())
