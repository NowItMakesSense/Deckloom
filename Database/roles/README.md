# Roles

Papéis (roles) do PostgreSQL do Deckloom e seus privilégios. Descrição de projeto apenas - nenhum arquivo aqui contém senha, connection string ou saída de consulta a um banco real. Decisão: [ADR-0015](../../docs/adr/0015-papeis-e-privilegios-do-banco.md).

## Convenção

Um arquivo por papel: `<role_name>.md`, com as seções abaixo, mais [`privilege-matrix.md`](privilege-matrix.md) (privilégios de tabela do papel de aplicação, formato estrito lido por teste).

```
# <role_name>

Introduced by feature: <feature-slug>
Status: Approved | Approved

## Purpose
Para que o papel existe e o que ele nunca deve fazer.

## Attributes
LOGIN/NOLOGIN, SUPERUSER, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS, INHERIT - todos explícitos.

## Memberships
De quais papéis é membro e com quais opções (INHERIT, SET, ADMIN); quem é membro dele.

## Grants
Banco, schemas, tabelas (ou link para a matriz), sequências, funções. Também o que é explicitamente revogado.

## Limits
CONNECTION LIMIT e parâmetros por papel (`ALTER ROLE ... IN DATABASE deckloom SET ...`).

## Used by
Quem/qual componente usa o papel, e por qual caminho (pool da API, psql de um humano, bootstrap).

## Secret
Nome da variável/segredo que guarda a senha (nunca o valor), onde ele pode existir e onde nunca pode aparecer.
```

## Papéis

| Papel | Login | Usado por | Arquivo |
|---|---|---|---|
| `postgres` | sim | Humano administrador do cluster; script de bootstrap | [postgres.md](postgres.md) |
| `deckloom_owner` | não | Ninguém diretamente; dono dos schemas e objetos (assumido via `SET ROLE` pelo migrador) | [deckloom_owner.md](deckloom_owner.md) |
| `deckloom_migrator` | sim | Humano que aplica migrações revisadas | [deckloom_migrator.md](deckloom_migrator.md) |
| `deckloom_app` | sim | API (`ConnectionStrings:Deckloom`), incluindo os jobs em `BackgroundService` | [deckloom_app.md](deckloom_app.md) |

## Onde cada coisa é criada

| O quê | Por quem | Arquivo (para humano executar) |
|---|---|---|
| Papéis, senhas, atributos, associação migrador -> dono, `CONNECT`/`CREATE` no banco, revogações de `PUBLIC`, privilégios padrão (só revogação), parâmetros e limites por papel | `postgres`, uma vez por ambiente (bootstrap) | Dev: `db/init/01-roles.sh` montado em `/docker-entrypoint-initdb.d` (T-005). Outros ambientes: o mesmo conteúdo executado por um humano (fase 8) |
| Schemas, tabelas, índices, constraints, carga de catálogos, `USAGE` nos schemas e `GRANT` de tabela da matriz | `deckloom_migrator` agindo como `deckloom_owner` | Migrações EF Core geradas como arquivo (T-066), revisadas e aplicadas por humano |

Por que o bootstrap é um script de shell e não `.sql`: as senhas de `deckloom_app` e `deckloom_migrator` vêm de variáveis de ambiente do contêiner `db`; o script falha se alguma estiver vazia (`${VAR:?}`) e as passa ao `psql` como variáveis (`-v`), nunca como texto literal versionado. O script desliga o log de comandos na própria sessão (`SET log_statement = 'none'`) antes de `CREATE ROLE ... PASSWORD`, para que a senha não vá para o log do servidor.

## Regras

- Nenhum agente executa estes scripts, conecta ao banco ou lê senhas/connection strings (prime directive 2 do `CLAUDE.md`). Tudo aqui é especificação para humanos.
- Toda tabela nova entra em `privilege-matrix.md` **na mesma feature** que a cria, e a migração que cria a tabela contém os `GRANT` correspondentes.
- Nenhum `ALTER DEFAULT PRIVILEGES ... GRANT` para `deckloom_app` (só revogações): privilégio novo é sempre explícito.
- A API nunca recebe credencial de `postgres`, `deckloom_owner` ou `deckloom_migrator`.
- Trocar uma senha: humano, com `\password <papel>` no `psql` (envia o hash SCRAM, a senha não aparece em log), e atualização do segredo correspondente.

## Validação manual (humano, ambiente local, após aplicar as migrações)

O SQLite dos testes (ADR-0012) não executa papéis nem `GRANT`. Depois de aplicar bootstrap e migrações num banco local descartável, uma pessoa confere (consultas a cargo dela; agentes não executam):

1. `deckloom_app` não é superusuário nem tem `CREATEDB`, `CREATEROLE`, `REPLICATION`, `BYPASSRLS` (catálogo `pg_roles`) e não é membro de nenhum papel.
2. `deckloom_app` não tem `CREATE` nem `TEMPORARY` no banco nem `CREATE` em `accounts`, `deck_suggestion` e `public` (`has_database_privilege`, `has_schema_privilege`).
3. Os privilégios de tabela e de coluna de `deckloom_app` (`information_schema.role_table_grants` e `information_schema.column_privileges`) são exatamente os de `privilege-matrix.md`.
4. Todos os objetos de `accounts` e `deck_suggestion`, incluindo `__ef_migrations_history`, têm dono `deckloom_owner` (nenhum com dono `deckloom_migrator` ou `postgres`).
5. Um `INSERT` em `accounts.access_attempt` como `deckloom_app` funciona sem privilégio na sequência de identidade.
6. Os parâmetros por papel (`pg_db_role_setting`) e o `CONNECTION LIMIT` (`pg_roles.rolconnlimit`) são os dos arquivos de cada papel.
7. Como `deckloom_app`, `CREATE TABLE accounts.x (i int)` e `UPDATE accounts.player SET email = ...` falham com `42501`.
8. Fluxos ponta a ponta (cadastro, login, edição de perfil, avatar, redefinição, exclusão, job de limpeza) funcionam com a API conectada como `deckloom_app`.
