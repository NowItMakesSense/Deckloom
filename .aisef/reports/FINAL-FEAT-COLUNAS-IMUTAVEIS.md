# Relatório final - FEAT-COLUNAS-IMUTAVEIS

Data: 2026-11-06 | Plano aprovado: APR-008 | Ciclo 1

## Planejado x executado
| Requisito | Tarefa | Situação | Evidência |
|---|---|---|---|
| REQ-I01 Colunas imutáveis nunca entram no SET de UPDATE gerado pelo EF, com a lista no próprio teste | T-I01 | atendido | `ImmutableColumnsTests.cs` (25 testes); RPT-T-I01; validada (E415) |

Requisitos atendidos: 1 de 1. Testes: 2826 aprovados, 0 falhas; cobertura >= 80% em todos os projetos.

## O que ficou protegido
- **player:** id, email, terms_version, terms_accepted_at, adult_declared, created_at.
- **player_session:** id, player_id, token_hash, created_at.
- **password_reset_token:** player_id. O token_hash dessa tabela é regravado de forma legítima na reemissão.
- **pending_google_auth:** id, email, created_at. A tabela nunca recebe UPDATE.

As colunas além do texto do requisito (adult_declared, created_at das sessões e das pendências) foram avaliadas pelo validador como coerentes com o REQ-I01.

## Limites
- O SQL do SQLite pode diferir do Npgsql (ADR-0012).
- Colunas novas não entram sozinhas na lista: só as já listadas são conferidas contra o modelo.
- A reemissão do token de reset só confirma que houve escrita na tabela.

## Pendências
- Nenhuma. Nada foi commitado; aceitar a feature não autoriza commit nem push.
