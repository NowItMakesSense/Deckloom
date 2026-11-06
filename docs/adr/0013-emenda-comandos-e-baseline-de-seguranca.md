# ADR-0013: Emenda da constituição - comandos Test/Lint/Build e `docs/security-policy.md` como baseline

- Status: Accepted (2026-10-23); Superseded by ADR-0017 (2026-11-06) quanto ao mecanismo `/amend-constitution` e às camadas do guard em `.claude/`
- Date: 2026-10-23
- Deciders: usuário (confirmado via `/amend-constitution`, 2026-10-23)

## Context

O `CLAUDE.md`/`AGENTS.md` foram gerados com `Test`/`Lint`/`Build` como `TBD`, a preencher quando a stack e a estrutura do repositório fossem decididas. Essa decisão foi tomada no design aprovado da feature `conta-jogador` (ADR-0008: monorepo com `backend/` e `frontend/`).

Separadamente, o projeto passou a ter `docs/security-policy.md` (commit `0ae4b13`), que faltava por um defeito da skill geradora `new-project-setup`, já corrigido na origem. Os projetos gerados pela skill corrigida citam essa política nos Engineering defaults; o `CLAUDE.md` deste projeto não citava, porque só muda via `/amend-constitution`.

## Decision

1. Preencher os comandos no `CLAUDE.md` (seção Commands) e no `AGENTS.md`:
   - Test: `dotnet test backend/Deckloom.sln && npm --prefix frontend run test -- --run`
   - Lint/format: `dotnet format backend/Deckloom.sln --verify-no-changes && npm --prefix frontend run lint && npm --prefix frontend run format:check`
   - Build: `dotnet build backend/Deckloom.sln -c Release && npm --prefix frontend run build`
   E remover a nota "(Any `TBD` must be filled once the stack is decided.)", que perdeu a função.
2. Citar `docs/security-policy.md` como baseline de segurança nos Engineering defaults do `CLAUDE.md` e na seção de testes/depuração do `AGENTS.md`, igual ao texto que a skill corrigida gera.

Prime directives, a diretiva de acesso ao banco e os blocos de regras não negociáveis dos agentes não foram tocados.

## Alternatives considered

- Manter `TBD` até a fase 5: descartado - o `qa`, o CI e o `implementer` precisam dos comandos reais, e a decisão já estava aprovada.
- Só os comandos, sem citar a política: descartado pelo usuário - a política existe e deve ser descoberta a partir do `CLAUDE.md`, como nos projetos novos.

## Consequences

- `implementer`/`test`/`qa` e o pipeline de CI (fase 8) usam esses comandos; eles só funcionam depois que o esqueleto do monorepo for criado na fase 5.
- Nenhuma feature em andamento foi aprovada sob o texto antigo de forma incompatível: o design da `conta-jogador` já usa estes comandos e já cita a política.
