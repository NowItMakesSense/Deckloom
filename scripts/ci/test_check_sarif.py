"""Testes de check_sarif.py (portao de achados do CodeQL, SARIF local sem upload).

Rodar a partir da raiz do repositorio:
    python -m unittest discover -s scripts/ci -p "test_*.py"
"""

import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import check_sarif as sarif  # noqa: E402


def make_sarif(results, rules=None):
    return {
        "version": "2.1.0",
        "runs": [
            {
                "tool": {"driver": {"name": "CodeQL", "rules": rules or []}},
                "results": results,
            }
        ],
    }


def make_result(
    rule="cs/example-rule",
    uri="backend/src/Deckloom.Api/Program.cs",
    line=42,
    message="Achado de teste",
    suppressions=None,
):
    item = {
        "ruleId": rule,
        "level": "warning",
        "message": {"text": message},
        "locations": [
            {
                "physicalLocation": {
                    "artifactLocation": {"uri": uri},
                    "region": {"startLine": line},
                }
            }
        ],
    }
    if suppressions is not None:
        item["suppressions"] = suppressions
    return item


class CheckSarifTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = self._tmp.name

    def write_file(self, name, content):
        path = os.path.join(self.root, name)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(content if isinstance(content, str) else json.dumps(content))
        return path

    def run_main(self, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = sarif.main(["check_sarif.py", *args])
        return code, out.getvalue(), err.getvalue()

    # ------------------------------------------------ SARIF sem achado ativo

    def test_sarif_without_results_passes(self):
        self.write_file("csharp.sarif", make_sarif([]))
        code, _out, _err = self.run_main(self.root)
        self.assertEqual(code, 0)

    def test_single_clean_file_argument_passes(self):
        path = self.write_file("javascript-typescript.sarif", make_sarif([]))
        code, _out, _err = self.run_main(path)
        self.assertEqual(code, 0)

    # ------------------------------------------------ resultado ativo falha

    def test_result_fails_and_prints_rule_file_and_line(self):
        self.write_file(
            "csharp.sarif",
            make_sarif([make_result(rule="cs/sql-injection", uri="backend/src/Deckloom.Api/X.cs", line=17)]),
        )
        code, _out, err = self.run_main(self.root)
        self.assertEqual(code, 1)
        self.assertIn("cs/sql-injection", err)
        self.assertIn("backend/src/Deckloom.Api/X.cs", err)
        self.assertIn("17", err)

    def test_finding_in_one_of_several_files_fails(self):
        self.write_file("csharp.sarif", make_sarif([]))
        self.write_file("javascript-typescript.sarif", make_sarif([make_result(rule="js/xss")]))
        code, _out, err = self.run_main(self.root)
        self.assertEqual(code, 1)
        self.assertIn("js/xss", err)

    def test_rule_falls_back_to_rule_index_when_rule_id_is_missing(self):
        result = make_result()
        del result["ruleId"]
        result["ruleIndex"] = 0
        self.write_file("csharp.sarif", make_sarif([result], rules=[{"id": "cs/from-index"}]))
        report = sarif.check_path(self.root)
        self.assertEqual([f.rule for f in report.findings], ["cs/from-index"])

    def test_result_without_location_still_counts_as_finding(self):
        result = make_result()
        del result["locations"]
        self.write_file("csharp.sarif", make_sarif([result]))
        report = sarif.check_path(self.root)
        self.assertEqual(len(report.findings), 1)
        self.assertEqual(report.findings[0].line, "?")

    # ------------------------------------------------ suprimido nao conta

    def test_suppressed_result_does_not_count(self):
        self.write_file(
            "csharp.sarif",
            make_sarif([make_result(suppressions=[{"kind": "inSource"}])]),
        )
        report = sarif.check_path(self.root)
        self.assertEqual(report.findings, [])
        self.assertEqual(report.suppressed, 1)
        code, _out, _err = self.run_main(self.root)
        self.assertEqual(code, 0)

    def test_each_suppressed_result_is_printed_with_rule_file_and_line(self):
        self.write_file(
            "csharp.sarif",
            make_sarif(
                [
                    make_result(rule="cs/suprimido-a", uri="backend/src/Deckloom.Api/A.cs", line=9, suppressions=[{"kind": "inSource"}]),
                    make_result(rule="cs/suprimido-b", uri="backend/src/Deckloom.Api/B.cs", line=77, suppressions=[{"kind": "external"}]),
                    make_result(rule="cs/ativo", uri="backend/src/Deckloom.Api/C.cs", line=3),
                ]
            ),
        )
        code, out, err = self.run_main(self.root)
        self.assertEqual(code, 1)
        lines = out.splitlines()
        suppressed_a = [line for line in lines if "cs/suprimido-a" in line]
        suppressed_b = [line for line in lines if "cs/suprimido-b" in line]
        self.assertEqual(len(suppressed_a), 1, out)
        self.assertIn("backend/src/Deckloom.Api/A.cs:9", suppressed_a[0])
        self.assertEqual(len(suppressed_b), 1, out)
        self.assertIn("backend/src/Deckloom.Api/B.cs:77", suppressed_b[0])
        self.assertNotIn("cs/ativo", out, "resultado ativo nao deve sair na lista de suprimidos")
        self.assertNotIn("cs/suprimido", err)

    def test_empty_suppressions_list_is_not_a_suppression(self):
        self.write_file("csharp.sarif", make_sarif([make_result(rule="cs/ativo", suppressions=[])]))
        report = sarif.check_path(self.root)
        self.assertEqual([f.rule for f in report.findings], ["cs/ativo"])
        self.assertEqual(report.suppressed, 0)

    def test_only_the_active_result_is_reported_next_to_a_suppressed_one(self):
        self.write_file(
            "csharp.sarif",
            make_sarif(
                [
                    make_result(rule="cs/suprimido", suppressions=[{"kind": "external"}]),
                    make_result(rule="cs/ativo"),
                ]
            ),
        )
        report = sarif.check_path(self.root)
        self.assertEqual([f.rule for f in report.findings], ["cs/ativo"])
        self.assertEqual(report.suppressed, 1)

    # ------------------------------------------------ nunca passar em silencio

    def test_empty_directory_fails(self):
        code, _out, err = self.run_main(self.root)
        self.assertEqual(code, 1)
        self.assertIn("nenhum arquivo .sarif", err)

    def test_directory_with_only_non_sarif_files_fails(self):
        self.write_file("notes.txt", "nada aqui")
        code, _out, _err = self.run_main(self.root)
        self.assertEqual(code, 1)

    def test_missing_path_fails(self):
        code, _out, err = self.run_main(os.path.join(self.root, "nao-existe"))
        self.assertEqual(code, 1)
        self.assertIn("caminho ausente", err)

    def test_invalid_json_fails(self):
        self.write_file("csharp.sarif", "{ isto nao e json")
        code, _out, err = self.run_main(self.root)
        self.assertEqual(code, 1)
        self.assertIn("SARIF invalido", err)

    def test_document_without_runs_fails(self):
        self.write_file("csharp.sarif", {"version": "2.1.0"})
        code, _out, _err = self.run_main(self.root)
        self.assertEqual(code, 1)

    def test_document_with_empty_runs_fails(self):
        self.write_file("csharp.sarif", {"version": "2.1.0", "runs": []})
        code, _out, _err = self.run_main(self.root)
        self.assertEqual(code, 1)

    def test_run_without_results_list_fails(self):
        self.write_file("csharp.sarif", {"version": "2.1.0", "runs": [{"tool": {"driver": {"name": "CodeQL"}}}]})
        code, _out, _err = self.run_main(self.root)
        self.assertEqual(code, 1)

    def test_result_that_is_not_an_object_fails(self):
        self.write_file("csharp.sarif", make_sarif(["texto no lugar de resultado"]))
        code, _out, _err = self.run_main(self.root)
        self.assertEqual(code, 1)

    def test_usage_without_argument_returns_2(self):
        code, _out, _err = self.run_main()
        self.assertEqual(code, 2)


if __name__ == "__main__":
    unittest.main()
