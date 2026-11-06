"""Higiene do workflow de CI (.github/workflows/ci.yml), so stdlib (sem PyYAML, como os outros scripts de CI).

O leitor aqui e deliberadamente minimo: entende a forma que o proprio ci.yml usa (jobs com indentacao de
dois espacos, env de job com indentacao de quatro, steps em lista "- "). Se o arquivo mudar de forma o teste
falha, em vez de aprovar em silencio.

Rodar a partir da raiz do repositorio:
    python -m unittest discover -s scripts/ci -p "test_*.py"
"""

import os
import sys
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", ".."))
WORKFLOW = os.path.join(REPO_ROOT, ".github", "workflows", "ci.yml")
CODEQL_JOB = "codeql"
BACKEND_JOB = "backend"
DIFF_INFORMED_VAR = "CODEQL_ACTION_DIFF_INFORMED_QUERIES"
API_CSPROJ = "backend/src/Deckloom.Api/Deckloom.Api.csproj"
SOLUTION = "backend/Deckloom.sln"
SERVICES_DIR = "backend/src/Services"

sys.path.insert(0, SCRIPT_DIR)

import check_coverage as cc  # noqa: E402


def indent_of(line):
    return len(line) - len(line.lstrip(" "))


def is_blank_or_comment(line):
    stripped = line.strip()
    return not stripped or stripped.startswith("#")


def scalar(raw):
    """Valor de um escalar YAML simples: sem comentario final e sem aspas."""
    value = raw.split(" #", 1)[0].strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1]
    return value


def job_lines(lines, job):
    """Linhas do corpo do job (sem o cabecalho), ate o proximo item de primeiro nivel."""
    start = None
    for index, line in enumerate(lines):
        if line == f"  {job}:":
            start = index + 1
            break
    if start is None:
        raise AssertionError(f"job '{job}' ausente em ci.yml")
    body = []
    for line in lines[start:]:
        if not is_blank_or_comment(line) and indent_of(line) <= 2:
            break
        body.append(line)
    return body


def job_env(lines, job):
    """Variaveis do bloco env no nivel do job. O env de step (indentacao 8) nao conta."""
    env = {}
    inside_job_env = False
    for line in job_lines(lines, job):
        if is_blank_or_comment(line):
            continue
        indent = indent_of(line)
        if indent == 4:
            inside_job_env = line.strip() == "env:"
        elif inside_job_env and indent == 6:
            key, _, raw = line.strip().partition(":")
            env[key.strip()] = scalar(raw)
    return env


def job_commands(lines, job, verb):
    """Linhas do job que chamam 'dotnet <verb>' (build, test, ...)."""
    return [line.strip() for line in job_lines(lines, job) if f"dotnet {verb}" in line]


def dotnet_build_commands(lines, job):
    return job_commands(lines, job, "build")


def read_workflow_lines():
    with open(WORKFLOW, encoding="utf-8") as handle:
        return handle.read().splitlines()


def service_csprojs():
    """Todo csproj sob backend/src/Services, como caminho relativo a raiz e com barras normais."""
    root = os.path.join(REPO_ROOT, *SERVICES_DIR.split("/"))
    found = []
    for dirpath, _dirs, files in os.walk(root):
        for name in files:
            if name.endswith(".csproj"):
                relative = os.path.relpath(os.path.join(dirpath, name), REPO_ROOT)
                found.append(relative.replace(os.sep, "/"))
    return sorted(found)


def solution_projects():
    """Caminhos dos projetos de backend/Deckloom.sln, relativos a raiz do repo (barras normais).

    O .sln grava os caminhos relativos a propria pasta dele (backend/), por isso o prefixo.
    """
    base = os.path.dirname(SOLUTION)
    paths = []
    with open(os.path.join(REPO_ROOT, *SOLUTION.split("/")), encoding="utf-8") as handle:
        for line in handle:
            if line.startswith("Project("):
                fields = line.split(" = ", 1)[1].split(", ")
                relative = fields[1].strip().strip('"').replace("\\", "/")
                paths.append(f"{base}/{relative}")
    return paths


class JobEnvParserTests(unittest.TestCase):
    def test_job_env_ignores_env_declared_inside_a_step(self):
        lines = [
            "jobs:",
            "  alvo:",
            "    steps:",
            "      - name: passo",
            "        env:",
            '          CODEQL_ACTION_DIFF_INFORMED_QUERIES: "false"',
            "  outro:",
            "    env:",
            '      X: "1"',
        ]
        self.assertEqual(job_env(lines, "alvo"), {})

    def test_job_env_reads_job_level_value_and_strips_quotes(self):
        lines = [
            "jobs:",
            "  alvo:",
            "    env:",
            '      CODEQL_ACTION_DIFF_INFORMED_QUERIES: "false"  # comentario',
            "    steps:",
            "      - name: passo",
        ]
        self.assertEqual(job_env(lines, "alvo"), {"CODEQL_ACTION_DIFF_INFORMED_QUERIES": "false"})


class CodeqlJobWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.lines = read_workflow_lines()

    def test_diff_informed_queries_is_false_at_codeql_job_level(self):
        env = job_env(self.lines, CODEQL_JOB)
        self.assertIn(DIFF_INFORMED_VAR, env, f"{DIFF_INFORMED_VAR} ausente no nivel do job '{CODEQL_JOB}' (SEC-30)")
        self.assertEqual(env[DIFF_INFORMED_VAR], "false")

    def test_codeql_build_compiles_api_and_every_service_csproj_exactly_once(self):
        services = service_csprojs()
        self.assertTrue(services, f"nenhum csproj encontrado em {SERVICES_DIR}")
        commands = dotnet_build_commands(self.lines, CODEQL_JOB)
        expected = [API_CSPROJ] + services
        self.assertEqual(len(commands), len(expected), f"builds do CodeQL divergem do esperado: {commands}")
        for project in expected:
            matching = [command for command in commands if project in command]
            self.assertEqual(
                len(matching),
                1,
                f"{project} deve ser compilado uma vez no build do CodeQL (SEC-29 / ADR-0018): {commands}",
            )

    def test_codeql_build_does_not_compile_the_solution(self):
        for command in dotnet_build_commands(self.lines, CODEQL_JOB):
            self.assertNotIn(SOLUTION, command, "o build do CodeQL nao deve compilar a solucao inteira (SEC-29)")


class BackendJobWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.lines = read_workflow_lines()

    def test_backend_job_builds_and_tests_the_solution_that_holds_the_services(self):
        builds = job_commands(self.lines, BACKEND_JOB, "build")
        tests = job_commands(self.lines, BACKEND_JOB, "test")
        self.assertTrue(any(SOLUTION in command for command in builds), f"build sem {SOLUTION}: {builds}")
        self.assertTrue(any(SOLUTION in command for command in tests), f"test sem {SOLUTION}: {tests}")

    def test_solution_lists_every_service_csproj_and_its_test_project(self):
        services = service_csprojs()
        self.assertTrue(services, f"nenhum csproj encontrado em {SERVICES_DIR}")
        projects = solution_projects()
        for service in services:
            self.assertIn(service, projects, f"{service} fora de {SOLUTION}: o job backend nao compila nem testa")
            name = os.path.splitext(os.path.basename(service))[0]
            test_project = f"backend/tests/{name}.Tests/{name}.Tests.csproj"
            self.assertIn(test_project, projects, f"projeto de teste {test_project} fora de {SOLUTION}")

    def test_backend_coverage_gate_uses_the_ci_threshold(self):
        gate = [line for line in job_lines(self.lines, BACKEND_JOB) if "check_coverage.py" in line]
        self.assertEqual(len(gate), 1, f"esperado um portao de cobertura no job backend: {gate}")
        self.assertIn('--threshold "$COVERAGE_THRESHOLD"', gate[0])


def production_csprojs():
    """Todo csproj sob backend/src (nucleo, modulos, SharedKernel, Api e servicos): codigo de producao."""
    root = os.path.join(REPO_ROOT, "backend", "src")
    names = []
    for _dirpath, _dirs, files in os.walk(root):
        for name in files:
            if name.endswith(".csproj"):
                names.append(os.path.splitext(name)[0])
    return sorted(names)


class CoverageClassificationGuardTests(unittest.TestCase):
    def test_every_production_csproj_is_classified_in_the_coverage_gate(self):
        production = production_csprojs()
        self.assertTrue(production, "nenhum csproj de producao encontrado em backend/src")
        classified = set(cc.PRODUCT_PACKAGES) | set(cc.TEST_SUPPORT_PACKAGES)
        missing = [name for name in production if name not in classified]
        self.assertEqual(
            missing,
            [],
            "projetos de producao fora de PRODUCT_PACKAGES e TEST_SUPPORT_PACKAGES (check_coverage.py): "
            f"{missing}",
        )


class ServiceCoverageGateTests(unittest.TestCase):
    def test_every_service_csproj_is_a_product_package_of_the_coverage_gate(self):
        services = service_csprojs()
        self.assertTrue(services, f"nenhum csproj encontrado em {SERVICES_DIR}")
        for service in services:
            name = os.path.splitext(os.path.basename(service))[0]
            self.assertIn(
                name,
                cc.PRODUCT_PACKAGES,
                f"{name} precisa estar em PRODUCT_PACKAGES para ter o mesmo portao de 80% (check_coverage.py)",
            )


if __name__ == "__main__":
    unittest.main()
