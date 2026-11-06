"""Testes do portao de cobertura do backend (check_coverage.py).

Rodar a partir da raiz do repositorio:
    python -m unittest discover -s scripts/ci -p "test_*.py"
"""

import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import check_coverage as cc  # noqa: E402


def cobertura(*packages):
    """Monta um cobertura minimo. Cada pacote: (nome, [(arquivo, [(linha, hits), ...]), ...])."""
    parts = ['<?xml version="1.0" encoding="utf-8"?>', "<coverage><packages>"]
    for name, classes in packages:
        parts.append(f'<package name="{name}"><classes>')
        for filename, lines in classes:
            parts.append(f'<class name="C" filename="{filename}"><lines>')
            for number, hits in lines:
                parts.append(f'<line number="{number}" hits="{hits}" branch="False"/>')
            parts.append("</lines></class>")
        parts.append("</classes></package>")
    parts.append("</packages></coverage>")
    return "".join(parts)


PRODUCT = [
    "Deckloom.Accounts.Application",
    "Deckloom.SharedKernel",
]


class CoverageGateTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = self._tmp.name

    def tearDown(self):
        self._tmp.cleanup()

    def write(self, name, content):
        path = os.path.join(self.dir, name)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(content)
        return path

    def run_gate(self, threshold=80.0, product=PRODUCT):
        return cc.run(self.dir, threshold, product)

    def test_passes_when_every_product_package_is_at_or_above_threshold(self):
        self.write(
            "a.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("A.cs", [(1, 1), (2, 1), (3, 1), (4, 1), (5, 0)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1), (2, 1), (3, 1), (4, 1), (5, 1)])]),
            ),
        )
        result = self.run_gate()
        self.assertTrue(result.ok, result.messages)

    def test_fails_and_names_the_package_below_threshold(self):
        self.write(
            "a.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("A.cs", [(1, 1), (2, 0), (3, 0), (4, 0), (5, 0)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])]),
            ),
        )
        result = self.run_gate()
        self.assertFalse(result.ok)
        self.assertTrue(any("Deckloom.Accounts.Application" in m for m in result.messages))

    def test_boundary_exactly_80_passes_and_just_below_fails(self):
        at_80 = [(n, 1 if n <= 8 else 0) for n in range(1, 11)]  # 8/10 = 80.0%
        below = [(n, 1 if n <= 7 else 0) for n in range(1, 11)]  # 7/10 = 70.0%
        self.write("a.xml", cobertura(("Deckloom.Accounts.Application", [("A.cs", at_80)]),
                                      ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])])))
        self.assertTrue(self.run_gate(threshold=80.0).ok)
        os.remove(os.path.join(self.dir, "a.xml"))
        self.write("a.xml", cobertura(("Deckloom.Accounts.Application", [("A.cs", below)]),
                                      ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])])))
        self.assertFalse(self.run_gate(threshold=80.0).ok)

    def test_same_line_in_two_reports_counts_once_and_is_covered_if_any_report_hits_it(self):
        # Dois projetos de teste medem o mesmo arquivo de produto: a linha 2 so e coberta num deles.
        self.write(
            "one.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("A.cs", [(1, 1), (2, 0)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])]),
            ),
        )
        self.write(
            "two.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("A.cs", [(1, 1), (2, 1)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])]),
            ),
        )
        result = self.run_gate()
        self.assertTrue(result.ok, result.messages)
        stats = cc.aggregate(cc.load_reports(self.dir))
        self.assertEqual(stats["Deckloom.Accounts.Application"], (2, 2))

    def test_same_file_under_different_path_prefixes_counts_once(self):
        # Coverlet grava o mesmo arquivo como "src/Modules/..." em um relatorio e "Modules/..." em outro.
        self.write(
            "one.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("src/Modules/Accounts/Deckloom.Accounts.Application/A.cs", [(1, 1), (2, 0)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])]),
            ),
        )
        self.write(
            "two.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("Modules/Accounts/Deckloom.Accounts.Application/A.cs", [(1, 0), (2, 1)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])]),
            ),
        )
        stats = cc.aggregate(cc.load_reports(self.dir))
        self.assertEqual(stats["Deckloom.Accounts.Application"], (2, 2))
        self.assertTrue(self.run_gate().ok)

    def test_missing_product_package_fails_because_it_was_not_measured(self):
        self.write("a.xml", cobertura(("Deckloom.Accounts.Application", [("A.cs", [(1, 1)])])))
        result = self.run_gate()
        self.assertFalse(result.ok)
        self.assertTrue(any("Deckloom.SharedKernel" in m for m in result.messages))

    def test_unclassified_package_fails_so_a_new_product_project_cannot_skip_the_gate(self):
        self.write(
            "a.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("A.cs", [(1, 1)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])]),
                ("Deckloom.Novo.Projeto", [("N.cs", [(1, 0)])]),
            ),
        )
        result = self.run_gate()
        self.assertFalse(result.ok)
        self.assertTrue(any("Deckloom.Novo.Projeto" in m for m in result.messages))

    def test_test_support_packages_are_ignored(self):
        self.write(
            "a.xml",
            cobertura(
                ("Deckloom.Accounts.Application", [("A.cs", [(1, 1)])]),
                ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])]),
                ("Deckloom.Accounts.Testing", [("T.cs", [(1, 0)])]),
                ("Deckloom.Api.E2E.Host", [("E.cs", [(1, 0)])]),
            ),
        )
        self.assertTrue(self.run_gate().ok)

    def test_no_reports_fails(self):
        result = self.run_gate()
        self.assertFalse(result.ok)

    def test_main_returns_exit_code_one_below_threshold(self):
        self.write("a.xml", cobertura(("Deckloom.Accounts.Application", [("A.cs", [(1, 0)])]),
                                      ("Deckloom.SharedKernel", [("S.cs", [(1, 1)])])))
        code = cc.main(["--reports-dir", self.dir, "--threshold", "80"])
        self.assertEqual(code, 1)


if __name__ == "__main__":
    unittest.main()
