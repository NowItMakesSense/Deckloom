# deckloom_app

Introduced by feature: conta-jogador
Status: Approved (emenda de controle de acesso ao banco, 2026-10-23; ADR-0015)

## Purpose
Único papel de aplicação: a API (endpoints e jobs em `BackgroundService`) usa só ele. Tem exatamente o DML de que os repositórios e jobs precisam, nada de DDL. Um só papel para todos os módulos porque a exclusão de conta e dos Decks acontece numa única transação (ADR-0006), e uma transação usa uma única identidade de sessão; o isolamento entre módulos está nos `GRANT` por tabela (ADR-0015).

## Attributes
`LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT`. Senha com hash SCRAM-SHA-256. `NOINHERIT`: o papel não tem associações; se alguém conceder uma por engano, os privilégios do outro papel não passam a valer automaticamente. `NOBYPASSRLS` e não ser dono de tabela mantêm aberta a adoção futura de RLS (hoje não adotada).

## Memberships
Nenhuma, em nenhuma direção.

## Grants
- Banco `deckloom`: só `CONNECT` (sem `CREATE`, sem `TEMPORARY`).
- Schemas `accounts` e `deck_suggestion`: só `USAGE`.
- Tabelas: exatamente a [matriz de privilégios](privilege-matrix.md) (resumo: `accounts.*` com `SELECT`/`INSERT`/`DELETE` e `UPDATE` só onde e nas colunas necessárias; `accounts.avatar_preset` e `deck_suggestion.format` só `SELECT`; `access_attempt`, `pending_google_auth`, `avatar_image`, `player_external_identity` e `player_preferred_format` sem `UPDATE`).
- Sequências: nenhuma (a identidade de `access_attempt` não exige).
- Nunca: `TRUNCATE`, `REFERENCES`, `TRIGGER`, `CREATE`, posse de objeto, privilégio em `__ef_migrations_history`, schema `public`.

## Limits
Definidos com `ALTER ROLE deckloom_app IN DATABASE deckloom SET ...` e `CONNECTION LIMIT`:

| Parâmetro | Valor | Motivo |
|---|---|---|
| `CONNECTION LIMIT` | 30 | Uma instância com `Maximum Pool Size=20` + folga. Regra: instâncias x `Maximum Pool Size` + 5 <= limite; revisar na fase 8 se houver mais de uma instância |
| `statement_timeout` | `5s` | Igual ao timeout de comando do design ("Resilience"); corta consulta longa no servidor mesmo se o cliente não cancelar |
| `lock_timeout` | `2s` | Espera por lock falha cedo (erro genérico "tente de novo") em vez de empilhar requisições |
| `idle_in_transaction_session_timeout` | `15s` | Transação esquecida aberta não segura locks nem conexão. Exige que nenhuma espera longa (atraso progressivo, Google, SMTP, SkiaSharp) aconteça com transação aberta |
| `search_path` | `pg_catalog` | Nenhum objeto é resolvido por nome não qualificado fora do catálogo do sistema; todo mapeamento EF Core é qualificado por schema |

O job horário de limpeza apaga em lotes (cada comando abaixo de `statement_timeout`).

## Used by
API `Deckloom.Api` (todos os `DbContext`: `AccountsDbContext`, `FormatCatalogDbContext`), via pool do Npgsql. Nenhum humano usa este papel para administração.

## Secret
- Configuração da API: `ConnectionStrings:Deckloom` (variável `ConnectionStrings__Deckloom`), com `Username=deckloom_app`. Segredo, nunca lido por agentes.
- Senha no dev: `DECKLOOM_APP_DB_PASSWORD`, usada pelo bootstrap no serviço `db` e interpolada pelo compose na connection string do serviço `api` (uma só fonte para a senha).
- Fora do dev: cofre de segredos (fase 8).
