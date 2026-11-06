# Specs (histórico anterior ao AISEF)

Esta pasta guarda as especificações produzidas antes da adoção do AISEF (ADR-0017), pelo fluxo da skill `new-project-setup` (`/plan-grill`, `/orchestrate`). O conteúdo é registro histórico: não é editado e não é a fonte de verdade de trabalho novo.

O estado de engenharia atual fica em `.aisef/`: `discovery.md`, Feature Contracts, Engineering Blueprints, Task Contracts, aprovações e log de auditoria. Requisitos e decisões daqui que continuam valendo são citados de lá, com fonte.

| Arquivo | O que é |
|---|---|
| `discovery.md` | Discovery inicial do produto (2026-10-21): problema, atores, MVP, fora de escopo, integrações, features propostas e questões abertas. |
| `conta-jogador/` | Feature fechada em 2026-11-05 (184/184 tarefas, código na `main`, sem deploy). `requirements.md` (REQ-*), `design.md`, `tasks.md`, `findings-ledger.md`, `security-report.md`, `release-notes.md` (bloqueios de go-live) e `retro.md` (pendências carregadas adiante). |

As menções a `.claude/...`, `/orchestrate` e aos agentes antigos (`architect`, `implementer`, `designer`...) dentro desses arquivos se referem ao ferramental removido na migração.
