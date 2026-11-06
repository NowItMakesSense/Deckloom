"""Higiene do workflow de CI (.github/workflows/ci.yml), so stdlib (sem PyYAML, como os outros scripts de CI).

O leitor aqui e deliberadamente minimo: entende a forma que o proprio ci.yml usa (jobs com indentacao de
dois espacos, env de job com indentacao de quatro, steps em lista "- "). Se o arquivo mudar de forma o teste
falha, em vez de aprovar em silencio.

Rodar a partir da raiz do repositorio:
    python -m unittest discover -s scripts/ci -p "test_*.py"
"""

import os
import unittest

WORKFLOW = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".github", "workflows", "ci.yml")
CODEQL_JOB = "codeql"
DIFF_INFORMED_VAR = "CODEQL_ACTION_DIFF_INFORMED_QUERIES"
API_CSPROJ = "backend/src/Deckloom.Api/Deckloom.Api.csproj"
SOLUTION = "backend/Deckloom.sln"


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


def dotnet_build_commands(lines, job):
    return [line.strip() for line in job_lines(lines, job) if "dotnet build" in line]


def read_workflow_lines():
    with open(WORKFLOW, encoding="utf-8") as handle:
        return handle.read().splitlines()


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

    def test_codeql_build_compiles_only_deckloom_api_project(self):
        commands = dotnet_build_commands(self.lines, CODEQL_JOB)
        self.assertEqual(len(commands), 1, f"esperado um 'dotnet build' no job '{CODEQL_JOB}': {commands}")
        self.assertIn(API_CSPROJ, commands[0])
        self.assertNotIn(SOLUTION, commands[0], "o build do CodeQL nao deve compilar a solucao inteira (SEC-29)")


if __name__ == "__main__":
    unittest.main()
