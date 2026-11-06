# ADR-0019: Remoção da documentação do banco em Database/ (papéis, tabelas, índices e matriz de privilégios)

- Status: Accepted (2026-11-06)
- Date: 2026-11-06
- Deciders: usuário (dono do projeto, NowItMakesSense), plano aprovado no APR-006
- Supersedes: ADR-0015 em parte (somente o trecho que exigia a documentação em `Database/`: a matriz de privilégios por tabela e as descrições de papéis e tabelas). As demais decisões da ADR-0015 continuam válidas.

## Context
A ADR-0015 definiu os papéis do PostgreSQL (`postgres`, `deckloom_owner`, `deckloom_migrator`, `deckloom_app`) e uma matriz de privilégio mínimo por tabela, mantida em `Database/roles/privilege-matrix.md`. Ao redor dela havia um padrão de documentar tabelas, índices, chaves e papéis em arquivos `.md` dentro de `Database/`, com testes que comparavam o modelo do EF e os GRANT das migrations com esses arquivos.

Em 2026-11-06 o usuário decidiu remover esse padrão, sem substituto (feature `FEAT-SEM-DATABASE-DOCS`). A tarefa T-D01 removeu a pasta `Database/` (59 arquivos `.md`) e 42 testes que a usavam como fonte de verdade. A lista completa dos arquivos removidos está em `.aisef/reports/RPT-T-D01.json`.

## Decision
1. A pasta `Database/` sai do repositório, junto com a matriz de privilégios, as classes que liam essa documentação (`DatabaseDocs`, `RoleDocs`, `PrivilegeMatrix`) e os testes que a usavam como fonte de verdade.
2. Não há substituto para essa verificação: nenhum arquivo ou teste novo confere o modelo do EF nem os GRANT das migrations contra uma especificação.
3. A fonte de verdade passa a ser o código: as migrations em `backend/src/Modules/*/Infrastructure/Migrations`, as entidades e as configurações do EF.
4. Continuam valendo as decisões da ADR-0015 que não dependem de `Database/`: quatro papéis com seus atributos, um único papel de aplicação (`deckloom_app`) para a API, dono sem login, migrador usado só por uma pessoa, bootstrap por `db/init/01-roles.sh`, endurecimento do `PUBLIC` e limites de timeout e de conexões.
5. A T-P04 da FEAT-PLATAFORMA perde o critério AC-P04b e o path `Database/**`, porque ambos dependiam da pasta removida.
6. Os documentos vivos (README, CLAUDE.md, AGENTS.md, `docs/security-policy.md`, `db/init/01-roles.sh` e `.aisef/discovery.md`) deixam de citar `Database/` e passam a apontar para as migrations e para a ADR-0015/ADR-0019.

## Consequences
- Três proteções automatizadas deixam de existir. O validador da T-D01 as registrou:
  1. `GeneratedUpdateColumnsTests`: garantia de que colunas imutáveis (email, termos, `created_at`, `id`, `player_id` e `token_hash` da sessão) nunca entram no SET de um UPDATE gerado pelo EF.
  2. Três testes de `MigrationGrantConformanceTests`: conferiam os GRANT por tabela e coluna das migrations contra a matriz.
  3. Três testes de `RepoHygieneTests`: conferiam `db/init/01-roles.sh` contra a documentação de papéis.
- Risco médio aceito pelo usuário (RISK-FEAT-SEM-DATABASE-DOCS-1): uma migration pode conceder privilégio além do necessário ao `deckloom_app` sem nenhum teste falhar. A garantia de menor privilégio da ADR-0015 passa a depender só da revisão humana.
- Risco baixo aceito (RISK-FEAT-SEM-DATABASE-DOCS-2): mudanças no modelo do EF deixam de ser conferidas contra uma especificação escrita. Os testes de persistência com SQLite em memória (ADR-0012) continuam cobrindo o comportamento, mas não verificam papéis nem GRANT.
- A revisão humana das migrations continua obrigatória antes de qualquer aplicação. A API não aplica migrations na inicialização (há teste de arquitetura para isso), e nenhum agente nem o CI executa `dotnet ef database update`.
- A T-P04 ficou sem o critério de documentação de tabela e de privilégio; o critério AC-P04a (migration de catálogo com rollback) continua.
- O histórico em `docs/specs/`, as ADRs 0001-0014 e 0016 e os relatórios de tarefas anteriores continuam citando `Database/`. Eles são registro histórico e não são editados.

## Alternatives considered
- **Mover a matriz de privilégios para código** (um teste que lê as migrations e as compara com uma tabela em C#): oferecido e recusado pelo usuário em 2026-11-06, que pediu que o padrão de documentação do banco saísse do projeto.
- **Manter `Database/` apenas como documentação, sem testes**: rejeitado, porque o usuário não quer o padrão de documentar o banco em arquivos `.md`.
- **Substituir só parte dos testes** (por exemplo, a conferência de GRANT): rejeitado, porque a feature foi aprovada como "sem substituto" e o risco foi aceito com esse escopo.
