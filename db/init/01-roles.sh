#!/usr/bin/env bash
# Bootstrap dos papeis do PostgreSQL (ADR-0015). Executado pelo entrypoint da imagem como postgres,
# so na primeira inicializacao do volume. Descricao dos papeis: Database/roles/*.md.
# Nao cria schemas nem tabelas (migracoes, T-066) e nao concede nada em tabelas.
# Por que .sh e nao .sql (UA4): um .sql em docker-entrypoint-initdb.d nao le variaveis de ambiente
# nem verifica se estao definidas; aqui as senhas vem do ambiente e a ausencia delas aborta (UA4).
set -euo pipefail

: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD nao definida}"
: "${DECKLOOM_APP_DB_PASSWORD:?DECKLOOM_APP_DB_PASSWORD nao definida}"
: "${DECKLOOM_MIGRATOR_DB_PASSWORD:?DECKLOOM_MIGRATOR_DB_PASSWORD nao definida}"

# As senhas chegam ao cliente SQL como variaveis (-v); o heredoc e quoted, entao o shell nao as expande no SQL.
psql --username "${POSTGRES_USER:-postgres}" --dbname "${POSTGRES_DB:-deckloom}" \
  -v ON_ERROR_STOP=1 \
  -v app_password="${DECKLOOM_APP_DB_PASSWORD}" \
  -v migrator_password="${DECKLOOM_MIGRATOR_DB_PASSWORD}" <<'SQL'
-- Evita que as senhas das instrucoes abaixo vao para o log do servidor.
SET log_statement = 'none';

CREATE ROLE deckloom_owner NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT;

CREATE ROLE deckloom_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS INHERIT
  CONNECTION LIMIT 2 PASSWORD :'migrator_password';

CREATE ROLE deckloom_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT
  CONNECTION LIMIT 30 PASSWORD :'app_password';

GRANT deckloom_owner TO deckloom_migrator WITH INHERIT FALSE, SET TRUE;

REVOKE ALL ON DATABASE deckloom FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE deckloom_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

GRANT CONNECT ON DATABASE deckloom TO deckloom_app, deckloom_migrator;
GRANT CREATE ON DATABASE deckloom TO deckloom_owner;

ALTER ROLE deckloom_app IN DATABASE deckloom SET search_path = pg_catalog;
ALTER ROLE deckloom_app IN DATABASE deckloom SET statement_timeout = '5s';
ALTER ROLE deckloom_app IN DATABASE deckloom SET lock_timeout = '2s';
ALTER ROLE deckloom_app IN DATABASE deckloom SET idle_in_transaction_session_timeout = '15s';

ALTER ROLE deckloom_migrator IN DATABASE deckloom SET search_path = pg_catalog;
ALTER ROLE deckloom_migrator IN DATABASE deckloom SET role = 'deckloom_owner';
ALTER ROLE deckloom_migrator IN DATABASE deckloom SET statement_timeout = '5min';
ALTER ROLE deckloom_migrator IN DATABASE deckloom SET lock_timeout = '5s';
ALTER ROLE deckloom_migrator IN DATABASE deckloom SET idle_in_transaction_session_timeout = '5min';
SQL
