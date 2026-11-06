# deckloom_owner

Introduced by feature: conta-jogador
Status: Approved (emenda de controle de acesso ao banco, 2026-10-23; ADR-0015)

## Purpose
Dono dos schemas `accounts` e `deck_suggestion` e de todos os objetos dentro deles (tabelas, índices, constraints, sequências de identidade, tabelas `__ef_migrations_history`). Concentra o poder de DDL num papel **sem login**, que só é assumido, por `SET ROLE`, por quem aplica migrações. Nunca é usado pela API.

## Attributes
`NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT`. Sem senha.

## Memberships
Não é membro de nenhum papel. Membros: `deckloom_migrator` (`WITH INHERIT FALSE, SET TRUE`, sem `ADMIN`).

## Grants
- Banco `deckloom`: `CREATE` (para criar os schemas na primeira migração). Não é dono do banco.
- Schemas `accounts` e `deck_suggestion`: dono (criados pelas migrações, `EnsureSchema`).
- Tabelas, índices e sequências desses schemas: dono. Por isso concede aos demais papéis os `GRANT` da [matriz](privilege-matrix.md), dentro das migrações.
- Privilégios padrão: `ALTER DEFAULT PRIVILEGES FOR ROLE deckloom_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC` (aplicado pelo `postgres` no bootstrap). Nenhum privilégio padrão concede nada à aplicação.
- Schema `public`: nenhum privilégio (nada do Deckloom fica nele).

## Limits
Nenhum parâmetro próprio: como `NOLOGIN`, só existe dentro da sessão do `deckloom_migrator`, cujos limites valem.

## Used by
Ninguém diretamente. `deckloom_migrator` assume este papel (parâmetro padrão `role` e `SET ROLE deckloom_owner;` no início de cada script de migração), para que todo objeto nasça com este dono.

## Secret
Nenhum (papel sem login).
