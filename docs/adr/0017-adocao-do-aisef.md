# ADR-0017: Adoção do AISEF como governança dos agentes, histórico git reiniciado e repositório público

- Status: Accepted (2026-11-06)
- Date: 2026-11-06
- Deciders: usuário (dono do projeto, NowItMakesSense), plano de migração aprovado em 2026-11-06
- Supersedes: ADR-0014; ADR-0013 no que toca ao mecanismo `/amend-constitution` e às camadas do guard em `.claude/`

## Context
O Deckloom foi gerado pela skill `new-project-setup`, que pôs em `.claude/` um orquestrador próprio (`/orchestrate` e as skills `/plan-grill`, `/to-spec`, `/to-tickets`, `/amend-constitution`), 14 agentes, um grafo de conhecimento (`docs/knowledge-graph/`) e hooks de guarda (`block-db-access.py`, `verify-guard.py`). A feature `conta-jogador` foi entregue nesse fluxo. O repositório remoto original foi apagado, e o usuário decidiu governar o projeto com o plugin AISEF, que traz contratos versionados (feature, blueprint, tarefa, agente), aprovações explícitas e um log de auditoria assinado.

## Decision
1. O projeto passa a ser governado pelo AISEF (`.aisef/`, plugin `aisef` 0.3.0): discovery, Feature Contracts, Engineering Blueprints, Task Contracts, Agent Contracts, Approval Center e Execution Gateway.
2. Todo o ferramental anterior de `.claude/` sai do repositório, assim como `docs/knowledge-graph/`, `docs/specs/_templates/`, o índice `docs/specs/STATUS.md` e `.setup-answers.json`. O `.claude/settings.json` passa a ser o gerado por `aisef_project.py settings --apply`.
3. `CLAUDE.md` e `AGENTS.md` foram reescritos: o `AGENTS.md` ganha a seção AISEF, e as regras mudam pelo fluxo do AISEF (aprovação do usuário registrada), não mais por `/amend-constitution`.
4. `docs/specs/` fica como registro histórico (ver `docs/specs/README.md`). ADRs 0001-0016 seguem válidas no conteúdo técnico.
5. O agente `designer` deixa de existir. O dono do `docs/design-system.md` passa a ser o agente `frontend` do AISEF; mudança de direção de arte continua exigindo confirmação do usuário.
6. O histórico git foi reiniciado com um commit inicial que importa o estado do merge do PR #4 (`cfe263a` no repositório antigo). O repositório novo é **público**.
7. CI: o job `guard-and-traceability` vira `ci-scripts` (só testes de `scripts/ci/`). O CodeQL passa a enviar o SARIF ao Code Scanning (repositório público), mantendo o portão `check_sarif.py`. As fingerprints do `.gitleaksignore` (presas a SHAs antigos) foram trocadas por `.gitleaks.toml`, que estende as regras padrão e libera só seis valores fictícios exatos das suítes de teste (SEC-27). Com o histórico novo, o primeiro scan cobre o repositório inteiro, e não só o diff de um PR: foram 16 ocorrências, todas fictícias.

## Consequences
- A guarda contra acesso ao banco e a segredos passa a ser o Execution Gateway do AISEF, mais as regras nativas. O CI não reverifica essas regras locais (antes fazia, com `verify-guard.py`).
- A rastreabilidade requisito x tarefa passa a ser validada pelo `aisef-check`, localmente; o CI deixa de checar `requirements.md` x `tasks.md`.
- O `git log`/`git blame` anterior à migração só existe na cópia local `Deckloom_old`. Os SHAs citados em docs e ADRs (por exemplo, `96e0919`, `37c6df8`) não existem no repositório novo.
- Com o repositório público, relatórios de segurança e ledger de achados da `conta-jogador` ficam visíveis. Os achados High/Critical estão fechados; os Lows abertos estão triados em `docs/specs/conta-jogador/retro.md`.
- O e-mail pessoal do dono foi trocado por "dono do projeto (NowItMakesSense)" nos docs.

## Alternatives considered
- **Manter o ferramental antigo ao lado do AISEF**: rejeitado, porque dois orquestradores e agentes homônimos (`qa`, `security`, `domain`, `frontend`) competiriam.
- **Manter só os guardas antigos (hook de banco + `verify-guard` no CI)**: rejeitado pelo usuário, em favor de uma governança única.
- **Preservar o histórico git**: rejeitado pelo usuário, em favor de um começo limpo.
