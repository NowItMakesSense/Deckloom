# ADR-0014: Adotar o time de front-end da versão atual da skill geradora

- Status: Superseded by ADR-0017 (2026-11-06)
- Date: 2026-10-23
- Deciders: usuário (escolha "A + B com emenda", 2026-10-23)

## Context

O Deckloom foi gerado em 2026-10-21 por uma versão da skill `new-project-setup` anterior ao time de front-end. A versão atual (main `299b7d6`) gera, para projetos com UI (`HAS_UI=yes`): os agentes `frontend` (constrói as tarefas `Owner: frontend` na fase 5) e `ui-reviewer` (revisão de UI na fase 6), a skill `frontend-craft` com as regras da casa (direção de arte, telas de app, tipografia/cor, motion, dados extremos, formato de revisão), o `designer` como diretor de arte, e o roteamento de tarefas por `Owner:` em `planner`, `spec-writer` e `orchestrate`. A atualização também trouxe a exportação automática de `graph.md` pelo `render-graph.py`.

## Decision

1. Adotar o time de front-end como a skill atual gera, sem customização local: `.claude/agents/{frontend,ui-reviewer}.md`, `.claude/skills/frontend-craft/`, e as versões atuais de `designer.md`, `planner.md`, `orchestrate/SKILL.md`, `spec-writer/SKILL.md` e dos templates `design.md`/`tasks.md`.
2. Atualizar a lista de agentes no `CLAUDE.md` (seção Agents) para o texto da skill atual.
3. Emendar as specs aprovadas da `conta-jogador` em vez de mantê-las como estavam: `tasks.md` ganha `Owner:` em cada tarefa e `design.md` ganha os itens de direção de arte do template novo; ambos voltam a Draft e são reaprovados.

Tiers de modelo (perfil `balanced`): `frontend` = sonnet, `ui-reviewer` = opus.

## Alternatives considered

- Só as ferramentas (sem time de front-end): descartado - as telas da conta-jogador (T-095..T-120) ficariam sem as regras de front-end e sem revisão de UI dedicada.
- Adotar sem emendar as specs: descartado - a regra nova do `orchestrate` devolve ao `planner` toda tarefa sem `Owner`, então exigiria uma exceção local que diverge da skill.

## Consequences

- Fase 6 passa a incluir `ui-reviewer` quando houver tarefas `Owner: frontend`.
- Os agentes novos só ficam disponíveis numa sessão nova do Claude Code (definições de agentes são carregadas no início da sessão).
- `.setup-answers.json` passa a ter os comandos aprovados na ADR-0013, para que `verify_setup.py` compare com os valores atuais.
