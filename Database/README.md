# Database

This folder is the single, authoritative source of truth for **Deckloom**'s database schema, as a design artifact - not a connection to any real database.

## What belongs here
- `er-diagram.md` - the complete entity-relationship diagram for the whole project. Updated incrementally as features are built; never replaced wholesale.
- One subfolder per kind of database object, one file per object:
  - `tables/<table_name>.md`
  - `indexes/<index_name>.md`
  - `foreign-keys/<name>.md`
  - `primary-keys/<name>.md`
  - `packages/<name>.md`
  - `functions/<name>.md`
  - `roles/<role_name>.md` - papéis do banco (finalidade, atributos, associações, privilégios, limites, quem usa, nome do segredo), mais `roles/privilege-matrix.md`, a matriz de privilégios por tabela do papel de aplicação (formato estrito, lido por teste). Ver `roles/README.md` e ADR-0015.

Each per-object file is a design description (columns/types/constraints, source/target, signature, purpose, side effects) - see the `README.md` in each subfolder for its exact convention.

## Non-negotiable rules
- **Static design artifacts only.** No real connection string, no credentials, no dump or sample of real data. See `docs/security-policy.md` / `CLAUDE.md` - agents never access the system database, and that includes never generating a file here from a live query.
- A feature's own `docs/specs/<feature>/design.md` ("Feature diagram" section) only ever covers the entities/tables that feature touches. The complete, cross-feature picture lives only here, in `er-diagram.md`.
- Owned by the `architect` agent: every feature that touches the database updates this folder as part of phase 3, after `design.md` is approved - never silently, and never by deleting another feature's objects without the user's confirmation.

## Regras inegociáveis de controle de acesso (ADR-0015)
- **A aplicação nunca roda como superusuário nem como dona de objetos.** A API conecta só como `deckloom_app` (`ConnectionStrings:Deckloom`). Credenciais de `postgres` (bootstrap e administração do cluster) e de `deckloom_migrator` (migrações aplicadas por humano, agindo como o dono sem login `deckloom_owner`) nunca aparecem na configuração da API, em `appsettings*.json` nem em arquivo versionado.
- **Privilégio mínimo por tabela.** Toda tabela de `tables/` tem exatamente uma linha em `roles/privilege-matrix.md`, criada na mesma feature que introduz a tabela, e a migração que cria a tabela contém os `GRANT` correspondentes. Nenhum `ALTER DEFAULT PRIVILEGES ... GRANT` para o papel de aplicação; `TRUNCATE`, `REFERENCES`, `TRIGGER`, `CREATE` e posse de objetos nunca são concedidos a ele.
- **Nenhuma DDL em tempo de execução.** A API nunca chama `Database.Migrate()`/`MigrateAsync()`/`EnsureCreated()`/`EnsureDeleted()`/`GetPendingMigrations()`. Migrações são geradas como arquivo e aplicadas por humano como `deckloom_migrator`.
- **Endurecimento padrão** faz parte do bootstrap: `REVOKE ALL ON DATABASE ... FROM PUBLIC`, `REVOKE ALL ON SCHEMA public FROM PUBLIC`, `EXECUTE` em funções revogado de `PUBLIC` por padrão, `search_path` fixo por papel, timeouts e `CONNECTION LIMIT` por papel.
- **TLS** (`SSL Mode=VerifyFull`, `hostssl` + `scram-sha-256`) é obrigatório fora da rede interna do Docker Compose de desenvolvimento, única exceção documentada.
- Scripts de bootstrap e migrações são arquivos para um humano revisar e executar. Senhas vêm de variáveis de ambiente/cofre de segredos, nunca de literais. Agentes nunca os executam.
