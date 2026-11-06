"""Testes de check_nuget_audit.py (saida JSON de `dotnet list ... package --vulnerable`).

Rodar a partir da raiz do repositorio:
    python -m unittest discover -s scripts/ci -p "test_*.py"
"""

import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import check_nuget_audit as audit  # noqa: E402


CLEAN = {
    "version": 1,
    "parameters": "--vulnerable --include-transitive",
    "projects": [
        {"path": "backend/src/Deckloom.Api/Deckloom.Api.csproj"},
        {
            "path": "backend/tests/Deckloom.Api.Tests/Deckloom.Api.Tests.csproj",
            "frameworks": [
                {
                    "framework": "net10.0",
                    "topLevelPackages": [{"id": "xunit", "resolvedVersion": "2.9.3"}],
                    "transitivePackages": [{"id": "System.Text.Json", "resolvedVersion": "10.0.0"}],
                }
            ],
        },
    ],
}

VULNERABLE = {
    "version": 1,
    "projects": [
        {
            "path": "backend/src/Deckloom.Api/Deckloom.Api.csproj",
            "frameworks": [
                {
                    "framework": "net10.0",
                    "transitivePackages": [
                        {
                            "id": "System.Net.Http",
                            "resolvedVersion": "4.3.0",
                            "vulnerabilities": [{"severity": "High", "url": "https://example.test/GHSA"}],
                        }
                    ],
                }
            ],
        }
    ],
}


class NugetAuditTests(unittest.TestCase):
    def test_clean_report_passes(self):
        findings = audit.find_findings(CLEAN)
        self.assertEqual(findings, [])

    def test_vulnerable_package_is_reported_with_its_id(self):
        findings = audit.find_findings(VULNERABLE)
        self.assertEqual(len(findings), 1)
        self.assertIn("System.Net.Http", findings[0])

    def test_empty_vulnerabilities_list_is_not_a_finding(self):
        doc = json.loads(json.dumps(CLEAN))
        doc["projects"][1]["frameworks"][0]["topLevelPackages"][0]["vulnerabilities"] = []
        self.assertEqual(audit.find_findings(doc), [])

    def test_restore_problems_fail_the_audit(self):
        doc = {"version": 1, "projects": [], "problems": [{"text": "NU1301: unable to load"}]}
        self.assertTrue(audit.find_findings(doc))

    def test_invalid_json_fails_closed(self):
        self.assertEqual(audit.main_from_text("{ not json"), 1)

    def test_main_returns_zero_for_clean_and_one_for_vulnerable(self):
        self.assertEqual(audit.main_from_text(json.dumps(CLEAN)), 0)
        self.assertEqual(audit.main_from_text(json.dumps(VULNERABLE)), 1)


if __name__ == "__main__":
    unittest.main()
