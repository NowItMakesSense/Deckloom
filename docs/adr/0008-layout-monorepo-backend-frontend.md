# ADR-0008: Monorepo com `backend/` e `frontend/`, e comandos de teste/lint/build propostos

- Status: Accepted (2026-10-23)
- Date: 2026-10-23
- Deciders: usuário (escolha da opção a, 2026-10-23), a partir da proposta do architect (feature `conta-jogador`)

## Context

A discovery deixou para a arquitetura a estrutura do repositório (backend .NET + frontend React/TypeScript) e os comandos `Test`/`Lint`/`Build` do `CLAUDE.md`, hoje `TBD`. O projeto usa trunk-based com PRs pequenos e Conventional Commits, CI com cobertura mínima de 80% e o monólito modular da ADR-0001.

## Decision

Um único repositório com duas raízes de toolchain:

```
backend/   Deckloom.sln, Directory.Packages.props,
           src/Deckloom.Api, src/Deckloom.SharedKernel,
           src/Modules/Accounts/Deckloom.Accounts.{Domain,Application,Infrastructure},
           src/Modules/DeckSuggestion/Deckloom.DeckSuggestion.Infrastructure,
           tests/<Projeto>.Tests (espelhando src), tests/Deckloom.Accounts.Testing (fakes)
frontend/  Vite + React + TypeScript; testes co-localizados *.test.tsx; e2e/ (Playwright)
docker-compose.yml, .env.example (só placeholders) na raiz
```

Comandos **propostos** para o `CLAUDE.md`, a aplicar somente via `/amend-constitution` (esta ADR não altera o `CLAUDE.md`):
- Test: `dotnet test backend/Deckloom.sln && npm --prefix frontend run test -- --run`
- Lint/format: `dotnet format backend/Deckloom.sln --verify-no-changes && npm --prefix frontend run lint && npm --prefix frontend run format:check`
- Build: `dotnet build backend/Deckloom.sln -c Release && npm --prefix frontend run build`
- E2E (job separado no CI): `npm --prefix frontend run e2e`

## Alternatives considered

- **(b) Monorepo plano** (`src/Deckloom.*` e `src/web` lado a lado): menos aninhamento, mas mistura toolchains .NET e Node no mesmo nível e complica filtros de caminho no CI.
- **(c) Dois repositórios**: isolamento total, mas mudanças de contrato API/UI exigiriam PRs coordenados, contra o trunk-based com PRs pequenos.

## Consequences

- O CI pode rodar jobs por pasta (`backend/**`, `frontend/**`) e um job e2e que usa os dois.
- Mudança de contrato API + UI cabe num único PR.
- Os comandos só passam a valer quando o `CLAUDE.md` for emendado por `/amend-constitution`.
