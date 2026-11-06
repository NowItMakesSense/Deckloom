# postgres

Introduced by feature: conta-jogador
Status: Approved (emenda de controle de acesso ao banco, 2026-10-23; ADR-0015)

## Purpose
Superusuário de bootstrap do cluster, criado pela imagem oficial do PostgreSQL na inicialização do volume. Serve só para administrar o cluster (configuração, papéis, extensões, recuperação) e para executar o script de bootstrap de papéis. **Nunca** é usado pela API, pelos jobs nem para aplicar migrações rotineiras.

## Attributes
`LOGIN SUPERUSER` (atributos de fábrica da imagem; não alterados). Em provedor gerenciado (fase 8) o equivalente é o usuário administrativo do provedor, que normalmente não é superusuário pleno; o bootstrap é adaptado lá.

## Memberships
Nenhuma associação criada pelo Deckloom. Por ser superusuário, consegue agir sobre qualquer papel.

## Grants
Todos, por ser superusuário. Responsável por executar, no bootstrap:
- `CREATE ROLE` de `deckloom_owner`, `deckloom_migrator` e `deckloom_app` com os atributos de cada arquivo;
- `GRANT deckloom_owner TO deckloom_migrator WITH INHERIT FALSE, SET TRUE`;
- `REVOKE ALL ON DATABASE deckloom FROM PUBLIC`; `GRANT CONNECT ON DATABASE deckloom TO deckloom_app, deckloom_migrator`; `GRANT CREATE ON DATABASE deckloom TO deckloom_owner`;
- `REVOKE ALL ON SCHEMA public FROM PUBLIC`;
- `ALTER DEFAULT PRIVILEGES FOR ROLE deckloom_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC`;
- os parâmetros e limites por papel descritos em cada arquivo.

O banco `deckloom` continua com dono `postgres` (criado pela imagem via `POSTGRES_DB`): o dono do banco pode removê-lo e alterar seus parâmetros, poder que não é dado a nenhum papel da aplicação.

## Limits
Sem parâmetros por papel (administração precisa de liberdade). Fora do ambiente de desenvolvimento, aceito só a partir da rede/host de administração (`pg_hba.conf`, fase 8), por `hostssl` ou socket local.

## Used by
Humano administrador; o entrypoint da imagem ao executar `/docker-entrypoint-initdb.d/01-roles.sh` na primeira inicialização do volume. Nenhum componente do Deckloom.

## Secret
`POSTGRES_PASSWORD` - só no ambiente do serviço `db` do compose e no cofre de segredos do administrador. Nunca na configuração da API, no `.env` com nome de configuração da API, em `appsettings*.json` nem em arquivo versionado (`.env.example` só com placeholder).
