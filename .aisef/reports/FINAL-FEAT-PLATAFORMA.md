# Relatório final - FEAT-PLATAFORMA

Data: 2026-11-06 | Plano aprovado: APR-005 | Ciclo 1 | Branch: `feat/plataforma` (nada commitado ainda)

## Planejado x executado
| Requisito | Tarefas | Situação | Evidência |
|---|---|---|---|
| REQ-P01 Arquitetura OPT-C: núcleo modular + 3 serviços com deploy e banco próprios, mensageria com outbox | T-P01, T-P02, T-P03, T-P03B, T-P05 | atendido | ADR-0018 (+ Emenda 1); `ServiceIsolationTests`; `OutboxPersistenceTests`/`OutboxDispatcherTests`; `ComposeBrokerTests`; `test_ci_workflow.py`; validadas (E550, E610, E847, E878, E1114) |
| REQ-P02 Os 5 TCGs do MVP | T-P04 | atendido | módulo `Catalog`, migration `CreateTcgCatalog` (arquivo); validada (E1055) |
| REQ-P03 Backend .NET e frontend React | T-P01, T-P02 | atendido | ADR-0018; serviços em .NET 10 |

Requisitos atendidos: 3 de 3. Critérios da feature: AC-P01a, AC-P01b, AC-P02a, AC-P02b, AC-P02c, AC-P03a, AC-P03b, AC-P04a, AC-P05a, AC-P05b, AC-P05c atendidos.

Verificação final (orquestrador, 2026-11-06): backend 2867 aprovados, 0 falhas; frontend 1000 aprovados; E2E 215 aprovados; scripts de CI 45 OK; `dotnet format`, lint e format do frontend limpos; portão de cobertura de 80% aprovado (relatório do validador da T-P05).

## O que foi entregue
- **ADR-0018** (OPT-C), com a ADR-0001 "Superseded in part". Emenda 1: RabbitMQ (APR-016), outbox no Accounts com publicador no-op/log até o adapter (APR-017), broker sem porta publicada (APR-020).
- **3 serviços** (`Deckloom.SuggestionService`, `Deckloom.MetaIngestionService`, `Deckloom.NotificationService`), só com `GET /health`, isolados do núcleo por teste de arquitetura.
- **Outbox** no `AccountsDbContext`: eventos de domínio gravados na mesma transação; despachante no Api publica uma única vez. Migration `AddOutboxMessage` (arquivo).
- **RabbitMQ** no `docker-compose.yml`, só na rede interna, credenciais `${RABBITMQ_DEFAULT_USER}`/`${RABBITMQ_DEFAULT_PASS}`; variáveis no `.env.example` (`<set-me>`).
- **Catálogo de TCGs** (`catalog.tcg`: mtg, pokemon, yugioh, onepiece, lorcana), porta `ITcgCatalog` registrada no Api, schema `catalog` nas verificações de papéis (ADR-0015) e no `scripts/db/criar-banco-local.ps1`.
- **CI**: CodeQL compila os 3 serviços; serviços e Catalog no portão de cobertura; guarda que falha se um projeto de `backend/src` ficar sem classificação.

## Decisões do usuário nesta feature
APR-015 (Catalog em módulo novo), APR-016 (RabbitMQ), APR-017 (outbox no Accounts, adapter depois), APR-018/AUTH-001 (escopo config_ci_iac para o devops), APR-019 (.env.example), APR-020 (sem porta publicada), APR-025 (ids, Catalog no Api, papéis). Fora da feature: APR-021/ADR-0020 (acesso ao dev local).

## Limites
- Migrations `AddOutboxMessage` e `CreateTcgCatalog` não foram aplicadas nem testadas contra PostgreSQL real (ADR-0012). A consulta do `NpgsqlDatabaseRoleInspector` e o script `.ps1` não foram executados.
- O publicador padrão da outbox marca mensagens como processadas sem broker. Isso é aceito até o primeiro consumidor (ADR-0018, Emenda 1).
- A tag `rabbitmq:4.1-management` não foi confirmada online; os containers não foram subidos.
- O Down de `CreateTcgCatalog` deixa o schema `catalog` vazio (mesmo padrão do `CreateFormatCatalog`).

## Pendências (fora desta feature)
1. Antes do primeiro consumidor ou de produção: adapter RabbitMQ e retenção/limpeza da outbox, inclusive na exclusão de conta (LGPD), conforme a Emenda 1 da ADR-0018.
2. Aplicar e revisar as 2 migrations no banco local; com a ADR-0020, o Claude pode fazer isso com aprovação por operação.
3. Teste instável `SmtpPasswordResetSenderTests.REQ_028` (K-001).
4. Alerta do Dependabot (1 moderado) na `main`.
5. `CLAUDE.md` e `AGENTS.md` ainda descrevem o produto antigo ("MVP só Magic", "lojas não são atores").

Aceitar a feature não autoriza commit, push, PR nem merge.
