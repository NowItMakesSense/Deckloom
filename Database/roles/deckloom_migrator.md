# deckloom_migrator

Introduced by feature: conta-jogador
Status: Approved (emenda de controle de acesso ao banco, 2026-10-23; ADR-0015)

## Purpose
Login usado **só por humanos** para aplicar as migrações EF Core já revisadas (geradas como arquivo em T-066). Separado do dono para que a credencial de login possa ser rotacionada, expirada ou desativada entre implantações sem mexer na posse dos objetos, e para que as conexões de migração sejam atribuíveis nos logs de conexão. Nunca é usado pela API nem por agentes.

## Attributes
`LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT` (o atributo `INHERIT` só define o padrão de novas associações; a única associação é criada explicitamente com `INHERIT FALSE`). Senha com hash SCRAM-SHA-256 (`password_encryption = scram-sha-256`, padrão do PostgreSQL 17).

## Memberships
Membro de `deckloom_owner` com `INHERIT FALSE, SET TRUE`, sem `ADMIN`. Efeito: sozinho, só tem `CONNECT`; para qualquer DDL precisa estar como `deckloom_owner`, e então tudo o que cria pertence ao dono.

## Grants
- Banco `deckloom`: `CONNECT`.
- Nada direto em schemas, tabelas ou sequências.

## Limits
- `CONNECTION LIMIT 2`.
- `ALTER ROLE deckloom_migrator IN DATABASE deckloom SET role = 'deckloom_owner'` (toda sessão começa como o dono; os scripts também começam com `SET ROLE deckloom_owner;`).
- `ALTER ROLE deckloom_migrator IN DATABASE deckloom SET search_path = pg_catalog` (as migrações qualificam tudo por schema; a tabela de histórico fica em `accounts`/`deck_suggestion`).
- `statement_timeout = '5min'` (cargas e índices de migração), `lock_timeout = '5s'` (uma migração não fica na fila de um lock segurando a API atrás dela), `idle_in_transaction_session_timeout = '5min'`.
- Fora do desenvolvimento: aceito só por `hostssl` com `scram-sha-256`, a partir do host de administração (fase 8). Manter `NOLOGIN` (ou `VALID UNTIL`) fora das janelas de implantação: deixado pelo usuário para a fase 8 (2026-10-23).

## Used by
Humano que aplica a migração: executa com `psql` o script gerado e revisado (ou `dotnet ef database update` com a connection string informada por ele mesmo na linha de comando), conectado como `deckloom_migrator`. Nenhum agente executa esse passo (o guard de banco bloqueia).

## Secret
`DECKLOOM_MIGRATOR_DB_PASSWORD` - no dev, só no ambiente do serviço `db` do compose (consumido pelo bootstrap) e com o humano que aplica migrações; fora do dev, no cofre do administrador. **Nunca** na configuração da API (`ConnectionStrings:*`, `appsettings*.json`, variáveis de ambiente do serviço `api`), nem em arquivo versionado, nem na `IDesignTimeDbContextFactory` (que usa connection string fictícia que nunca conecta).
