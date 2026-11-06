# ADR-0012: Testes dos adapters EF Core com SQLite em memória; fakes em memória no restante

- Status: Accepted (2026-10-23)
- Date: 2026-10-23
- Deciders: usuário (escolha da opção a, 2026-10-23), a partir da proposta do architect (feature `conta-jogador`)

## Context

O `CLAUDE.md` determina que os testes usem fakes em memória e proíbe qualquer acesso ao banco do sistema. Os adapters EF Core de `conta-jogador` dependem de constraints (email único, `subject` único, PKs compostas), cascatas da exclusão de conta (ADR-0006) e consultas de consumo atômico (`DELETE ... RETURNING`), que fakes não verificam. O provider InMemory do EF Core ignora constraints e transações.

## Decision

- **Adapters EF Core**: testados contra **SQLite em memória** (`Microsoft.EntityFrameworkCore.Sqlite`, `DataSource=:memory:`, conexão aberta durante o teste e descartada ao fim). É um banco efêmero criado pelo próprio teste, nunca o banco do sistema, sem credenciais nem rede. O esquema é criado com `EnsureCreated` a partir do modelo (as migrações PostgreSQL não são executadas).
- **Todo o restante** (domínio, aplicação, API, e2e): fakes em memória das portas (`InMemoryPlayerRepository` etc.).
- Ajustes de mapeamento só para SQLite ficam no projeto de teste (ex.: schemas `accounts`/`deck_suggestion` viram prefixo de tabela; `xmin` substituído por um token de concorrência de teste), sem alterar o mapeamento de produção.

## Alternatives considered

- **(b) Só fakes em memória**: mais simples, mas mapeamento, unicidade e cascatas só seriam verificados quando um humano aplicasse as migrações.
- **(c) PostgreSQL descartável (Testcontainers) iniciado por humano ou CI**: fidelidade total, mas exige Docker no CI e decisão explícita; não adotado agora (pode ser revisto numa ADR futura, sempre fora do alcance dos agentes).

## Consequences

- Comportamentos do PostgreSQL **não verificados** por esses testes: tipos `timestamptz` (SQLite guarda texto, sem fuso), `bytea` e o check `octet_length`; checks com expressão regular (`~`); índice único parcial (`WHERE is_default = true`, suportado pelo SQLite mas com sintaxe gerada diferente); concorrência otimista via `xmin`; schemas e FKs entre schemas; `generated always as identity`; semântica de colação e comparação de texto (o email já chega normalizado, o que reduz o risco); isolamento de transações e bloqueios sob concorrência real; `DELETE ... RETURNING` (o SQLite suporta, mas o SQL gerado pelo provider difere); desempenho e planos de índices.
- Esses pontos ficam para a revisão humana das migrações geradas e para a validação manual em ambiente de desenvolvimento (Docker Compose), feita por uma pessoa.
