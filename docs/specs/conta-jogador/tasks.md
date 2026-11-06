# Tasks: conta-jogador

Status: Approved | Date: 2026-10-23 | Approved by: dono do projeto (NowItMakesSense) (2026-10-23; reaprovado após a emenda de controle de acesso ao banco - ADR-0015, T-131..T-139)

Migração de formato de 2026-10-24 (skill new-project-setup 707a0fb): `Depends on` passou a `Blocked by`, e cada tarefa ganhou `Status` (T-001 a T-027 `done`, conforme os commits) e `Demo`. Nenhum escopo, REQ, ordem ou dependência foi alterado.

Emenda de 2026-10-23 (controle de acesso ao banco, ADR-0015, `design.md` > "Controle de acesso ao banco", aprovada em 2026-10-23): emendadas T-005, T-027, T-063, T-066, T-079 (nota), T-093 e T-094; novas T-131 a T-139 (Grupo H); atualizadas as seções de mitigações, itens não alocados, paralelização e validação manual por humano. T-001 a T-004 (concluídas e commitadas) não foram reescritas; a regra nova que estende T-002 é a T-131. Nenhum REQ foi alterado: as tarefas novas são habilitadoras ou reforçam REQ já cobertos.

Fonte da verdade: `requirements.md` (REQ-001..REQ-049, aprovado) e `design.md` (aprovado, inclusive a emenda ADR-0015), ADRs 0001-0013 e 0015. Este arquivo ordena a implementação (fase 5, TDD) em tarefas pequenas; nenhuma tarefa introduz comportamento fora dessas specs.

## Convenções (valem para todas as tarefas)

- **Ciclo**: cada tarefa é um ciclo red -> green -> refactor de uma sentada e um PR pequeno (Conventional Commits). Iron Law do `CLAUDE.md`: nenhum código de produção sem teste falhando antes; código escrito antes do teste é apagado e refeito.
- **Papéis** (ADR-0014): o `test` escreve o RED de **toda** tarefa. GREEN e REFACTOR são do `Owner:` da tarefa: `implementer` (backend, infraestrutura, lógica pura e testes ponta a ponta) ou `frontend` (telas, componentes, estilos e movimento), que aplica `.claude/skills/frontend-craft/` e confere o resultado renderizado. Na fase 6 o `ui-reviewer` revisa as tarefas `Owner: frontend`. O `designer` é o diretor de arte (design A.8) e não implementa.
- **Critério de `Owner:`**: `frontend` = tudo que é tela, componente, estilo ou movimento (T-095 a T-102, T-107 a T-120, T-127 a T-130). `implementer` = o resto. Casos limítrofes: `apiClient` (T-103), validadores/graphemes (T-104), mapa de mensagens (T-105) e roteamento/sessão (T-106) são lógica sem estilo e ficam com `implementer`; o foco no `<h1>` em transição de rota (na carga completa da página sem foco programático, A11Y-23/34; emenda de 2026-10-31, QA-03) e o título por rota (comportamento visual) foram separados em T-128 (`frontend`); os arquivos SVG dos presets foram separados de T-065 em T-129 (`frontend`); T-100 (`useSubmit` + RateLimitNotice) fica com `frontend` por ser inseparável do botão em loading e do aviso; T-003 (esqueleto/ferramentas do frontend) e os specs E2E T-121 a T-126 ficam com `implementer`, e qualquer defeito de UI que um E2E revelar volta como correção de `frontend` (T-127 concentra os ajustes de CSS e semântica).
- **DoD-F (definition of done extra para `Owner: frontend`)**: além do DoD-P, o `frontend` abre o resultado renderizado (largura de 320, 768 e 1280px, texto a 200%) e aplica as regras de `frontend-craft`; roda `inspect.mjs --app` a 1440, 768 e 390 (ART-09) quando houver Node 22 e Chrome e, se não puder, declara isso explicitamente no PR; usa as fixtures `?data=worst` (T-130) para os casos extremos A.8.5; os testes de CSS globais de T-095 (ART-01, 03, 04, 06, 07, 08) precisam estar verdes. Nas tarefas de frontend abaixo, "DoD-P" significa DoD-P + DoD-F.
- **Dependências transversais do frontend**: T-128 (foco/título por rota) e T-130 (fixtures de dados extremos) valem para todas as telas T-107 a T-120, além do `Blocked by:` listado em cada uma.
- **`Covers:`**: lista de REQ-IDs cobertos pela tarefa (formato lido por `.claude/scripts/check-traceability.py`). Tarefas de infraestrutura que não implementam comportamento de nenhum REQ usam `Covers: habilitadora (sem REQ direto)` e listam o que viabilizam em `Habilita:` (linha à parte, para não contar cobertura que a tarefa não entrega).
- **Nomes de teste**: backend `REQ_005_login_with_wrong_password_returns_generic_error`; frontend `it("REQ-005: ...")` (nome contém o REQ-ID). Critérios A11Y-xx/UX-xx do design aparecem no nome do teste que os verifica.
- **Dublês**: fakes em memória nomeados por papel, no projeto `Deckloom.Accounts.Testing`, ao lado das portas que implementam. Tempo sempre por `FakeTimeProvider`; atraso por `RecordingDelayer` (nunca dormir). Persistência: fakes em memória e, nos adapters EF Core, SQLite em memória criado e descartado pelo teste (ADR-0012). **Nenhum teste ou tarefa acessa o banco do sistema, `.env`, credenciais ou connection strings reais.**
- **Definition of done padrão (DoD-P)**: testes novos e existentes verdes (`dotnet test backend/Deckloom.sln` e/ou `npm --prefix frontend run test -- --run`); lint/format limpos (comandos do `CLAUDE.md`); cobertura dos arquivos tocados >= 80%; REFACTOR concluído com testes verdes; PR pequeno revisado, sem segredos, sem PII em logs. Cada tarefa abaixo acrescenta o que for específico dela.
- **Migrações**: tarefas só **geram arquivos** de migração para revisão e aplicação por humano; nenhum agente executa `dotnet ef database update`, scripts de banco, `docker exec` ou conecta a um PostgreSQL.
- **Docker Compose**: só o arquivo é produzido e validado por teste estático; ninguém sobe os contêineres contra dados reais. CI/CD é fase 8 e não está neste arquivo.
- **Controle de acesso ao banco (ADR-0015)**: papéis, `GRANT` e limites são especificação para humanos (`Database/roles/`); agentes só produzem arquivos e testes estáticos/com fakes. A API conecta como `deckloom_app` (sem DDL, sem posse, sem `UPDATE` fora das colunas da matriz); migrações são aplicadas por humano como `deckloom_migrator`. Recriar o volume `db-data` (`docker compose down -v`) apaga os dados locais e é **ação destrutiva exclusivamente humana**. O que o SQLite e os fakes não cobrem está na seção "Validação manual por humano (ADR-0015)" no fim deste arquivo.

## Grupo A - Fundação (walking skeleton)

## T-001: Esqueleto do backend (solution, projetos, pacotes centrais) com `Result` e `DomainError`
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: todas as tarefas de backend; comando Test/Lint/Build do `CLAUDE.md` (backend)
- Blocked by: none
- Demo: `dotnet build` e `dotnet test` da solution rodam verdes com a estrutura de projetos do design e o `ResultTests` passando.
- RED: em `Deckloom.Accounts.Domain.Tests`, `ResultTests` (`Success` expõe valor, `Failure` expõe `DomainError` com código estável, acesso ao valor de falha é erro) - falha porque a solution/projetos ainda não existem (`dotnet test backend/Deckloom.sln` não encontra nada).
- GREEN: criar `backend/Deckloom.sln`, `Directory.Packages.props` (gestão central, versões fixas, sem faixas flutuantes), `src/Deckloom.SharedKernel` (`Result<T>`, `DomainError`, base `DomainEvent`), `src/Modules/Accounts/Deckloom.Accounts.{Domain,Application,Infrastructure}`, `src/Modules/DeckSuggestion/Deckloom.DeckSuggestion.Infrastructure`, `src/Deckloom.Api` (host vazio), e os projetos de teste `tests/Deckloom.Accounts.{Domain,Application,Infrastructure}.Tests`, `tests/Deckloom.DeckSuggestion.Infrastructure.Tests` (dono do modelo, da migração e da carga de `deck_suggestion.format`, ADR-0007), `Deckloom.Accounts.Testing`, `Deckloom.Api.Tests`, `Deckloom.ArchitectureTests`. Referências conforme a camada (ADR-0001/0008). `Domain` sem nenhum PackageReference.
- REFACTOR: namespaces espelhando pastas; `Directory.Build.props` com `Nullable`, `TreatWarningsAsErrors`, `LangVersion`; sem código morto nos projetos vazios.
- Definition of done: DoD-P; `dotnet test backend/Deckloom.sln` e `dotnet build backend/Deckloom.sln -c Release` verdes; estrutura idêntica ao layout de `design.md` > "Layout do repositório".

## T-002: Teste de arquitetura - dependências apontam para dentro e regras de higiene de código
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: ADR-0001; mitigação "injeção de SQL" e "logs sem dados sensíveis" do threat model; todas as camadas
- Blocked by: T-001
- Demo: O teste de arquitetura passa e falha de propósito ao se introduzir uma referência ou chamada proibida.
- RED: em `Deckloom.ArchitectureTests`: (1) `Domain` não referencia nenhum projeto nem pacote além da BCL (inspeção do `.csproj` e dos assemblies); (2) `Application` referencia só `Domain` e `SharedKernel`; (3) `Infrastructure` referencia `Application`/`Domain`; (4) `Domain` e `Application` não referenciam `Infrastructure` nem `Api`; (5) tipos de `Infrastructure` só são usados em `Deckloom.Api` no namespace de composição `Deckloom.Api.Composition`; (6) proibido em `Domain`/`Application`: `DateTime.Now/UtcNow`, `DateTimeOffset.UtcNow`, `Thread.Sleep`, `Task.Delay` (usar `TimeProvider`/`IDelayer`); (8) `Deckloom.Api` (produção) não referencia nenhum projeto `*.Testing` nem `*.E2E.Host` (os fakes só chegam à API pelo host de teste e pela `WebApplicationFactory`); (7) proibido em `Infrastructure`: `FromSqlRaw`, `ExecuteSqlRaw`, `EnableSensitiveDataLogging` (apenas formas interpoladas parametrizadas). Provar o vermelho de cada regra adicionando temporariamente uma referência/chamada proibida e revertendo.
- GREEN: implementar as regras (NetArchTest ou reflexão sobre assemblies/`.csproj`); ajustar referências se alguma regra falhar.
- REFACTOR: mensagens de falha apontam a regra do design; regras como dados (lista), fáceis de estender para features futuras.
- Definition of done: DoD-P; as oito regras verdes e comprovadamente vermelhas com a violação temporária.

## T-003: Esqueleto do frontend (Vite, React, TypeScript, scripts de test/lint/format/build)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: comando Test/Lint/Build do `CLAUDE.md` (frontend); A11Y-10 e A11 (XSS) por lint; todas as tarefas de frontend
- Blocked by: none
- Demo: `npm run test/lint/format:check/build` do frontend rodam verdes com a página base sem violações do axe.
- RED: `App.test.tsx` (Vitest + React Testing Library) - renderiza `<App />` e espera um landmark `<main>` e passa no `axe` local sem violações; `npm --prefix frontend run test -- --run` falha porque o projeto não existe.
- GREEN: `frontend/` com Vite + React 19 + TS, React Router, Vitest + RTL + `@testing-library/user-event` + `axe-core` (helper `expectNoA11yViolations`) + MSW; scripts `test`, `lint`, `format`, `format:check`, `build`, `e2e` (placeholder do Playwright); ESLint com `eslint-plugin-jsx-a11y` (`no-positive-tabindex`) e regra `react/no-danger` como erro; Prettier; versões fixas em `package-lock.json`.
- REFACTOR: estrutura `src/app`, `src/design-system`, `src/features/conta`, `src/shared` conforme o design; alias de imports.
- Definition of done: DoD-P; `npm --prefix frontend run test -- --run`, `run lint`, `run format:check` e `run build` verdes.

## T-004: Comandos Test/Lint/Build do `CLAUDE.md` verdes de ponta a ponta
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: gate do `qa` e CI da fase 8 (comandos reais)
- Blocked by: T-001, T-003
- Demo: Os comandos Test, Lint e Build do `CLAUDE.md` rodam de ponta a ponta, verdes, a partir da raiz.
- RED: executar literalmente os três comandos do `CLAUDE.md` (Test, Lint/format, Build) - falham até que `.editorconfig`, analisadores e `dotnet format` estejam configurados e todos os projetos formatados.
- GREEN: `.editorconfig` na raiz, analisadores .NET, baseline `dotnet format backend/Deckloom.sln --verify-no-changes`, `.gitignore` (inclui `.env`, `bin/`, `obj/`, `node_modules/`), e ajustes mínimos até os três comandos saírem com código 0.
- REFACTOR: documentar em `README.md` da raiz só os comandos (sem segredos).
- Definition of done: DoD-P; os três comandos passam em sequência numa máquina limpa; nenhum arquivo `.env` rastreado.

## T-005: Docker Compose de desenvolvimento, nginx e `.env.example` (somente arquivos)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: topologia de dev do design (web, api, db, mail); `Referrer-Policy`/CSP no frontend (A9); Mailpit como captura de emails (ADR-0010); bootstrap dos papéis do banco (DB10, ADR-0015) para dev
- Blocked by: T-001, T-003
- Demo: `docker-compose.yml`, nginx e `.env.example` existem e o teste estático confirma papéis separados e nenhum segredo literal.
- Estado (2026-10-23): RED `T005_*` já escritos em `RepoHygieneTests`; `docker-compose.yml`, Dockerfiles e `nginx.conf` escritos e não commitados; esta emenda acrescenta os itens de banco abaixo (design > "Controle de acesso ao banco > Mudanças para o T-005").
- RED: `RepoHygieneTests` em `Deckloom.ArchitectureTests` (leitura estática dos arquivos, sem executar nada), na ordem:
  - Já escritos: `docker-compose.yml` define exatamente os serviços `web`, `api`, `db` (PostgreSQL 17, volume) e `mail` (Mailpit); `api` e `web` na mesma origem via proxy `/api`; volume para chaves do Data Protection; `.env.example` contém só placeholders (nenhum valor que pareça segredo) e nomes de configuração do design (`Auth:Google:ClientSecret`, `Auth:RateLimitHmacKey`, `Email:Smtp:*`, etc.); `.gitignore` ignora `.env`; `nginx.conf` envia `Content-Security-Policy` do design, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, `Permissions-Policy`; Dockerfile da API roda sem root; versões de imagem fixas (sem `latest`).
  - Novos, serviço `db`: `POSTGRES_USER: postgres` (superusuário de bootstrap, nome explícito), `POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}`, `POSTGRES_DB: deckloom`, `POSTGRES_HOST_AUTH_METHOD: scram-sha-256`; `DECKLOOM_APP_DB_PASSWORD` e `DECKLOOM_MIGRATOR_DB_PASSWORD` presentes **só** no `db`, por referência a variável (`${...}`), nunca literais; volume `./db/init:/docker-entrypoint-initdb.d:ro` (somente leitura); sem `ports` no `db`.
  - Novos, `db/init/01-roles.sh`: existe, usa `set -euo pipefail`, termina com LF (sem CRLF); exige as três senhas com `${POSTGRES_PASSWORD:?}`, `${DECKLOOM_APP_DB_PASSWORD:?}` e `${DECKLOOM_MIGRATOR_DB_PASSWORD:?}` (falha em variável vazia); chama `psql -v ON_ERROR_STOP=1` e passa as senhas por `-v` com `:'app_password'`/`:'migrator_password'` em heredoc com aspas (`<<'SQL'`); **nenhuma senha literal** (nenhum `PASSWORD '<literal>'`); a sessão começa com `SET log_statement = 'none'` antes de qualquer `CREATE ROLE`; cria `deckloom_owner` `NOLOGIN`, `deckloom_migrator` e `deckloom_app` com `NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS` (e `NOINHERIT` onde o design manda), a associação do migrador ao dono `WITH INHERIT FALSE, SET TRUE` e seu `SET role = 'deckloom_owner'`, `REVOKE ALL ON DATABASE deckloom FROM PUBLIC`, `REVOKE ALL ON SCHEMA public FROM PUBLIC`, `ALTER DEFAULT PRIVILEGES FOR ROLE deckloom_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC`, `search_path = pg_catalog` por papel e os limites de `deckloom_app` (`CONNECTION LIMIT 30`, `statement_timeout 5s`, `lock_timeout 2s`, `idle_in_transaction_session_timeout 15s`) e de `deckloom_migrator` (`CONNECTION LIMIT 2`, `5min`/`5s`/`5min`); **não** cria schema nem tabela e não contém `GRANT` em tabelas (isso é das migrações, T-066); os valores conferem com `Database/roles/*.md`.
  - Novos, serviço `api`: `ConnectionStrings__Deckloom` montada pelo compose com `Username=deckloom_app`, `Password=${DECKLOOM_APP_DB_PASSWORD}`, `SSL Mode=Disable` (exceção de dev, DB8), `Maximum Pool Size=20`, `Command Timeout=5`, `Timeout=5`, `Application Name=deckloom-api`; a connection string não contém `postgres`, `deckloom_migrator` nem `deckloom_owner` como usuário; o `api` não tem nenhuma variável `POSTGRES_*` nem `DECKLOOM_MIGRATOR_*`; `ASPNETCORE_ENVIRONMENT=Development` no `api` (condição para `SSL Mode=Disable`, T-136).
  - Novos, `.env.example`: contém placeholders para `POSTGRES_PASSWORD`, `DECKLOOM_APP_DB_PASSWORD` e `DECKLOOM_MIGRATOR_DB_PASSWORD` (valor vazio ou marcador explícito, nunca algo que pareça senha) e **não** contém mais `ConnectionStrings__Deckloom` como variável solta.
- GREEN: `docker-compose.yml`, `backend/Dockerfile`, `frontend/Dockerfile`, `frontend/nginx.conf`, `db/init/01-roles.sh`. **`.env.example` é escrito por um humano**: as regras de deny dos agentes cobrem todo caminho `.env*`, então o `implementer` não o cria nem o edita; o `implementer` entrega ao humano, na descrição do PR, o conteúdo exato esperado (nomes das variáveis e marcadores de placeholder) e os testes `T005_env_*` ficam vermelhos até o humano gravar o arquivo (único RED que depende de ação humana).
- REFACTOR: comentários mínimos sobre por que a mesma origem (cookies `__Host-`) e por que o bootstrap é `.sh` e não `.sql` (UA4); sem duplicar configuração; a lista de papéis/limites do teste é gerada de uma tabela de dados, não de asserts repetidos.
- Definition of done: DoD-P; testes estáticos verdes (incluindo `T005_env_*`, que dependem do arquivo escrito pelo humano); **o compose e `01-roles.sh` não são executados por nenhum agente nem contra dados**; nenhum segredo real versionado e nenhuma senha literal em arquivo versionado; **recriar o volume `db-data` (`docker compose down -v`) para o script rodar é ação destrutiva e só humana** (o script só roda em volume vazio; um volume inicializado pelo compose antigo, com `POSTGRES_USER: deckloom`, exige essa recriação, que apaga os dados locais e precisa de confirmação humana explícita); aplicar migrações no dev é passo humano (`psql` como `deckloom_migrator` dentro da rede do compose).

## Grupo B - Domínio (value objects, agregados, políticas)

## T-006: Value object `Email`
- Status: done
- Covers: REQ-003
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `Email` normalizando, validando e comparando endereços.
- RED: `EmailTests` em `Domain.Tests`: `trim` e minúsculas; vazio, sem `@`, dois `@`, parte local ou domínio vazio, domínio sem ponto ou com espaço, > 254 caracteres inválidos; pontos e `+` preservados; igualdade por valor normalizado (`REQ_003_*`).
- GREEN: `Email.Create(string) : Result<Email>` com as regras de `design.md` > value objects.
- REFACTOR: constantes nomeadas; mensagens como códigos (`required`, `invalid_format`).
- Definition of done: DoD-P; todos os limites da tabela cobertos.

## T-007: Value object `Password` (8 a 128 code points)
- Status: done
- Covers: REQ-003, REQ-030, REQ-031, REQ-038
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `Password` aceitando de 8 a 128 code points e rejeitando o resto.
- RED: `PasswordTests`: 7 -> `too_short`; 8 -> ok; `aaaaaaaa` aceito (sem composição); 128 -> ok; 129 -> `too_long`; contagem em code points Unicode (emoji fora do BMP conta 1); sem `trim` nem normalização; vazio -> `required`; `ToString` não expõe o valor (`REQ_038_*`).
- GREEN: `Password.Create` retornando `Result<Password>`; valor nunca persistível (sem serialização).
- REFACTOR: limites como constantes compartilhadas com o contrato de erro.
- Definition of done: DoD-P; fronteiras 7/8/128/129 verdes.

## T-008: Value object `DisplayName`
- Status: done
- Covers: REQ-015, REQ-037, REQ-039, REQ-040
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `DisplayName` aceitando de 3 a 30 caracteres e rejeitando o resto.
- RED: `DisplayNameTests`: NFC; `trim` de espaços Unicode nas bordas; `"  Bi  "` conta 2 -> `length`; 3 e 30 aceitos, 2 e 31 rejeitados; emoji composto (vários code points) conta 1 (2 letras + emoji = 3, aceito); quebra de linha e outros Cc rejeitados com `invalid_characters`; nome repetido permitido (não há unicidade no tipo); nome de 40 caracteres rejeitado sem truncar (`REQ_039_*`, `REQ_040_*`).
- GREEN: `DisplayName.Create` com `StringInfo.LengthInTextElements`.
- REFACTOR: extrair `TextRules` (NFC/trim/Cc) reutilizável por `Bio`.
- Definition of done: DoD-P.

## T-009: Value object `Bio`
- Status: done
- Covers: REQ-015, REQ-016, REQ-040
- Owner: implementer
- Blocked by: T-008
- Demo: Testes mostram `Bio` opcional de até 280 caracteres.
- RED: `BioTests`: vazia ou só espaços vira ausente; 280 aceita, 281 -> `too_long`; `\r\n` vira `\n`; quebra de linha aceita; NUL e demais Cc rejeitados (`invalid_characters`); contagem em grapheme clusters; NFC.
- GREEN: `Bio.Create(string?) : Result<Bio?>` reutilizando `TextRules`.
- REFACTOR: remover duplicação com `DisplayName`.
- Definition of done: DoD-P.

## T-010: `FavoriteTcg` (enumeração extensível, padrão `mtg`)
- Status: done
- Covers: REQ-015, REQ-017, REQ-044
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `FavoriteTcg` com `mtg` como padrão e a lista extensível.
- RED: `FavoriteTcgTests`: conjunto suportado = `{ mtg }` com rótulo "Magic: The Gathering"; `Default == mtg`; "Pokemon" -> `invalid`; teste de extensibilidade: registrar um segundo valor numa lista de suportados não altera a serialização/igualdade de `mtg`.
- GREEN: value object com tabela de valores suportados.
- REFACTOR: lista de suportados num único ponto.
- Definition of done: DoD-P.

## T-011: `FormatPreferences` (conjunto de `FormatId`, preservação de inativos)
- Status: done
- Covers: REQ-015, REQ-016, REQ-037, REQ-046
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `FormatPreferences` como conjunto, preservando formatos inativos.
- RED: `FormatPreferencesTests`: vazio aceito; duplicatas colapsadas; item novo inexistente ou inativo -> `invalid`; salvar mantém referências inativas já existentes; `VisibleIn(activeIds)` omite inativos; reativado volta a aparecer; "FormatoInventado" rejeitado (`REQ_046_*`, `REQ_015_*`).
- GREEN: `FormatPreferences.Update(requested, activeIds)` e `Visible(activeIds)`.
- REFACTOR: nomes que evitem confundir "removido" com "inativo".
- Definition of done: DoD-P.

## T-012: Value object `Avatar` (`PresetAvatar` | `CustomAvatar`)
- Status: done
- Covers: REQ-018, REQ-022
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `Avatar` sendo exatamente um entre preset e customizado.
- RED: `AvatarTests`: soma fechada (exatamente um); `PresetAvatar` exige `PresetId` não vazio; `CustomAvatar` exige `StorageKey`; criação padrão usa o preset padrão informado; igualdade por valor.
- GREEN: tipos em `Domain`.
- REFACTOR: sem herança aberta (hierarquia selada).
- Definition of done: DoD-P.

## T-013: `TermsAcceptance` (versão, data/hora, 18+)
- Status: done
- Covers: REQ-033, REQ-034, REQ-043, REQ-048
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `TermsAcceptance` registrando versão, data/hora e maioridade.
- RED: `TermsAcceptanceTests`: versão igual à vigente + `AdultDeclared = true` + `AcceptedAt` -> ok; sem aceite -> `required`; versão diferente -> `version_mismatch`; sem 18+ -> `required` (campo de maioridade); imutável (sem setters).
- GREEN: `TermsAcceptance.Create(currentVersion, submittedVersion, adultDeclared, accepted, now)`.
- REFACTOR: códigos de erro por campo alinhados ao contrato (`acceptTerms`, `adultDeclaration`).
- Definition of done: DoD-P.

## T-014: `PasswordHash` e `ExternalIdentity`
- Status: done
- Covers: REQ-007, REQ-013
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram `PasswordHash` e `ExternalIdentity` sem expor a senha nem aceitar subject vazio.
- RED: `PasswordHash` aceita só o formato `v1$pbkdf2-sha256$<it>$<salt>$<hash>` e `ToString`/serialização nunca imprimem o valor; `ExternalIdentity` exige `Provider = google` e `Subject` não vazio; igualdade por (provedor, subject) (`REQ_013_password_hash_never_in_string_representation`).
- GREEN: dois value objects.
- REFACTOR: `PasswordHash.Parse` isolado para o adapter usar.
- Definition of done: DoD-P.

## T-015: `Player.RegisterWithPassword` e `Player.RegisterWithGoogle` (J1-J4, J6)
- Status: done
- Covers: REQ-001, REQ-006, REQ-022, REQ-033, REQ-034, REQ-044, REQ-048
- Owner: implementer
- Blocked by: T-006, T-007, T-008, T-010, T-012, T-013, T-014
- Demo: Testes mostram o `Player` sendo criado por senha ou por Google, com os eventos corretos.
- RED: `PlayerRegistrationTests`: conta por senha tem `PasswordHash` e nenhum `ExternalIdentity`; conta Google tem `ExternalIdentity` e nenhum `PasswordHash` (J2); ambas nascem com `FavoriteTcg = mtg`, avatar preset padrão, sem Formatos, `TermsAcceptance` com versão/data/18+; emite `PlayerRegistered(playerId, method, termsVersion)` sem email/hash; sem aceite ou sem 18+ não cria; nome inicial do Google respeita as mesmas regras de `DisplayName`.
- GREEN: fábricas e evento.
- REFACTOR: construtor privado; `PlayerId` tipado.
- Definition of done: DoD-P.

## T-016: `Player.UpdateProfile` (J5, tudo ou nada)
- Status: done
- Covers: REQ-014, REQ-015, REQ-016, REQ-046
- Owner: implementer
- Blocked by: T-015, T-009, T-011
- Demo: Testes mostram `UpdateProfile` aplicando tudo ou nada.
- RED: `UpdateProfileTests`: alteração válida persiste nome, bio, TCG, Formatos e emite `ProfileUpdated(changedFields)`; qualquer campo inválido rejeita tudo e mantém o estado; bio e formatos vazios aceitos; Formato inativo preservado ao salvar sem ele no pedido; nome repetido aceito.
- GREEN: `UpdateProfile(displayName, bio, favoriteTcg, formatIds, activeFormatIds)`.
- REFACTOR: validação agregada que devolve todos os erros por campo, não só o primeiro.
- Definition of done: DoD-P.

## T-017: Comportamentos de avatar do `Player` (J6)
- Status: done
- Covers: REQ-018, REQ-021
- Owner: implementer
- Blocked by: T-015
- Demo: Testes mostram o jogador trocando entre preset e avatar customizado.
- RED: `PlayerAvatarTests`: `ChooseAvatarPreset` define o preset; `SetCustomAvatar(key)` define o custom; trocar de custom para outro custom ou para preset emite `AvatarChanged(PreviousStorageKey = chave anterior)`; trocar de preset para preset não emite chave anterior.
- GREEN: métodos e evento.
- REFACTOR: evento carrega só identificadores.
- Definition of done: DoD-P.

## T-018: `Player.LinkGoogle` (J7)
- Status: done
- Covers: REQ-007, REQ-008, REQ-036
- Owner: implementer
- Blocked by: T-015
- Demo: Testes mostram `LinkGoogle` vinculando a identidade e recusando duplicidade.
- RED: `LinkGoogleTests`: vincula só se o Jogador tem senha local, o email verificado do Google é igual ao `Email` e há `PasswordProof` válida; sem prova -> erro e nada vinculado; segunda identidade Google -> erro (J7); conta sem senha local não vincula; emite `GoogleIdentityLinked(playerId)`.
- GREEN: `LinkGoogle(identity, verifiedEmail, PasswordProof)`.
- REFACTOR: `PasswordProof` como tipo que só o caso de uso consegue criar (construtor interno/fábrica de aplicação).
- Definition of done: DoD-P.

## T-019: `Player` - redefinição de senha (J8)
- Status: done
- Covers: REQ-029, REQ-030, REQ-031, REQ-038
- Owner: implementer
- Blocked by: T-015, T-007
- Demo: Testes mostram a redefinição de senha com token de uso único e validade.
- RED: `PasswordResetTests` com `FakeTimeProvider`: `IssuePasswordReset(tokenHash, now)` invalida o token anterior (no máximo 1) e define validade de 60 min; só para Jogador com senha local; `ResetPassword(newHash, now)` aceita a 59:59, rejeita a 60:01 e depois de usado (`reset_link_invalid`); sucesso troca o hash, consome o token e emite `PasswordChanged`; só o hash do token é guardado.
- GREEN: `PasswordResetToken` interno e os dois métodos.
- REFACTOR: janela de 60 min como constante de política.
- Definition of done: DoD-P.

## T-020: `Player.Delete` e `Reauthentication` (J9)
- Status: done
- Covers: REQ-023, REQ-024, REQ-026, REQ-042
- Owner: implementer
- Blocked by: T-015
- Demo: Testes mostram `Player.Delete` exigindo `Reauthentication` recente.
- RED: `DeletePlayerTests`: sem `confirm` -> erro e estado intacto; conta com senha exige `PasswordProof`; conta sem senha exige `RecentGoogleLogin` com `ReauthenticatedAt` <= 5 min (4:59 aceito, 5:01 -> `reauth_expired`, nunca reautenticou -> `reauth_required`) mais confirmação final; sucesso emite `PlayerDeleted(playerId)` sem dados pessoais.
- GREEN: `Delete(confirmation, reauthentication, now)` e `Reauthentication`.
- REFACTOR: janela de 5 min em constante única, usada também pela `Session`.
- Definition of done: DoD-P.

## T-021: Agregado `Session` (S1-S6)
- Status: done
- Covers: REQ-010, REQ-011, REQ-012, REQ-030, REQ-035, REQ-042
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram a `Session` sendo criada, estendida, expirada e revogada.
- RED: `SessionTests` com `FakeTimeProvider`: sem lembrar-me, válida enquanto `now - LastSeenAt < 30 min` (29 min ok, 31 min expirada) e cada uso renova `LastSeenAt`; com lembrar-me válida até `CreatedAt + 30 dias` (29 dias ok, 31 dias expirada) e não renova a validade; revogada nunca volta a valer (idempotente); só guarda o hash do token (256 bits); `MarkReauthenticated(linked, presented, now)` só marca com o mesmo `subject` e `ToRecentGoogleLogin()` leva o `ReauthenticatedAt` (a janela de 5 min é aplicada pelo `Player.Delete`, T-020). Emenda 2026-10-23 (T-021, design.md bloco "Agregado `Session`"): `RevokeAllFor(player)` é do repositório (T-024, T-057), não do agregado.
- GREEN: `Session` com `IsValid(now)`, `Touch`, `Revoke`, `MarkReauthenticated`.
- REFACTOR: evitar estado mutável público; eventos `SessionStarted`/`SessionRevoked`.
- Definition of done: DoD-P.

## T-022: `ProgressiveDelayPolicy` (T1-T3)
- Status: done
- Covers: REQ-032, REQ-041
- Owner: implementer
- Blocked by: T-001
- Demo: Testes mostram o atraso crescer a cada falha repetida, sem bloquear a conta.
- RED: `ProgressiveDelayPolicyTests` cobrindo a tabela da ADR-0005: conta 0-2 -> 0 s, 3 -> 1 s, 4 -> 2 s, 5 -> 4 s, 6 -> 8 s, >=7 -> 10 s; IP 0-9 -> 0 s, 10 -> 1 s, 11 -> 2 s, 12 -> 4 s, 13 -> 8 s, >=14 -> 10 s; atraso = máximo entre as duas chaves; monotonicamente crescente; teto de 10 s; nunca bloqueia (sem resultado "bloqueado"); ação `registration` usa só a chave de IP (contagem de conta ignorada).
- GREEN: função pura `DelayFor(accountCount, ipCount, action)` com `ProgressiveDelayOptions`.
- REFACTOR: limiares em opções configuráveis.
- Definition of done: DoD-P; teste de propriedade para monotonicidade.

## T-023: Eventos de domínio sem dados pessoais
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: NFR de logs sem PII; "Eventos de domínio" do design
- Blocked by: T-015, T-017, T-019, T-020, T-021
- Demo: Testes mostram eventos de domínio emitidos sem dados pessoais.
- RED: teste de reflexão em `Domain.Tests`: nenhum tipo `DomainEvent` possui propriedade de tipo string chamada email, password, hash, token, name, bio nem IP; só ids, enums, `DateTimeOffset` e a versão dos Termos (`TermsVersion`, exigida pela tabela de eventos do design; emenda 2026-10-23, aprovada pelo usuário).
- GREEN: ajustar eventos que violarem.
- REFACTOR: base `DomainEvent` com `OccurredAt` via `TimeProvider`. Emenda 2026-10-23 (T-023, design.md "Regras dos eventos"): lista fechada de tipos (ids, enums, `DateTimeOffset`, `bool`) com strings só em `PlayerRegistered.TermsVersion`, `ProfileUpdated.ChangedFields` e `AvatarChanged.PreviousStorageKey`; `OccurredAt` carimbado por `AggregateRoot.PullDomainEvents(TimeProvider)`.
- Definition of done: DoD-P.

## T-024: Portas de repositório e repositórios em memória
- Status: done
- Covers: REQ-002, REQ-027
- Owner: implementer
- Blocked by: T-015, T-021
- Demo: Testes rodam contra repositórios em memória com commit tudo ou nada.
- RED: em `Deckloom.Accounts.Testing` (testes do próprio fake em `Application.Tests`): `InMemoryPlayerRepository` rejeita segundo `Add` com o mesmo `Email` simulando a violação de unicidade (corrida de cadastro); `Remove` libera o email e um novo cadastro gera outro `PlayerId` (J1); `FindByGoogleSubject`, `FindByResetTokenHash`, `ExistsByEmail` coerentes; `InMemorySessionRepository.RevokeAllFor` revoga todas; `FindByTokenHash` por hash.
- GREEN: interfaces `IPlayerRepository` e `ISessionRepository` no `Domain` e os dois fakes.
- REFACTOR: fakes ao lado das portas, sem lógica de negócio duplicada.
- Emenda 2026-10-23 (design.md, "Forma dos repositórios"): a unicidade do email estoura no `CommitAsync` de um `IUnitOfWork` mínimo (Application) com `UniqueConstraintViolationException` (Domain); o T-024 entrega esse `IUnitOfWork` mínimo e o `InMemoryUnitOfWork` só com commit, completado no T-027.
- Definition of done: DoD-P.

## Grupo C - Aplicação (casos de uso com fakes)

## T-025: Portas de aplicação e fakes de segurança/tempo
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: `IPasswordHasher`/`FakePasswordHasher`, `ISecureTokenGenerator`/`SequentialTokenGenerator`, `ITokenHasher`, `IKeyedHasher`, `IDelayer`/`RecordingDelayer`, `ITermsVersionProvider`/`FixedTermsVersionProvider`, `TimeProvider`/`FakeTimeProvider` (REQ-001, REQ-029, REQ-032, REQ-033)
- Blocked by: T-024
- Demo: Casos de uso conseguem rodar com fakes de segurança, tempo e termos.
- RED: testes dos fakes: `FakePasswordHasher` (hash determinístico, `Verify` coerente, registra chamadas, `NeedsRehash`), `SequentialTokenGenerator` (sequência previsível e distinta), `RecordingDelayer` (registra atrasos e não espera), `FixedTermsVersionProvider`.
- GREEN: interfaces em `Application`, fakes em `Testing`.
- REFACTOR: nomes por papel.
- Definition of done: DoD-P.

## T-026: Portas e fakes de catálogos
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: `IFormatCatalog`/`InMemoryFormatCatalog` com ativar/inativar, `IAvatarPresetCatalog`/`InMemoryAvatarPresetCatalog` com preset padrão (REQ-018, REQ-022, REQ-037, REQ-046)
- Blocked by: T-025
- Demo: Casos de uso conseguem rodar com fakes dos catálogos de formatos e avatares.
- RED: testes dos fakes: só ativos listados e ordenados; `Contains(id)`; inativar e reativar muda a listagem; exatamente um preset padrão.
- GREEN: interfaces e fakes.
- REFACTOR: catálogo de TCGs favoritos como lista de domínio, não porta.
- Definition of done: DoD-P.

## T-027: Portas e fakes de estado (acessos, pendências Google, unidade de trabalho)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: `IAccessAttemptStore`, `IPendingGoogleAuthStore`, `IUnitOfWork` (REQ-032, REQ-036, REQ-039, REQ-041, REQ-047)
- Blocked by: T-025
- Demo: Casos de uso conseguem rodar com fakes de estado e unidade de trabalho com rollback.
- RED: `InMemoryAccessAttemptStore` conta por janela de 15 min e por chave (conta/IP), apaga por `account_key_hash` e por idade (> 24 h); `InMemoryPendingGoogleAuthStore` cria pendência com TTL 10 min, `Consume` atômico e de uso único (dois consumos concorrentes: só um vence), erro não consome; `InMemoryUnitOfWork` confirma ou descarta mudanças dos fakes (rollback restaura) e expõe `IsTransactionOpen` (verdadeiro só dentro do bloco de persistência; falso antes de abrir, depois de confirmar e depois de descartar), usado pelas sondas de T-137 (emenda ADR-0015: transação só em volta da persistência).
- GREEN: interfaces e fakes.
- REFACTOR: lógica de janela isolada para reuso nos testes.
- Emenda 2026-10-23 (T-024): `IUnitOfWork.CommitAsync` e a checagem de unicidade no commit já existem desde o T-024; aqui entram descarte/rollback e `IsTransactionOpen`. Os repositórios em memória guardam referências, então o rollback precisa de estratégia própria (ex.: registro de mudanças pendentes no UoW).
- Emenda 2026-10-23 (T-027, design.md, "Forma das portas de estado"): rollback por registro de mudanças pendentes; `IUnitOfWork.ExecuteAsync(bloco)` e `IsTransactionOpen`/`TransactionOpenDuringLastCommit` no `InMemoryUnitOfWork`; `InMemoryAccessAttemptStore(uow)` e `InMemoryPendingGoogleAuthStore(TimeProvider)`. Acréscimo ao T-024: `InMemorySessionRepository.RevokeAllFor` passa a ser mudança pendente (conta na hora, `Revoke` só no commit, descarte cancela), para a T-049 reverter as sessões se o purger falhar; os testes de `RevokeAllFor` do T-024 passam a chamar `CommitAsync` antes de afirmar o efeito.
- Definition of done: DoD-P.

## T-028: Fakes das portas de saída (notificador, purger, armazenamento e processador de avatar, verificador Google)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: `IPasswordResetNotifier`/`RecordingPasswordResetNotifier`, `IPlayerDecksPurger`/`RecordingPlayerDecksPurger`, `IAvatarStorage`/`InMemoryAvatarStorage`, `IAvatarImageProcessor`/`FakeAvatarImageProcessor`, `IGoogleIdentityVerifier`/`FakeGoogleIdentityVerifier` (REQ-019, REQ-025, REQ-028, REQ-006)
- Blocked by: T-025
- Demo: Casos de uso conseguem rodar com fakes de notificador, purger, avatar e verificador Google.
- RED: testes dos fakes: o notificador grava (email, link) e permite falha injetada; o purger registra os `PlayerId` e participa do rollback do `IUnitOfWork`; o armazenamento salva/lê/remove por chave; o processador fake devolve `Accepted(bytes)` ou `Rejected(size|type|content)` programável; o verificador Google devolve `GoogleVerifiedIdentity`, cancelamento ou falha programáveis.
- GREEN: interfaces e fakes.
- REFACTOR: DTO `GoogleVerifiedIdentity` (subject, email, emailVerified, nome) sem foto nem tokens (REQ-022).
- Definition of done: DoD-P.

## T-029: `AccessThrottle` (aplicação do atraso progressivo)
- Status: done
- Covers: REQ-032, REQ-041
- Owner: implementer
- Blocked by: T-022, T-027, T-025
- Demo: Testes mostram o atraso progressivo sendo aplicado por IP e por conta sem dormir de verdade.
- RED: `AccessThrottleTests`: conta eventos por HMAC de conta e de IP na janela, chama `IDelayer` com o atraso da política, registra o evento conforme a ação (login/vínculo/exclusão registram falhas; redefinição registra toda solicitação; cadastro registra só chave de IP em recusa por email em uso), calcula chaves também para email inexistente (anti-enumeração), sucesso de login apaga falhas da chave de conta, nunca lança "bloqueado", Jogador legítimo entra após o atraso de outro IP.
- GREEN: serviço `AccessThrottle` com `BeforeAttempt(action, email?, ip)` e `RecordFailure/Record`.
- REFACTOR: ações como enum; HMAC só via `IKeyedHasher`.
- Definition of done: DoD-P.

## T-030: Caso de uso `RegisterWithPassword` - caminho feliz
- Status: done
- Covers: REQ-001, REQ-022, REQ-033, REQ-044, REQ-048
- Owner: implementer
- Blocked by: T-015, T-024, T-025, T-026, T-027, T-029
- Demo: Um teste de aplicação cadastra um jogador com senha e recebe a sessão.
- RED: `RegisterWithPasswordTests`: cadastro válido cria `Player` com hash (nunca a senha em claro), avatar preset padrão, `favoriteTcg = mtg`, aceite com data/hora/versão/18+, abre sessão sem lembrar-me e não pede verificação de email; estado persistido apenas após sucesso (unidade de trabalho); resposta é `SessionSummary` sem email nem hash.
- GREEN: caso de uso mínimo.
- REFACTOR: montagem de `SessionSummary` extraída (reutilizada por login).
- Definition of done: DoD-P.

## T-031: `RegisterWithPassword` - validação por campo
- Status: done
- Covers: REQ-003, REQ-033, REQ-034, REQ-038, REQ-040, REQ-043
- Owner: implementer
- Blocked by: T-030
- Demo: Um teste de aplicação mostra cada campo inválido do cadastro com seu erro.
- RED: tabela de casos (Scenario Outline de REQ-003 e REQ-038): email inválido/vazio, senha vazia, 7 caracteres, 129 caracteres, `aaaaaaaa` aceito, 128 aceito; nome com Cc; sem aceite; sem 18+; versão de termos divergente -> `version_mismatch`; todos os erros por campo devolvidos juntos; nenhuma conta criada, nenhum hash calculado quando a senha é inválida.
- GREEN: validação agregada antes de hash e persistência.
- REFACTOR: coletor de erros reutilizável pelos demais casos de uso.
- Definition of done: DoD-P.

## T-032: `RegisterWithPassword` - email indisponível, atraso por IP e corrida
- Status: done
- Covers: REQ-002, REQ-027, REQ-041
- Owner: implementer
- Blocked by: T-031
- Demo: Testes mostram email já usado, atraso por IP e corrida entre dois cadastros tratados.
- RED: email existente -> `email_unavailable` sem criar nem alterar conta; a recusa grava evento só com chave de IP e não toca a chave de conta (login legítimo sem atraso causado por isso); IP com várias recusas recebe atraso crescente sem bloqueio; email de conta excluída cadastra novo Jogador sem dados anteriores; dois cadastros concorrentes do mesmo email (fake com violação de unicidade): um vence, o outro recebe `email_unavailable`, nunca erro 500.
- GREEN: tratamento da violação de unicidade e do evento de recusa.
- REFACTOR: `email_unavailable` como único caminho de erro para a existência prévia.
- Definition of done: DoD-P.

## T-033: Caso de uso `LoginWithPassword`
- Status: done
- Covers: REQ-004, REQ-005, REQ-027, REQ-032, REQ-035, REQ-038
- Owner: implementer
- Blocked by: T-030, T-029
- Demo: Um teste de aplicação faz login com senha e recebe a sessão; credencial errada dá erro genérico.
- RED: `LoginWithPasswordTests`: credenciais corretas abrem sessão com token novo a cada login (sem fixação); senha errada, email inexistente, conta excluída e conta só-Google retornam o mesmo `invalid_credentials`; para inexistente e sem senha local o `FakePasswordHasher` registra uma verificação contra hash fictício (tempo equalizado); senha > 128 devolve `invalid_credentials` sem chamar o hasher; `rememberMe` gera sessão de 30 dias; atraso progressivo aplicado antes e falha registrada; sucesso apaga falhas da conta; `NeedsRehash` regrava o hash.
- GREEN: caso de uso.
- REFACTOR: verificação fictícia isolada em `CredentialVerifier`.
- Definition of done: DoD-P; teste prova que todos os ramos de falha chamam o hasher exatamente uma vez (exceto senha > 128).

## T-034: `AuthenticateSession` e `Logout`
- Status: done
- Covers: REQ-010, REQ-011, REQ-012, REQ-035
- Owner: implementer
- Blocked by: T-021, T-024, T-025
- Demo: Testes mostram a sessão sendo autenticada e encerrada no logout.
- RED: `AuthenticateSessionTests`: sem token, token desconhecido, expirado (31 min sem lembrar-me; 31 dias com lembrar-me) e revogado -> `unauthenticated` sem dados; 29 min/29 dias válidos e renovam `LastSeenAt` no caso sem lembrar-me; `Logout` revoga no servidor (token reutilizado -> não autenticado), é idempotente, e funciona sem sessão.
- GREEN: dois casos de uso.
- REFACTOR: lookup por `ITokenHasher`.
- Definition of done: DoD-P.

## T-035: `RequestPasswordReset`
- Status: done
- Covers: REQ-028, REQ-029, REQ-032
- Owner: implementer
- Blocked by: T-019, T-029, T-028, T-025
- Demo: Testes mostram o pedido de redefinição respondendo igual para email existente ou não.
- RED: `RequestPasswordResetTests`: conta email+senha -> emite token (SHA-256 guardado, claro só no notificador), invalida o anterior e enfileira o link `{PublicBaseUrl}/redefinir-senha#token=<token>`; inexistente, só-Google e falha do notificador -> mesmo resultado genérico sem email enviado; atraso por conta e IP aplicado e solicitação sempre registrada; o token em claro nunca aparece em evento nem log; resultado idêntico entre os casos (mesmo tipo/valor).
- GREEN: caso de uso.
- REFACTOR: `PublicBaseUrl` vem de opções.
- Definition of done: DoD-P.

## T-036: `ResetPassword`
- Status: done
- Covers: REQ-030, REQ-031, REQ-038
- Owner: implementer
- Blocked by: T-035, T-021
- Demo: Testes mostram a senha trocada com o token e as sessões antigas revogadas.
- RED: `ResetPasswordTests`: token válido + senha 8-128 -> troca o hash, consome o token, revoga todas as sessões do Jogador na mesma unidade de trabalho e não abre sessão; token expirado (60:01), usado, inexistente -> `reset_link_invalid` sem revelar conta; senha de 7 ou 129 caracteres -> erro de campo e o token **não** é consumido (segundo envio válido funciona); dois consumos concorrentes: só um vence.
- GREEN: caso de uso com validação da senha antes do consumo.
- REFACTOR: reutiliza `Password.Create`.
- Definition of done: DoD-P.

## T-037: `HandleGoogleCallback` - conta existente e falhas
- Status: done
- Covers: REQ-007, REQ-009
- Owner: implementer
- Blocked by: T-018, T-028, T-024, T-021
- Demo: Testes mostram o retorno do Google entrando em conta existente e tratando as falhas.
- RED: `HandleGoogleCallbackExistingTests`: `subject` já vinculado -> sessão na conta existente sem duplicar (busca primeiro por `subject`); cancelamento (`error=access_denied`), falha do verificador e email não verificado -> resultado `falhou` sem conta nem sessão e mesma mensagem; indisponibilidade do Google não afeta `LoginWithPassword` (degradação graciosa).
- GREEN: ramos de `ok` e `falhou`.
- REFACTOR: resultado como união (`ok`, `concluir`, `vincular`, `reautenticado`, `falhou`, `reautenticacao-falhou`).
- Definition of done: DoD-P.

## T-038: `HandleGoogleCallback` - email novo e email de conta com senha
- Status: done
- Covers: REQ-006, REQ-008, REQ-039
- Owner: implementer
- Blocked by: T-037, T-027
- Demo: Testes mostram o retorno do Google com email novo e com email de conta com senha.
- RED: `HandleGoogleCallbackNewTests`: email verificado sem conta -> cria `PendingGoogleSignup` (subject, email, nome do Google sem truncar, ou nulo), **não cria conta nem sessão**; email de conta email+senha sem vínculo Google -> `PendingGoogleLink` para o `PlayerId` alvo, sem sessão nem segunda conta; conta Google sem senha com outro `subject` não é vinculada; pendência com TTL de 10 min.
- GREEN: ramos `concluir` e `vincular`.
- REFACTOR: criação de pendência extraída para o store.
- Definition of done: DoD-P.

## T-039: `StartGoogleAuth`/`HandleGoogleCallback` - reautenticação para exclusão
- Status: done
- Covers: REQ-023, REQ-042
- Owner: implementer
- Blocked by: T-037, T-021
- Demo: Testes mostram o Google reautenticando o jogador para excluir a conta.
- RED: `GoogleReauthTests`: `intent=reauth` exige sessão válida; pede `prompt=login` e `max_age=300`; callback exige `auth_time` nos últimos 5 min e `subject` igual ao vinculado; sucesso marca `ReauthenticatedAt` na sessão (resultado `reautenticado`); `auth_time` antigo, subject diferente ou falha -> `reautenticacao-falhou` sem marcar; `returnTo` ignorado na reautenticação.
- GREEN: ramo de reautenticação.
- REFACTOR: janela de 5 min compartilhada com `Session`.
- Definition of done: DoD-P.

## T-040: `GetPendingGoogleAuth` e `CancelPendingGoogleAuth`
- Status: done
- Covers: REQ-006, REQ-008, REQ-039
- Owner: implementer
- Blocked by: T-038
- Demo: Testes mostram a leitura e o cancelamento de uma pendência Google.
- RED: leitura devolve `{kind, email, suggestedDisplayName}` (nome de 40 caracteres sem truncar; `null` se o Google não informou); pendência expirada (10:01) ou inexistente -> `pending_not_found`; cancelar descarta e é idempotente; leitura não consome.
- GREEN: dois casos de uso.
- REFACTOR: DTO sem tokens do Google.
- Definition of done: DoD-P.

## T-041: `CompleteGoogleSignup`
- Status: done
- Covers: REQ-006, REQ-022, REQ-033, REQ-034, REQ-039, REQ-040, REQ-043, REQ-044, REQ-048
- Owner: implementer
- Blocked by: T-038, T-040, T-031
- Demo: Testes mostram o cadastro Google sendo concluído com nome, termos e maioridade.
- RED: `CompleteGoogleSignupTests`: nome editado 3-30 + aceite + 18+ -> cria conta sem senha local, avatar preset padrão (foto do Google ignorada), `mtg`, aceite com 18+, abre sessão e consome a pendência na mesma unidade de trabalho; `"  Bi  "` rejeitado; 2 letras + emoji composto aceito; nome de 40 caracteres sem editar rejeitado com `length`; Cc rejeitado; sem aceite, sem 18+ ou versão divergente -> erro de campo e **pendência não consumida**.
- GREEN: caso de uso.
- REFACTOR: validação compartilhada com `RegisterWithPassword`.
- Definition of done: DoD-P.

## T-042: `CompleteGoogleSignup` - expiração, uso único e corrida
- Status: done
- Covers: REQ-039
- Owner: implementer
- Blocked by: T-041
- Demo: Testes mostram pendência expirada, uso único e corrida na conclusão do cadastro Google.
- RED: pendência aberta há > 10 min -> nenhuma conta e orientação a refazer o login; reenviar a mesma pendência após sucesso -> nenhuma segunda conta; dois envios concorrentes: só um vence; email que passou a existir entre o callback e a conclusão -> `google_not_completed`; violação de unicidade de `subject` tratada.
- GREEN: consumo atômico e tradução das violações.
- REFACTOR: erros de pendência unificados.
- Definition of done: DoD-P.

## T-043: `LinkGoogleWithPassword`
- Status: done
- Covers: REQ-008, REQ-032, REQ-036, REQ-038
- Owner: implementer
- Blocked by: T-038, T-018, T-029
- Demo: Testes mostram o Google sendo vinculado após provar a senha.
- RED: `LinkGoogleWithPasswordTests`: senha correta -> vincula `ExternalIdentity`, abre sessão, consome a pendência, e login Google futuro entra na conta; senha incorreta -> `incorrect_password`, nada vinculado, nenhuma sessão, falha registrada e atraso progressivo aplicado, pendência **não consumida**; senha > 128 -> `too_long` sem vincular nem consumir; pendência expirada -> `pending_not_found`.
- GREEN: caso de uso.
- REFACTOR: reutiliza `CredentialVerifier`.
- Definition of done: DoD-P.

## T-044: Consultas de catálogo (`ListFormats`, `ListAvatarPresets`, `ListFavoriteTcgs`, `GetTermsInfo`)
- Status: done
- Covers: REQ-017, REQ-018, REQ-033, REQ-037
- Owner: implementer
- Blocked by: T-026, T-010
- Demo: Testes mostram as consultas de catálogo e dos termos devolvendo os dados esperados.
- RED: formatos ativos ordenados (Standard, Modern, Pioneer, Commander, Pauper); adicionar "Legacy" no fake aparece sem mudança de código; TCGs favoritos = só Magic: The Gathering; presets ativos com preset padrão marcado; `GetTermsInfo` devolve versão e URLs do provedor.
- GREEN: quatro consultas.
- REFACTOR: DTOs de saída próprios.
- Definition of done: DoD-P.

## T-045: `GetMyProfile`
- Status: done
- Covers: REQ-013, REQ-016, REQ-017, REQ-045, REQ-046
- Owner: implementer
- Blocked by: T-016, T-044, T-034
- Demo: Um teste de aplicação devolve o perfil do jogador autenticado.
- RED: `GetMyProfileTests`: devolve `Profile` com nome, avatar, TCG favorito, Formatos **ativos**, bio, `hasPassword`, `hasGoogle`; sem hash; o tipo `Profile`/`SessionSummary` não possui propriedade de email (teste de reflexão); Formato inativado some e reaparece ao reativar; perfil sem bio/formatos ok; sem sessão -> `unauthenticated`.
- GREEN: caso de uso e DTOs.
- REFACTOR: mapeamento centralizado de `Avatar` -> DTO com `alt` ("Avatar: {rótulo}" / "Seu avatar personalizado").
- Definition of done: DoD-P.

## T-046: `UpdateMyProfile`
- Status: done
- Covers: REQ-014, REQ-015, REQ-016, REQ-037, REQ-040, REQ-046
- Owner: implementer
- Blocked by: T-045
- Demo: Testes mostram o perfil sendo atualizado e rejeitado por campo.
- RED: `UpdateMyProfileTests`: edição válida persiste e devolve o perfil atualizado; erros por campo para nome (2/31 caracteres, Cc), bio (281, NUL), TCG "Pokemon", Formato inexistente; perfil inalterado em qualquer erro; Formato inativo preservado quando o cliente não o envia; só atua sobre o `PlayerId` da sessão; conflito de concorrência otimista vira `concurrency_conflict`; nome repetido aceito.
- GREEN: caso de uso.
- REFACTOR: reutiliza validação agregada de T-016.
- Definition of done: DoD-P.

## T-047: `ChooseAvatarPreset`
- Status: done
- Covers: REQ-018, REQ-021
- Owner: implementer
- Blocked by: T-017, T-045, T-028
- Demo: Testes mostram o jogador escolhendo um avatar preset.
- RED: `ChooseAvatarPresetTests`: preset ativo define o avatar ("mago-azul"); preset inexistente/inativo -> `invalid`; ao trocar de custom para preset, a imagem custom é removida do `IAvatarStorage` na mesma unidade de trabalho (rollback mantém a imagem); `AvatarChanged` emitido.
- GREEN: caso de uso.
- REFACTOR: remoção do avatar anterior em função única reutilizada pelo upload.
- Definition of done: DoD-P.

## T-048: `UploadCustomAvatar` e `GetMyCustomAvatar`
- Status: done
- Covers: REQ-019, REQ-020, REQ-021, REQ-049
- Owner: implementer
- Blocked by: T-047
- Demo: Testes mostram o envio e a leitura do avatar customizado.
- RED: `UploadCustomAvatarTests` com `FakeAvatarImageProcessor`: aceito -> grava a imagem processada, define `CustomAvatar(key)`, remove a imagem anterior e atualiza tudo numa unidade de trabalho; rejeitado por `size`/`type`/`content` (inclui animada e dimensões acima do limite, vindos do processador) -> avatar atual mantido e imagem anterior intacta, mesmo erro genérico; falha do processador (exceção) vira `content` sem alterar o avatar; `GetMyCustomAvatar` só devolve a imagem do dono da sessão e `404` se não houver.
- GREEN: dois casos de uso.
- REFACTOR: `AvatarRejection` com `reason` e `detail` (apenas para log).
- Definition of done: DoD-P.

## T-049: `DeleteMyAccount` - conta com senha
- Status: done
- Covers: REQ-023, REQ-024, REQ-025, REQ-026, REQ-027, REQ-032, REQ-047, REQ-048
- Owner: implementer
- Blocked by: T-020, T-029, T-027, T-028, T-024
- Demo: Testes mostram a exclusão de conta com senha apagando os dados pessoais.
- RED: `DeleteMyAccountPasswordTests`: sem `confirm` -> `validation_failed` e nada muda; senha vazia -> erro; senha incorreta -> `incorrect_password`, conta e dados intactos, falha registrada e atraso aplicado (`account_deletion`); sucesso -> numa única unidade de trabalho: chama `IPlayerDecksPurger`, apaga os eventos de acesso cujo `account_key_hash` é o HMAC do email do Jogador (e mantém os de outras chaves e os só de IP), remove o `Player` (aceite e 18+ vão juntos) e o avatar custom, revoga/remove todas as sessões; falha do purger faz rollback total; repetir a exclusão -> `unauthenticated`; login posterior com as credenciais antigas -> `invalid_credentials`; log/evento só com `playerId`.
- GREEN: caso de uso.
- REFACTOR: cálculo da chave de conta extraído e reutilizado com `AccessThrottle`.
- Definition of done: DoD-P.

## T-050: `DeleteMyAccount` - conta sem senha local (Google)
- Status: done
- Covers: REQ-023, REQ-026, REQ-042
- Owner: implementer
- Blocked by: T-049, T-039
- Demo: Testes mostram a exclusão de conta Google após reautenticar.
- RED: `DeleteMyAccountGoogleTests` com `FakeTimeProvider`: reautenticação há 2 min + `confirm` -> exclui; 4:59 aceito, 5:01 -> `reauth_expired` ("confirmação expirada"), nada excluído; nunca reautenticou -> `reauth_required`; reautenticou mas sem `confirm` -> conta intacta; reautenticação não é transferível para outra sessão; a exclusão reusa a mesma transação do caminho com senha.
- GREEN: ramo Google.
- REFACTOR: estratégia de reautenticação por tipo de conta.
- Definition of done: DoD-P.

## T-051: `PurgeExpiredData` (retenção)
- Status: done
- Covers: REQ-047
- Owner: implementer
- Blocked by: T-024, T-027
- Demo: Testes mostram dados expirados sendo removidos pela retenção.
- RED: `PurgeExpiredDataTests` com `FakeTimeProvider`: eventos de acesso > 24 h apagados e os mais novos mantidos; sessões expiradas/revogadas, tokens de redefinição e pendências expiradas apagados; idempotente (segunda execução não altera); seguro com duas execuções concorrentes (fake).
- GREEN: caso de uso e métodos de limpeza nas portas.
- REFACTOR: um método por tipo de dado.
- Definition of done: DoD-P.
- Nota: a variante de integração com SQLite (EF) desta limpeza é coberta em T-075.

## T-052: Teste de autorização por caso de uso (J10)
- Status: done
- Covers: REQ-011, REQ-013
- Owner: implementer
- Blocked by: T-045, T-046, T-047, T-048, T-049
- Demo: Um teste percorre todos os casos de uso e prova que nenhum atua sobre dados de outro jogador.
- RED: teste de reflexão em `Application.Tests`: nenhum comando/consulta de Perfil, avatar ou exclusão tem parâmetro `PlayerId` alvo vindo do cliente (o `PlayerId` vem do contexto autenticado); cada caso de uso protegido retorna `unauthenticated` com sessão ausente/expirada/revogada; Jogador A não lê nem altera dados de B (dois Jogadores no fake).
- GREEN: ajustar assinaturas que violarem.
- REFACTOR: `AuthenticatedPlayer` como tipo de entrada comum.
- Definition of done: DoD-P.

## Grupo D - Infraestrutura (adapters)

## T-053: `Pbkdf2PasswordHasher`
- Status: done
- Covers: REQ-001, REQ-004, REQ-038
- Owner: implementer
- Blocked by: T-025, T-014
- Demo: Testes mostram o hash PBKDF2 gerado e verificado.
- RED: `Pbkdf2PasswordHasherTests` em `Infrastructure.Tests` (iterações reduzidas por configuração nos testes): formato `v1$pbkdf2-sha256$<it>$<salt>$<hash>`; salts distintos para a mesma senha; `Verify` correto/incorreto em tempo constante (`CryptographicOperations.FixedTimeEquals`); `NeedsRehash` quando iterações < configuradas; padrão de produção = 600.000; semáforo limita hashes simultâneos ao número de núcleos; entrada > 128 caracteres nunca chega ao hasher (contrato).
- GREEN: `Rfc2898DeriveBytes.Pbkdf2` com SHA-256, salt 16 bytes, saída 32 bytes.
- REFACTOR: parâmetros em `PasswordHashingOptions` (`Auth:PasswordHashing:Iterations`).
- Definition of done: DoD-P; teste confirma que a senha não aparece no hash armazenado.

## T-054: Adapters de token e HMAC (`RandomNumberGenerator`, SHA-256, HMAC-SHA256)
- Status: done
- Covers: REQ-029, REQ-047
- Owner: implementer
- Blocked by: T-025
- Demo: Testes mostram tokens aleatórios, SHA-256 e HMAC-SHA256 funcionando.
- RED: `SecureTokenGenerator` gera 256 bits (base64url, sem repetição em 10.000 amostras); `Sha256TokenHasher` determinístico e de saída fixa; `HmacKeyedHasher` determinístico por segredo, diferente com segredo diferente, igual para o mesmo email normalizado, chave vazia/curta rejeitada; nenhum valor de entrada em `ToString`.
- GREEN: três adapters.
- REFACTOR: segredo HMAC via `Auth:RateLimitHmacKey` (opções, sem valor padrão).
- Definition of done: DoD-P.

## T-055: `AccountsDbContext` e mapeamento do `Player` (SQLite em memória)
- Status: done
- Covers: REQ-001, REQ-002, REQ-033, REQ-044, REQ-048
- Owner: implementer
- Blocked by: T-015, T-024, T-053
- Demo: Testes com SQLite em memória gravam e leem um `Player` pelo `AccountsDbContext`.
- RED: fixture `SqliteInMemoryFixture` (cria e descarta o banco por teste, sem connection string real) e `PlayerPersistenceTests`: ida e volta do `Player` com `accounts.player` (email único `ux_player_email`, `password_hash` nulo para Google, `favorite_tcg = mtg`, `terms_version`, `terms_accepted_at`, `adult_declared`); segundo `Add` com o mesmo email viola a unicidade e é traduzido para `email_unavailable`; a coluna de senha guarda só o hash; concorrência otimista com token de teste no lugar de `xmin`.
- GREEN: `AccountsDbContext` (schema `accounts`), configurações de entidade, `EfPlayerRepository`.
- REFACTOR: nomes de tabelas/colunas/índices iguais aos de `Database/tables/accounts.player.md`.
- Definition of done: DoD-P; `EnableSensitiveDataLogging` ausente.

## T-056: Formatos preferidos e identidade externa (EF)
- Status: done
- Covers: REQ-007, REQ-014, REQ-016, REQ-036, REQ-046
- Owner: implementer
- Blocked by: T-055, T-011
- Demo: Testes com SQLite em memória gravam formatos preferidos e identidade externa.
- RED: `PlayerRelationsPersistenceTests` (SQLite): `player_preferred_format` com PK composta e preservação de Formato inativo ao salvar; `player_external_identity` com `ux_player_external_identity_provider_subject` (mesmo `subject` em dois Jogadores viola; traduzido para `google_not_completed`); `FindByGoogleSubject` e `FindByEmail`; FKs com cascata ao remover o Jogador.
- GREEN: mapeamentos e consultas.
- REFACTOR: consultas por email já normalizado.
- Definition of done: DoD-P.

## T-057: Repositório EF de sessões
- Status: done
- Covers: REQ-010, REQ-012, REQ-030, REQ-035
- Owner: implementer
- Blocked by: T-055, T-021
- Demo: Testes com SQLite em memória persistem e consultam sessões.
- RED: `SessionPersistenceTests` (SQLite): grava só `token_hash` (`ux_player_session_token_hash`); `FindByTokenHash`; `RevokeAllFor(player)` marca todas; remoção em cascata com o Jogador; "agora" passado como parâmetro por `TimeProvider` (nenhuma função de data do banco).
- GREEN: `EfSessionRepository`.
- REFACTOR: índices `ix_player_session_player_id` e `ix_player_session_expires_at` declarados.
- Definition of done: DoD-P.

## T-058: Persistência do token de redefinição
- Status: done
- Covers: REQ-029, REQ-030, REQ-031
- Owner: implementer
- Blocked by: T-055, T-019
- Demo: Testes com SQLite em memória persistem o token de redefinição guardando só o hash.
- RED: `ResetTokenPersistenceTests` (SQLite): só o `token_hash` é gravado; `FindByResetTokenHash`; um token por Jogador (emitir substitui); consumo único (segunda tentativa de consumo falha); cascata na exclusão do Jogador.
- GREEN: mapeamento de `password_reset_token` e consumo atômico (`DELETE ... RETURNING` parametrizado via forma interpolada, ou `ExecuteDelete` com checagem de linhas afetadas).
- REFACTOR: documentar a diferença para PostgreSQL (ADR-0012).
- Definition of done: DoD-P.

## T-059: Store EF de pendências Google
- Status: done
- Covers: REQ-008, REQ-036, REQ-039
- Owner: implementer
- Blocked by: T-055, T-027
- Demo: Testes com SQLite em memória persistem as pendências Google.
- RED: `PendingGoogleAuthPersistenceTests` (SQLite): só `token_hash` gravado (`ux_pending_google_auth_token_hash`); `kind` signup/link; `target_player_id` com cascata; consumo único e atômico (segundo consumo falha); expiração calculada com `TimeProvider`; leitura não consome.
- GREEN: `EfPendingGoogleAuthStore`.
- REFACTOR: reutiliza a técnica de consumo de T-058.
- Definition of done: DoD-P.

## T-060: Store EF de tentativas de acesso
- Status: done
- Covers: REQ-032, REQ-041, REQ-047
- Owner: implementer
- Blocked by: T-055, T-054
- Demo: Testes com SQLite em memória persistem as tentativas de acesso.
- RED: `AccessAttemptPersistenceTests` (SQLite): grava só `ip_key_hash` e `account_key_hash` (nunca IP/email em claro); contagem por chave na janela de 15 min; `DeleteByAccountKeyHash` apaga só as linhas daquela chave e mantém as de IP e de outras contas; `DeleteOlderThan(24 h)` descarta antigas e mantém recentes; sem FK com `player`.
- GREEN: `EfAccessAttemptStore`.
- REFACTOR: índices `ix_access_attempt_*` declarados.
- Definition of done: DoD-P.

## T-061: `PostgresAvatarStorage`
- Status: done
- Covers: REQ-019, REQ-021, REQ-024
- Owner: implementer
- Blocked by: T-055, T-017
- Demo: Testes mostram o avatar customizado sendo guardado e lido.
- RED: `AvatarStoragePersistenceTests` (SQLite): salva imagem (bytes) por `storage_key`; no máximo uma por Jogador (`ux_avatar_image_player_id`); trocar de avatar grava a nova e apaga a anterior **na mesma transação** (rollback mantém a anterior); excluir o Jogador apaga a imagem em cascata; leitura devolve os bytes do dono.
- GREEN: `PostgresAvatarStorage` sobre o mesmo `DbContext`.
- REFACTOR: tamanho máximo de 256 KB validado na borda do adapter (`Avatar:*`).
- Definition of done: DoD-P; limitação do check `octet_length` do PostgreSQL registrada como não verificada pelo SQLite.

## T-062: Adapters de catálogo (presets com cache de 5 min, Formatos somente leitura)
- Status: done
- Covers: REQ-018, REQ-022, REQ-037, REQ-046
- Owner: implementer
- Blocked by: T-055, T-026
- Demo: Testes mostram os catálogos lidos com cache de 5 minutos e Formatos somente leitura.
- RED: `CatalogAdapterTests` (SQLite + `FakeTimeProvider`; os de Formato em `Deckloom.DeckSuggestion.Infrastructure.Tests`): `EfAvatarPresetCatalog` lista só ativos, expõe o padrão, cache de 5 min (mudança aparece após 5 min); `FormatCatalogDbContext` (em `DeckSuggestion.Infrastructure`, dono do modelo da tabela `deck_suggestion.format` conforme ADR-0007) só lê `deck_suggestion.format` (sem `SaveChanges` exposto) e `EfFormatCatalog` devolve ativos ordenados; inativar remove da listagem sem apagar a linha; FK `RESTRICT` de `player_preferred_format.format_id`.
- Decisão do usuário (2026-10-25, após checar Rule3 de ADR-0001): `EfFormatCatalog` (implementação de `IFormatCatalog`) mora em `Deckloom.DeckSuggestion.Infrastructure` e referencia `Deckloom.Accounts.Application`, como a RED original. Consequência conhecida: inverte a direção da ACL descrita em design.md linha ~104 (Accounts só depende da porta). Testes de `EfFormatCatalog` e do `FormatCatalogDbContext` ficam em `Deckloom.DeckSuggestion.Infrastructure.Tests`. Cache de Formatos não é exigido.
- GREEN: dois adapters e o contexto de leitura.
- REFACTOR: cache atrás de interface pequena.
- Definition of done: DoD-P.

## T-063: `EfUnitOfWork` e exclusão em cascata completa
- Status: done
- Covers: REQ-024, REQ-025, REQ-027, REQ-047, REQ-048
- Owner: implementer
- Blocked by: T-056, T-057, T-058, T-059, T-060, T-061
- Demo: Testes mostram a unidade de trabalho e a exclusão em cascata completa.
- RED: `AccountDeletionPersistenceTests` (SQLite): excluir o `Player` remove na mesma transação sessões, vínculo Google, preferências, token, pendências alvo e `avatar_image`; aceite e 18+ somem com a linha; eventos de acesso com o `account_key_hash` do email somem e os demais permanecem; falha no meio da transação faz rollback total; depois da exclusão o email está livre e novo cadastro gera outro `PlayerId` sem dados anteriores. Emenda ADR-0015: `EfUnitOfWorkTests` (SQLite) provam que `Database.CurrentTransaction` é nulo até o início do bloco de persistência, existe só dentro dele e volta a ser nulo após commit ou rollback (a transação nunca fica aberta enquanto o chamador espera atraso, hash, Google, SMTP ou imagem); o `DELETE` da exclusão só toca tabelas com `DELETE` na matriz de privilégios (a verificação formal está em T-133).
- GREEN: `EfUnitOfWork` (uma transação por caso de uso, aberta só em volta da persistência) e ordem de remoção.
- REFACTOR: sem exclusão manual onde a cascata cobre.
- Definition of done: DoD-P.

## T-064: `NoDecksYetPurger`
- Status: done
- Covers: REQ-025
- Owner: implementer
- Blocked by: T-028
- Demo: Testes mostram o purger de decks (ainda inexistentes) sendo chamado sem efeito.
- RED: o adapter implementa `IPlayerDecksPurger`, não lança e não acessa nada; é registrado como substituível; teste de contrato compartilhado com `RecordingPlayerDecksPurger` (mesma suíte de contrato) garante que a futura implementação de Decks participará da mesma unidade de trabalho; documentado como no-op até a feature 9.
- GREEN: adapter no-op documentado.
- REFACTOR: comentário apontando a ADR-0006 e a feature 9.
- Definition of done: DoD-P; pendência da feature 9 anotada no PR.

## T-065: Carga inicial dos catálogos (Formatos e presets) - dados no banco
- Status: done
- Covers: REQ-037, REQ-018, REQ-022
- Owner: implementer
- Blocked by: T-062
- Demo: A carga inicial de Formatos e presets existe como arquivo de dados verificado por teste.
- RED: `CatalogSeedTests` (SQLite aplicando o modelo com os dados semente do `HasData`; a carga de `format` fica em `DeckSuggestion.Infrastructure` e seu teste em `Deckloom.DeckSuggestion.Infrastructure.Tests`, a de `avatar_preset` em Accounts): `format` contém exatamente Standard, Modern, Pioneer, Commander e Pauper, todos ativos; `avatar_preset` tem entre 8 e 12 presets ativos, ids no padrão aceito, **exatamente um** `is_default` ativo; a correspondência com os arquivos de imagem é verificada em T-129.
- GREEN: dados semente no modelo (viram migração, T-066). Os arquivos SVG dos presets (ativos visuais) são feitos em T-129 (Owner: frontend), que depende desta tarefa para os ids.
- REFACTOR: ids estáveis e rótulos acessíveis em pt-BR.
- Definition of done: DoD-P.

## T-066: Gerar migrações EF Core (somente arquivos, para revisão e aplicação por humano)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Nota (T-062, análise do architect): a migração de DeckSuggestion (schema `deck_suggestion`, tabela `format` e carga inicial) deve ser aplicada ANTES da migração de Accounts que cria `fk_player_preferred_format_format`. Opção A aprovada pelo usuário (ADR-0001, Emenda 1). Registrar também na validação manual.
- Habilita: criação do esquema `accounts` e `deck_suggestion.format` em PostgreSQL por um humano, com os `GRANT` mínimos de `deckloom_app` (REQ-024, REQ-037, REQ-047; DB3, DB4, ADR-0015)
- Blocked by: T-055, T-056, T-057, T-058, T-059, T-060, T-061, T-062, T-065, T-132
- Demo: Os arquivos de migração são gerados para revisão humana, sem aplicar nada.
- RED: `ModelConformanceTests` (metadados do modelo EF, **sem conexão com banco**): tabelas, PKs (`pk_*`), FKs (`fk_*` e ações referenciais CASCADE/RESTRICT), índices (`ux_*`, `ix_*`, índice parcial `ux_avatar_preset_single_default`) e colunas listados em `Database/` e em `design.md` > "Objetos de Database/ afetados" existem no modelo com os mesmos nomes; falha se faltar algum. Emenda ADR-0015, mais três testes estáticos (sem conexão):
  - `MigrationGrantConformanceTests` (usa o parser de T-132; lê os arquivos `.cs` das duas migrações geradas e extrai os literais de `migrationBuilder.Sql`): para cada tabela da matriz existem exatamente os `GRANT` de `SELECT`/`INSERT`/`DELETE` marcados com `x` e `GRANT UPDATE (cols)` com as colunas exatas da matriz, todos `TO deckloom_app`; `GRANT USAGE ON SCHEMA accounts` e `deck_suggestion` a `deckloom_app`; **falha** para qualquer `GRANT` a `PUBLIC`, a outro papel, `ALL PRIVILEGES`, `TRUNCATE`/`REFERENCES`/`TRIGGER`, `ALTER DEFAULT PRIVILEGES ... GRANT`, `CREATE` em schema, qualquer privilégio nas tabelas `__ef_migrations_history` ou na sequência de identidade de `access_attempt`, e para `GRANT` em tabela ausente da matriz; as migrações não contêm `CREATE ROLE`, `ALTER ROLE` nem senha (papéis são do bootstrap, T-005); os `GRANT` são literais (sem concatenação nem interpolação).
  - `MigrationsHistoryTests`: cada módulo (Accounts e DeckSuggestion) configura `MigrationsHistoryTable("__ef_migrations_history", "<schema do módulo>")` (o `search_path` dos papéis é `pg_catalog`).
  - `SchemaQualifiedMappingTests`: toda entidade do modelo tem schema explícito (`accounts` ou `deck_suggestion`); nenhum mapeamento depende de `search_path`.
- GREEN: ajustar o modelo até o teste passar; **gerar** (não aplicar) a migração do schema `accounts` com `dotnet ef migrations add` usando `IDesignTimeDbContextFactory` com connection string **placeholder fictícia, escrita no código, que nunca é usada para conectar**; gerar a migração do schema `deck_suggestion` (tabela `format` e carga inicial) **no projeto `DeckSuggestion.Infrastructure`**, dono do dado (ADR-0007), do mesmo modo. Só `dotnet ef migrations add` (e `migrations remove` para refazer) é permitido; **qualquer `dotnet ef database ...` (`update`, `drop`) e `dotnet ef migrations script --idempotent` contra conexão é proibido e bloqueado pelo guard de banco**; nenhuma leitura de `.env`. Emenda ADR-0015: cada migração emite, por `migrationBuilder.Sql` com texto literal, na mesma migração que cria a tabela, os `GRANT` da matriz (`privilege-matrix.md`) e o `GRANT USAGE` do schema; configurar `MigrationsHistoryTable` em schema explícito e mapeamentos qualificados por schema; nenhuma revogação adicional além da matriz é necessária nas migrações (as revogações de `PUBLIC` e de privilégios padrão são do bootstrap, T-005; as tabelas nascem sem privilégio para `deckloom_app`, então "revogar" = não conceder o que a matriz marca `-`). A migração é aplicada por humano como `deckloom_migrator` (dono resultante `deckloom_owner`).
- REFACTOR: revisar manualmente o SQL gerado contra `Database/*` e anotar divergências (limites do SQLite da ADR-0012) na descrição do PR; se houver divergência em relação aos arquivos de `Database/`, parar e reportar (o `architect` atualiza, nada é alterado em silêncio).
- Definition of done: DoD-P; duas migrações geradas (Accounts e DeckSuggestion) como arquivos; **nenhum comando `dotnet ef database ...`, script ou conexão ao PostgreSQL foi executado**; PR marcado "migração para revisão e aplicação por humano"; os três testes da emenda ADR-0015 verdes e comprovadamente vermelhos com uma violação temporária (ex.: `GRANT ... TO PUBLIC`, `GRANT UPDATE` em coluna fora da matriz, tabela sem `GRANT`); divergência entre a matriz e `Database/tables/` ou `Database/roles/` = parar e reportar ao `architect`.

## T-067: `StubGoogleOidcServer` e URL de autorização do `GoogleOidcClient`
- Status: done
- Covers: REQ-006, REQ-042
- Owner: implementer
- Blocked by: T-028
- Demo: Testes contra o servidor OIDC de mentira geram a URL de autorização do Google.
- RED: `GoogleOidcAuthorizationUrlTests` (loopback local, sem Google real): URL com `response_type=code`, `scope=openid email profile`, `state`, `nonce`, `code_challenge` S256, `redirect_uri` fixo vindo de `Auth:PublicOrigin`; `intent=reauth` acrescenta `prompt=login` e `max_age=300`; valores de `state`/`nonce`/`code_verifier` aleatórios por chamada; `StubGoogleOidcServer` (em `Testing`) serve discovery, JWKS e token endpoint com chaves de teste.
- GREEN: `StubGoogleOidcServer` e `GoogleOidcClient.BuildAuthorizationUrl` (`HttpClient`, `Microsoft.IdentityModel`).
- REFACTOR: `ConfigurationManager` com cache de discovery/JWKS (24 h).
- Definition of done: DoD-P; nenhuma chamada de rede externa nos testes.

## T-068: `GoogleOidcClient` - troca do código e validação positiva do `id_token`
- Status: done
- Covers: REQ-006, REQ-007, REQ-042
- Owner: implementer
- Blocked by: T-067
- Demo: Testes mostram a troca do código e a validação positiva do `id_token`.
- RED: contra o stub: código válido + `code_verifier` -> `GoogleVerifiedIdentity` (subject, email, `emailVerified`, nome sem truncar, `auth_time`); a foto do Google não é lida nem retornada; token e segredo do cliente não aparecem em log nem em `ToString`; timeout de 5 s configurado.
- GREEN: troca de código e validação (assinatura via JWKS, `iss`, `aud`, `exp`, `nonce`).
- REFACTOR: mapeamento isolado para o DTO.
- Definition of done: DoD-P.

## T-069: `GoogleOidcClient` - falhas, negativos e degradação
- Status: done
- Covers: REQ-009, REQ-042
- Owner: implementer
- Blocked by: T-068
- Demo: Testes mostram os `id_token` inválidos e a indisponibilidade do Google tratados.
- RED: contra o stub: assinatura inválida, `nonce` errado, `aud` errado, `iss` errado, `exp` vencido, `email_verified=false`, `auth_time` antigo em reauth, `error=access_denied`, resposta 5xx do token endpoint, timeout (> 5 s) e JWKS com 1 retry -> todos viram `GoogleAuthFailed` tipado (estágio e motivo conforme catálogo de logs) sem lançar para o chamador; troca de código sem retry (código de uso único); `GoogleOidcClient` indisponível não afeta `LoginWithPassword` (teste de composição).
- GREEN: classificação de falhas.
- REFACTOR: motivos como enum (`cancelled`, `state_mismatch`, `unverified_email`, `provider_error`, `timeout`, `subject_mismatch`).
- Definition of done: DoD-P.

## T-070: `SkiaAvatarImageProcessor` - tamanho e assinatura antes de decodificar
- Status: done
- Covers: REQ-019, REQ-020
- Owner: implementer
- Blocked by: T-028
- Demo: Testes mostram arquivos grandes demais ou de tipo errado recusados antes de decodificar.
- RED: `AvatarSignatureTests` com fixtures em `tests/Deckloom.Accounts.Infrastructure.Tests/Fixtures/` (binários pequenos **commitados**: JPEG com EXIF/GPS e orientação 6, PNG, WebP, GIF, APNG, WebP animado e PNG cujo cabeçalho declara 20000x20000; os **grandes** - 2,1 MB, exatamente 2.097.152 bytes - são sintetizados em memória no teste): JPEG (`FF D8 FF`), PNG (`89 50 4E 47 0D 0A 1A 0A`), WebP (`RIFF....WEBP`) aceitos; GIF, `.exe` (bytes `MZ`), arquivo `foto.png` com texto, PNG de 2,1 MB -> `size`; arquivo de exatamente 2.097.152 bytes passa da etapa de tamanho; extensão e `Content-Type` ignorados; assinatura válida com corpo corrompido -> `content`; **nenhuma chamada de decodificação ocorre** nas rejeições (verificado por spy no wrapper do codec); pacote `SkiaSharp` e `SkiaSharp.NativeAssets.Linux.NoDependencies` com versão exata fixada em `Directory.Packages.props` (teste de higiene lê o arquivo).
- GREEN: `SkiaAvatarImageProcessor` etapas 2 e 3 (assinatura e tipo).
- REFACTOR: wrapper fino sobre `SKCodec` para tornar a ordem testável.
- Definition of done: DoD-P; binários nativos presentes no ambiente de teste (fixados).

## T-071: `SkiaAvatarImageProcessor` - cabeçalho: dimensões e animação (REQ-049)
- Status: done
- Covers: REQ-049, REQ-020
- Owner: implementer
- Blocked by: T-070
- Demo: Testes mostram imagens com dimensões inválidas ou animadas recusadas.
- RED: `AvatarHeaderLimitsTests`: 4096x4096 **aceita**; 4097x4096, 4096x4097, 4097x100, 100x4097 -> `content`; PNG cujo cabeçalho declara 20000x20000 recusado **sem decodificar pixels** (sem pico de memória medido por spy/limite); WebP animado e APNG (mais de um quadro) -> `content`; formato do codec diferente da assinatura -> `content`; regra `(long)width * height > 16_777_216` testada em unidade com valores que estourariam `int`; todas as rejeições têm o mesmo código de erro genérico de REQ-020.
- GREEN: etapa 3 completa (somente cabeçalho via `SKCodec.Create`).
- REFACTOR: limites vindos de `Avatar:MaxDimension` e `Avatar:MaxPixels`.
- Definition of done: DoD-P.

## T-072: `SkiaAvatarImageProcessor` - decodificação, recorte e reencode WebP 256x256
- Status: done
- Covers: REQ-019
- Owner: implementer
- Blocked by: T-071
- Demo: Testes mostram uma imagem sendo recortada e reencodada em WebP 256x256.
- RED: `AvatarReencodeTests`: JPEG de 1,5 MB com EXIF/GPS e orientação 6 -> saída WebP 256x256 (recorte central) com a orientação aplicada aos pixels e **sem bloco EXIF, XMP, GPS nem ICC** (inspeção dos bytes); PNG e WebP válidos reencodados; saída <= 256 KB; amostragem reduzida para originais > 512 px; semáforo (2 por núcleo) e timeout de 5 s; exceção do codec nativo vira `content` sem propagar.
- GREEN: etapas 4, 5 e 7 do processamento.
- REFACTOR: qualidade 85 e tamanho de saída em opções.
- Definition of done: DoD-P; mitigação do risco de código nativo do threat model (R) registrada para o `security`.

## T-073: `SmtpPasswordResetSender` (MailKit)
- Status: done
- Covers: REQ-028
- Owner: implementer
- Blocked by: T-028
- Demo: Testes mostram o email de redefinição montado e enviado via SMTP de teste.
- RED: `SmtpPasswordResetSenderTests` com `ISmtpTransport` fake: mensagem em pt-BR com o link `{PublicBaseUrl}/redefinir-senha#token=<token>` (token no **fragmento**, nunca em query), remetente `Email:From`; 3 tentativas com backoff 2 s, 8 s e 30 s (`FakeTimeProvider`); falha final registrada como `PasswordResetEmailFailed(playerId, attempt)` sem email/token e sem exceção para o chamador; timeout de 10 s por tentativa; TLS exigido quando `Email:Smtp:UseTls`.
- GREEN: sender e wrapper `ISmtpTransport` sobre MailKit.
- REFACTOR: template do corpo em recurso, sem HTML dinâmico não escapado.
- Definition of done: DoD-P; nenhum teste usa SMTP real nem Mailpit.

## T-074: Fila em memória do notificador e `BackgroundService` de envio
- Status: done
- Covers: REQ-028
- Owner: implementer
- Blocked by: T-073, T-035
- Demo: Testes mostram a fila em memória entregando o email em segundo plano.
- RED: `PasswordResetQueueTests`: `Enqueue` retorna imediatamente (tempo igual exista ou não a conta); o worker consome e chama o sender; capacidade limitada: acima dela descarta e registra log sem dados pessoais (mitigação de bombardeio de emails); falha do sender não derruba o worker nem revela a conta; o token em claro nunca é persistido.
- GREEN: `ChannelPasswordResetNotifier` (`Channel` limitado) e `BackgroundService`.
- REFACTOR: encerramento limpo no shutdown.
- Definition of done: DoD-P.

## T-075: Job horário de limpeza (retenção de ~24 h)
- Status: done
- Covers: REQ-047
- Owner: implementer
- Blocked by: T-051, T-060, T-057, T-058, T-059
- Demo: Testes mostram o job horário removendo dados com mais de ~24 h.
- RED: `CleanupJobTests` (SQLite + `FakeTimeProvider`): a cada ciclo chama `PurgeExpiredData`; eventos de acesso > 24 h somem e os recentes permanecem; sessões/tokens/pendências expirados somem; idempotente; duas instâncias simultâneas não falham.
- GREEN: `BackgroundService` agendado por `TimeProvider` com intervalo de 1 h.
- REFACTOR: intervalo e retenção em opções.
- Definition of done: DoD-P.

## Grupo E - API (Interface)

## T-076: Host da API, `WebApplicationFactory` com fakes e Problem Details (RFC 9457)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: todos os testes de API; contrato de erros do design (REQ-003, REQ-005)
- Blocked by: T-025, T-026, T-027, T-028
- Demo: Uma requisição à API de teste devolve erro em Problem Details (RFC 9457).
- RED: `ProblemDetailsTests` em `Api.Tests`: erro de validação -> `400 application/problem+json` com `type`, `title`, `status`, `code` estável, `errors` por campo (códigos `required`, `invalid_format`, `too_short`, `too_long`, `length`, `invalid_characters`, `invalid`, `unavailable`, `version_mismatch`) e `traceId` W3C; exceção não tratada -> `500 internal_error` genérico sem stack, SQL ou mensagem de biblioteca; `DomainError` mapeado em tabela única; CORS desabilitado (requisição `Origin` externa sem cabeçalhos CORS).
- GREEN: host Minimal APIs, `AddProblemDetails`, middleware de exceções, mapeador `DomainError` -> HTTP, fábrica de teste que troca persistência pelos fakes.
- REFACTOR: mapeamento erro -> status em um único lugar.
- Definition of done: DoD-P.

## T-077: Cabeçalhos de segurança da API
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: mitigações de XSS, clickjacking, vazamento de `Referer` e cache do threat model
- Blocked by: T-076
- Demo: Uma resposta da API traz os cabeçalhos de segurança esperados.
- RED: `SecurityHeadersTests`: toda resposta traz `Content-Security-Policy` do design (sem `unsafe-inline`), `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, `Permissions-Policy`; `Strict-Transport-Security` fora do ambiente local; respostas autenticadas com `Cache-Control: no-store`.
- GREEN: middleware.
- REFACTOR: política em opções, igual ao `nginx.conf` de T-005 (teste compara os dois textos).
- Definition of done: DoD-P.

## T-078: Proteção CSRF (antiforgery + `Origin`)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: mitigação de CSRF; todos os POST/PUT/DELETE
- Blocked by: T-076
- Demo: Um POST sem token CSRF ou com `Origin` errado é recusado.
- RED: `CsrfTests`: `GET /api/v1/csrf` devolve `{token}` e cookie `__Host-dl_csrf` (HttpOnly, Secure, SameSite=Strict, Path=/); POST/PUT/DELETE sem `X-CSRF-Token`, com token inválido ou com `Origin` diferente de `Auth:PublicOrigin` -> `403 csrf_failed` e evento de log 1020 (`path`, sem valores); GET não exige token.
- GREEN: antiforgery do ASP.NET Core + validação de `Origin`.
- REFACTOR: filtro de endpoint reutilizável por grupo de rotas.
- Definition of done: DoD-P.
- Nota (emenda 1 do ADR-0002, 2026-10-29): a validação usa o principal anônimo (`AnonymousAntiforgery`); o token fica vinculado só ao cookie `__Host-dl_csrf`, vale antes e depois do login e no logout, sem nova busca pelo frontend.

## T-079: Catálogo de eventos de log e harness de captura
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: NFR de observabilidade e de logs sem PII; teste de redação (T-093)
- Blocked by: T-076
- Demo: Os eventos de log do catálogo são capturados em teste.
- RED: `LogCatalogTests`: eventos 1001-1020 definidos com `EventId`, nome e campos permitidos; método de log tipado por evento (LoggerMessage); tentativa de logar campo fora do catálogo não compila/falha em teste; `RecordingLoggerProvider` (em `Testing`) captura mensagens e estado; formato JSON em stdout com `traceId`; logging de corpo de requisição desativado.
- GREEN: classe `AccountsLog` com `LoggerMessage` e o provider de captura.
- REFACTOR: um ponto de acesso ao catálogo.
- Definition of done: DoD-P.
- Nota (emenda ADR-0015): o catálogo passa a ter 21 eventos (1001-1021); o evento 1021 `DatabaseCommandFailed` e a métrica `deckloom.db.command_failures` são entregues por T-139, que estende `AccountsLog` e o `LogCatalogTests` desta tarefa. Esta tarefa continua com 1001-1020.

## T-080: Autenticação por cookie de sessão (`__Host-dl_session`)
- Status: done
- Covers: REQ-010, REQ-011, REQ-012, REQ-035
- Owner: implementer
- Blocked by: T-034, T-076, T-078
- Demo: Uma requisição com o cookie `__Host-dl_session` é autenticada.
- RED: `SessionAuthenticationTests`: cookie com `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, nome com prefixo `__Host-`; sem `Max-Age` sem lembrar-me e `Max-Age=2592000` com lembrar-me; rota protegida sem cookie, com token desconhecido, expirado ou revogado -> `401 unauthenticated` sem dados; requisição válida renova a sessão (30 min); logout expira o cookie; `Cache-Control: no-store`.
- GREEN: handler de autenticação que chama `AuthenticateSession` e popula `AuthenticatedPlayer`.
- REFACTOR: emissão e expiração do cookie em um serviço.
- Definition of done: DoD-P.

## T-081: Endpoints de catálogo e legal
- Status: done
- Covers: REQ-017, REQ-018, REQ-033, REQ-037
- Owner: implementer
- Blocked by: T-044, T-076
- Demo: Os endpoints de catálogo e legal respondem na API de teste.
- RED: `CatalogEndpointsTests`: `GET /api/v1/legal/terms` -> `{version, termsUrl, privacyUrl}`; `GET /catalog/formats` -> Standard, Modern, Pioneer, Commander, Pauper; `GET /catalog/favorite-tcgs` -> `[{id:"mtg", name:"Magic: The Gathering"}]`; `GET /catalog/avatar-presets` -> ativos com `imageUrl` e `isDefault`; Formato novo no fake aparece sem mudança de código; acessíveis sem sessão.
- GREEN: mapeamento das rotas.
- REFACTOR: grupo de rotas `catalog`.
- Definition of done: DoD-P.

## T-082: `POST /auth/register`
- Status: done
- Covers: REQ-001, REQ-002, REQ-003, REQ-022, REQ-033, REQ-034, REQ-038, REQ-040, REQ-041, REQ-043, REQ-044, REQ-048
- Owner: implementer
- Blocked by: T-032, T-031, T-080, T-078
- Demo: `POST /auth/register` cadastra o jogador e devolve a sessão em cookie.
- RED: `RegisterEndpointTests`: `201 SessionSummary` + cookie de sessão sem lembrar-me; resposta **sem propriedade `email`** nem hash; `400 validation_failed` com os códigos de campo do contrato (email `required/invalid_format`, password `required/too_short/too_long`, displayName `length/invalid_characters`, acceptTerms `required/version_mismatch`, adultDeclaration `required`); `409 email_unavailable` com `errors.email=["unavailable"]` e contagem de atraso por IP; senha com 128 aceita e 129 recusada; corpo limitado a 16 KB.
- GREEN: endpoint.
- REFACTOR: DTOs de requisição/resposta próprios (nunca o agregado).
- Definition of done: DoD-P.

## T-083: `POST /auth/login`, `GET /auth/session`, `POST /auth/logout`
- Status: done
- Covers: REQ-004, REQ-005, REQ-010, REQ-011, REQ-012, REQ-027, REQ-032, REQ-035, REQ-038
- Owner: implementer
- Blocked by: T-033, T-080
- Demo: Login, consulta de sessão e logout funcionam pela API de teste.
- RED: `LoginEndpointTests`: `200 SessionSummary` + cookie; senha errada, email inexistente, conta excluída, conta só-Google e senha > 128 -> **respostas idênticas** `401 invalid_credentials` (mesmo corpo, mesmos cabeçalhos); `400` só para campos vazios; `rememberMe` -> cookie com `Max-Age`; `GET /auth/session` com sessão -> `200` sem email, sem sessão -> `401`; logout `204` idempotente, token reutilizado -> `401`; atraso aplicado via `RecordingDelayer` e jamais bloqueio de conta.
- GREEN: três endpoints.
- REFACTOR: respostas de falha construídas por uma única função.
- Definition of done: DoD-P.

## T-084: Endpoints de redefinição de senha
- Status: done
- Covers: REQ-028, REQ-029, REQ-030, REQ-031, REQ-032, REQ-038
- Owner: implementer
- Blocked by: T-035, T-036, T-074, T-076, T-078
- Demo: Os endpoints de redefinição de senha funcionam pela API de teste.
- RED: `PasswordResetEndpointTests`: `POST /auth/password-reset/request` -> `202` com corpo vazio para email existente, só-Google e inexistente (respostas idênticas); duração mínima de 500 ms (medida com `FakeTimeProvider`/`IDelayer`); `400` só para formato; atraso aplicado; `POST /confirm` -> `204` e sessões antigas revogadas (cookie antigo vira `401`), `400 reset_link_invalid` para token inválido/expirado/usado, `400 validation_failed` para senha curta/longa **sem consumir o token**; nenhuma sessão nova criada; o link no `RecordingPasswordResetNotifier` usa fragmento.
- GREEN: dois endpoints.
- REFACTOR: duração mínima como política reutilizável.
- Definition of done: DoD-P.

## T-085: Endpoints Google `start` e `callback`
- Status: done
- Covers: REQ-006, REQ-007, REQ-008, REQ-009, REQ-023, REQ-042
- Owner: implementer
- Blocked by: T-037, T-038, T-039, T-080, T-069
- Demo: Os endpoints `start` e `callback` do Google funcionam pela API de teste.
- RED: `GoogleEndpointTests` com `FakeGoogleIdentityVerifier`: `GET /auth/google/start` -> `302` ao Google com `state`/`nonce`/PKCE e cookie `__Host-dl_oauth` (criptografado e assinado, HttpOnly, Secure, SameSite=Lax, `Max-Age=600`); `returnTo` externo (`https://...`, `//host`, `\`) ou fora da lista de rotas é ignorado no servidor; callback com `state` divergente, `error`, falha ou email não verificado -> `302 /entrar/google/retorno?resultado=falhou`; callback `ok` -> sessão; `concluir` e `vincular` -> cookie `__Host-dl_pending` (HttpOnly, Secure, SameSite=Lax, `Max-Age=600`) e **nenhuma conta nem sessão**; `intent=reauth` sem sessão recusado; reautenticação -> `reautenticado`/`reautenticacao-falhou`; cookie `__Host-dl_oauth` apagado no callback (uso único); queda do Google não afeta `/auth/login`.
- GREEN: dois endpoints.
- REFACTOR: lista de rotas internas num único módulo.
- Definition of done: DoD-P.

## T-086: Endpoints Google `pending` e `complete-signup`
- Status: done
- Covers: REQ-006, REQ-033, REQ-034, REQ-039, REQ-040, REQ-043, REQ-044, REQ-048
- Owner: implementer
- Blocked by: T-040, T-041, T-042, T-085
- Demo: Os endpoints `pending` e `complete-signup` funcionam pela API de teste.
- RED: `GooglePendingEndpointTests`: `GET /auth/google/pending` -> `{kind, email, suggestedDisplayName}` (nome sem truncar; `null` se vazio) e `404 pending_not_found` quando expirada/inexistente; `DELETE` idempotente `204` e cookie expirado; `POST /complete-signup` -> `201 SessionSummary` + cookie de sessão e cookie de pendência apagado; `400 validation_failed` (displayName `length/invalid_characters`, acceptTerms `required/version_mismatch`, adultDeclaration `required`) **sem consumir a pendência**; `404`; `409 google_not_completed`; resposta sem email.
- GREEN: três rotas.
- REFACTOR: leitura do cookie de pendência em um helper.
- Definition of done: DoD-P.

## T-087: Endpoint `POST /auth/google/link`
- Status: done
- Covers: REQ-008, REQ-032, REQ-036, REQ-038
- Owner: implementer
- Blocked by: T-043, T-085
- Demo: `POST /auth/google/link` vincula o Google pela API de teste.
- RED: `GoogleLinkEndpointTests`: senha correta -> `200 SessionSummary` + cookie de sessão; senha incorreta -> `401 incorrect_password` sem vínculo nem sessão e com atraso; `400 validation_failed` para senha vazia ou > 128 (nada vinculado, pendência intacta); `404 pending_not_found`; resposta sem email; cookie de pendência apagado só no sucesso.
- GREEN: endpoint.
- REFACTOR: reutiliza helpers de T-086.
- Definition of done: DoD-P.

## T-088: Endpoints `GET`/`PUT /me/profile`
- Status: done
- Covers: REQ-013, REQ-014, REQ-015, REQ-016, REQ-017, REQ-037, REQ-040, REQ-045, REQ-046
- Owner: implementer
- Blocked by: T-045, T-046, T-080, T-078
- Demo: `GET` e `PUT /me/profile` leem e atualizam o perfil pela API de teste.
- RED: `ProfileEndpointTests`: `GET` -> `200 Profile` com nome, bio, `favoriteTcg`, `preferredFormats` (só ativos), `avatar`, `hasPassword`, `hasGoogle`; **nenhuma propriedade `email` nem hash** em `GET`/`PUT /me/profile`, `GET /auth/session`, login, cadastro e vínculo (varredura recursiva do JSON); `PUT` válido -> `200` atualizado; `400` com `displayName` (`length`, `invalid_characters`), `bio` (`too_long`, `invalid_characters`; quebra de linha aceita), `favoriteTcg` `invalid`, `formatIds` `invalid`; Formato inativo preservado ao salvar e omitido na resposta; `409 concurrency_conflict`; sem sessão -> `401`; **não existe rota com `PlayerId`/identificador de outro Jogador** (IDOR): Jogador A nunca lê/altera B; `Cache-Control: no-store`.
- GREEN: dois endpoints.
- REFACTOR: DTOs de saída próprios.
- Definition of done: DoD-P.

## T-089: Endpoints de avatar
- Status: done
- Covers: REQ-018, REQ-019, REQ-020, REQ-021, REQ-049
- Owner: implementer
- Blocked by: T-047, T-048, T-072, T-080, T-078
- Demo: Os endpoints de avatar escolhem preset, enviam e leem o customizado.
- RED: `AvatarEndpointTests`: `PUT /me/avatar/preset` -> `200 {avatar}`, id inválido -> `400`; `POST /me/avatar/custom` multipart campo `file`: `Content-Length` > 2,5 MB -> `413` antes de ler o corpo; > 2.097.152 bytes -> `413 avatar_rejected (size)`; tipo -> `415 (type)`; conteúdo/animada/dimensões -> `422 (content)` (processador real com as fixtures: 4096x4096 aceita, 4097x4096 recusada, WebP animado recusado); em toda rejeição o avatar atual segue inalterado e a mensagem ao usuário é a mesma; `GET /me/avatar/custom?v=` -> `image/webp`, `Cache-Control: private, max-age=31536000, immutable`, `X-Content-Type-Options: nosniff`, `Content-Disposition: inline`, só para o dono, `404` sem avatar; `401` sem sessão.
- GREEN: três rotas.
- REFACTOR: tradução `AvatarRejection` -> status em tabela.
- Definition of done: DoD-P.

## T-090: Endpoint `POST /me/deletion`
- Status: done
- Covers: REQ-023, REQ-024, REQ-025, REQ-026, REQ-027, REQ-042, REQ-047, REQ-048
- Owner: implementer
- Blocked by: T-049, T-050, T-080, T-078
- Demo: `POST /me/deletion` exclui a conta pela API de teste.
- RED: `DeletionEndpointTests`: conta com senha: `400` sem `confirm` ou sem `password`; `401 incorrect_password` (conta intacta); `204` + cookie de sessão expirado e Decks purgados; conta sem senha: `403 reauth_required`, `403 reauth_expired` (> 5 min com `FakeTimeProvider`), `204` com reautenticação recente + `confirm`; após excluir, repetir -> `401`, login antigo -> `401 invalid_credentials`, novo cadastro do mesmo email cria outro Jogador; rate limit da ação `account_deletion` aplicado; log `AccountDeleted` só com `playerId`.
- GREEN: endpoint.
- REFACTOR: expiração de cookie junto ao logout.
- Definition of done: DoD-P.

## T-091: Limitador de concorrência por IP e limites de corpo
- Status: done
- Covers: REQ-032
- Owner: implementer
- Blocked by: T-083, T-089
- Demo: Requisições acima do limite por IP ou do tamanho do corpo são recusadas.
- RED: `ConcurrencyLimiterTests`: no máximo 4 requisições simultâneas por IP aos endpoints de autenticação, fila de 16 e acima disso `503` genérico (não é bloqueio de conta e sem corpo informativo); corpo JSON > 16 KB -> recusado; upload > 2,5 MB -> recusado; limites configuráveis; requisições de outros IPs não são afetadas.
- GREEN: middleware de concorrência e limites de Kestrel/endpoint.
- REFACTOR: limites em opções.
- Definition of done: DoD-P.

## T-092: Métricas OpenTelemetry
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: NFR de observabilidade e regras de alerta do design
- Blocked by: T-083, T-084, T-085, T-089, T-090
- Demo: As métricas OpenTelemetry aparecem após exercitar os fluxos.
- RED: `MetricsTests` com `MeterListener`: `deckloom.auth.login{outcome}`, `deckloom.auth.google{outcome,stage}`, `deckloom.auth.progressive_delay{action}` (histograma), `deckloom.auth.password_reset{outcome}`, `deckloom.auth.reset_email_failures`, `deckloom.accounts.registrations{method}`, `deckloom.accounts.deletions`, `deckloom.avatar.rejections{reason}`, `deckloom.auth.processing_duration` medida **depois** do atraso deliberado; nenhum atributo com email, IP ou id de conta.
- GREEN: medidores e instrumentação nos casos de uso/endpoints.
- REFACTOR: nomes de métricas em constantes.
- Definition of done: DoD-P; as regras de alerta ficam documentadas para a fase 8 (roteamento fora de escopo aqui).

## T-093: Teste de redação de logs ponta a ponta
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: NFR de logs sem PII; mitigação "vazamento de dados pessoais em logs" do threat model
- Blocked by: T-079, T-082, T-083, T-084, T-085, T-086, T-087, T-088, T-089, T-090, T-139
- Demo: Um teste ponta a ponta prova que os logs não contêm dados sensíveis.
- RED: `LogRedactionTests`: executa cadastro, login (sucesso e falha), logout, redefinição (pedido e conclusão), Google (todos os desfechos), edição de perfil, upload rejeitado, atraso, vínculo e exclusão com `RecordingLoggerProvider` e **falha se qualquer log (mensagem, estado, escopos, exceções) contiver** o email, a senha, o token de sessão/redefinição/pendência, o `code`/`state` do OAuth, o hash, o IP em claro, o nome de exibição ou a bio usados; só `playerId`, `sessionId`, códigos e `traceId`; `EnableSensitiveDataLogging` desativado; os **21** eventos do catálogo (1001-1021, emenda ADR-0015) são emitidos nos fluxos correspondentes, incluindo o 1021 `DatabaseCommandFailed` num fluxo com falha de banco injetada (exceção com `SqlState`) cujo log traz só `sqlState` e `operation`, sem SQL, parâmetros, mensagem do servidor, host nem connection string.
- GREEN: remover/ajustar qualquer log que vaze (incluindo o de acesso para `/api/v1/auth/google/callback`, só caminho sem query).
- REFACTOR: lista de valores sensíveis gerada pelo próprio teste a partir das entradas.
- Definition of done: DoD-P.

## T-094: Raiz de composição (DI) e opções de configuração
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: execução real da API em Docker Compose; segredos só por variável de ambiente
- Blocked by: T-053, T-054, T-063, T-064, T-068, T-072, T-074, T-075, T-086
- Demo: A API sobe com a composição de DI e as opções de configuração validadas.
- RED: `CompositionTests`: o namespace `Deckloom.Api.Composition` registra todas as portas com os adapters de produção; opções obrigatórias ausentes (`Auth:RateLimitHmacKey`, `Auth:Google:ClientSecret`, `ConnectionStrings:Deckloom`) fazem a inicialização falhar com mensagem sem valores; a fábrica de teste continua trocando só os adapters de persistência/externos pelos fakes; segredos fictícios usados são strings de teste no próprio código, nunca lidas de `.env`.
- GREEN: `AddAccountsModule`, opções tipadas (`Auth:*`, `Legal:*`, `Email:*`, `Avatar:*`), `TimeProvider.System`. Pendência do T-061: ligar `Avatar:MaxBytes` ao parâmetro `maxBytes` do construtor de `PostgresAvatarStorage` (hoje vale só o padrão 262144).
- REFACTOR: validação de opções no start (`ValidateOnStart`).
- Definition of done: DoD-P; nenhum arquivo `.env` lido; nenhum segredo real em código.
- Nota (emenda ADR-0015): esta tarefa continua dona da raiz de composição e das opções obrigatórias. Os acréscimos de controle de acesso ao banco são tarefas próprias que dependem desta: validação de `SSL Mode` fora de `Development` e ausência de credencial de migrador/superusuário na configuração (T-136) e verificação de papel na inicialização, DB7 (T-134 decisão pura, T-135 ligação). `AddAccountsModule` nunca chama `Migrate`/`MigrateAsync`/`EnsureCreated` (regra T-131), e nenhuma opção ou variável do serviço `api` carrega credencial de `postgres`, `deckloom_owner` ou `deckloom_migrator`.

## Grupo F - Frontend (componentes, validação compartilhada, telas S1-S11)

## T-095: Tokens de design, contraste e documento base (`lang`, foco, alvos)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: `cssRules.test.ts` varre todo o CSS do frontend e passa a valer para todas as tarefas seguintes - ART-01 (sem gradiente, brilho, `backdrop-filter` nem sombra em elemento em repouso; sombra só nos tokens do AccountMenu e do Dialog), ART-03 (raios só em {4, 6, 8, pleno}; raio interno menor que o externo), ART-06 (`:hover` só dentro de `@media (hover: hover) and (pointer: fine)`; todo pressionável com `:active { transform: scale(0.97) }`), ART-07 (sem `transition: all`; só `transform`, `opacity` e as cores de E3), ART-08 (altura mínima de 44px); ART-04 por lint (sem emoji, eyebrow, maiúsculas espaçadas, numeração de índice, "lorem" ou "Acme" no código e nos textos); ART-05 (token/classe com `font-variant-numeric: tabular-nums`); A11Y-37 (com `prefers-reduced-motion: reduce` emulado: sem rotação nem escala, só opacidade, coluna "Reduced motion" de A.8.4), A11Y-38 (`prefers-contrast: more`: borda de 1px `--color-border-control` no painel e no diálogo e texto de apoio na cor de texto), A11Y-39 (nenhuma superfície translúcida além do scrim, que permanece escuro e opaco; revisão); tokens de movimento (`--ease-out`, durações) conforme `docs/design-system.md` v0.1.
- Habilita: A11Y-09, A11Y-13, A11Y-14, A11Y-21, A11Y-32, A11Y-36 (decisão U16)
- Blocked by: T-003
- Demo: A página base mostra tokens de design, contraste, foco e alvos de toque corretos.
- RED: `tokens.test.ts`: o contraste calculado a partir dos tokens de `docs/design-system.md` (primária #1D4ED8, texto, perigo, bordas, anel de foco) cumpre >= 4,5:1 (texto) e >= 3:1 (bordas, ícones, foco) - A11Y-13/14; varredura de CSS falha se houver `outline: none` sem substituto (A11Y-09) ou conteúdo essencial revelado só por `:hover` (A11Y-32); `index.html` tem `<html lang="pt-BR">` (A11Y-21); token de alvo mínimo 44x44 px (A11Y-36).
- GREEN: `src/design-system/tokens.css` e `index.html`.
- REFACTOR: tokens nomeados semanticamente; só tema claro.
- Definition of done: DoD-P.

## T-096: Componentes base - Button, GoogleButton, Link, Divider, Alert, Spinner/Skeleton, Tag, Avatar
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-01 (`:active` com `scale(0.97)`, 120ms), MOT-02 (hover só de cor, 150ms, só com hover fino), MOT-05 (Alert: `opacity` 0 a 1 em 160ms, sem deslocamento), MOT-07 (indicador de loading do Button: rotação de 800ms, rótulo e largura mantidos), MOT-08 (Spinner), MOT-09 (Skeleton plano, sem brilho nem animação), cada MOT com sua coluna "Reduced motion" (A11Y-37); WC-02 (Avatar com `flex-shrink: 0`, contêiner de nome com `min-width: 0` e `overflow-wrap: anywhere`), WC-08 (Tags quebram em linhas; nome de 60 caracteres quebra dentro da Tag), WC-10 (Avatar que falha ou demora: círculo cinza com a primeira letra do nome - primeiro grapheme -, mesmo tamanho e mesmo nome acessível, `width`/`height` fixos).
- Habilita: A11Y-03, A11Y-15, A11Y-16, A11Y-24, A11Y-30, A11Y-36; U15 (botão Google com SVG local)
- Blocked by: T-095
- Demo: Os componentes base renderizam e passam no axe.
- RED: testes RTL + `axe`: Button em `loading` mantém rótulo, `aria-busy="true"`, não duplica envio; GoogleButton com SVG local (nenhum recurso externo, rótulo "Continuar com Google" idêntico em S1/S2); Alert `info/sucesso` com `role="status"` e `erro` de página com `role="alert"`, ícone + texto (A11Y-03/16); Link externo exibe "(abre em nova aba)" (A11Y-24); Spinner acompanhado de texto e Skeleton com `aria-busy`; Tag como texto, não botão; Avatar com `alt` descritivo ("Avatar: {rótulo}" / "Seu avatar personalizado") ou `alt=""` ao lado do nome (A11Y-15).
- GREEN: componentes em `src/design-system`.
- REFACTOR: variantes por props tipadas.
- Definition of done: DoD-P; zero violações `axe`.

## T-097: TextField, PasswordField, FormError, ErrorSummary
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-04 (erros de campo e ErrorSummary aparecem e recebem foco na hora, sem animação); WC-06 (email de 254 caracteres rola por dentro do campo; senha de 128 com "Mostrar": o botão permanece visível e o painel não estoura); ART-08 (controles >= 44px).
- Habilita: A11Y-01, A11Y-02, A11Y-03, A11Y-04, A11Y-22, A11Y-26, A11Y-36
- Blocked by: T-096
- Demo: Os campos de texto e senha mostram rótulo, erro e resumo de erros acessíveis.
- RED: TextField com `<label>` visível associado, placeholder nunca é rótulo (A11Y-01); em erro `aria-invalid="true"` e `aria-describedby` para erro e dica, e ambos somem ao corrigir (A11Y-02); FormError com ícone + texto (A11Y-03); ErrorSummary com `tabindex="-1"`, recebe foco ao enviar com erros e cada link foca o campo correspondente (A11Y-04); PasswordField com botão "Mostrar senha" (nome acessível, `aria-pressed`), sem perder valor nem foco, colar permitido (A11Y-26/36), `autocomplete` correto (`email`/`username`, `current-password`, `new-password`, `nickname`) (A11Y-22).
- GREEN: quatro componentes.
- REFACTOR: hook `useFieldIds` para ligar ids de dica/erro.
- Definition of done: DoD-P; zero violações `axe`.

## T-098: Checkbox, CheckboxGroup, RadioGroup, Select e Textarea com contador
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: WC-15 (rótulos longos quebram a 320px sem corte; alvos >= 44px; caixa alinhada à primeira linha); WC-18 (contador com `Intl.NumberFormat("pt-BR")` e `tabular-nums` - ART-05 -, sem "NaN" nem "undefined", "281 de 280" correto); MOT-15 (Checkbox, radio e contador mudam instantaneamente, sem animar `height`).
- Habilita: A11Y-24, A11Y-25, A11Y-27, A11Y-28, REQ-015 (contador em grapheme clusters)
- Blocked by: T-097
- Demo: Caixas, rádios, seleção e área de texto com contador funcionam por teclado.
- RED: Checkbox nativo com label clicável e links no label que **não** alteram o estado (A11Y-24/25); CheckboxGroup e RadioGroup em `<fieldset>/<legend>`, vazio mostra texto (A11Y-27); Select nativo; Textarea com contador ligado por `aria-describedby`, sem anúncio a cada tecla, contando elementos de texto (`Intl.Segmenter`), não bloqueia digitação e acima do limite mostra "281 de 280" como erro (A11Y-28).
- GREEN: componentes.
- REFACTOR: contador usa a função de grapheme de T-104 (importada quando disponível; até lá, stub local trocado na tarefa seguinte).
- Definition of done: DoD-P; zero violações `axe`.

## T-099: Dialog (modal e alertdialog)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-12 (painel: `scale` 0.97 a 1 e `opacity`, 200ms entrada / 150ms saída) e MOT-13 (scrim: `opacity`), com abrir/fechar rápido retargetando sem travar; A11Y-37 (só opacidade com movimento reduzido); A11Y-38 (borda de 1px no painel com `prefers-contrast: more`); WC-14 (painel mais alto que a janela, 320x480 com texto a 200%, rola por inteiro, botões alcançáveis, foco preso e devolvido ao gatilho; teste de componente aqui e e2e em T-127); ART-01 (sombra só no Dialog).
- Habilita: A11Y-06, A11Y-08, A11Y-11, A11Y-12; S10 e S11
- Blocked by: T-096
- Demo: O Dialog abre, prende o foco e fecha corretamente.
- RED: `role="dialog"`/`"alertdialog"`, `aria-modal="true"`, `aria-labelledby`; foco entra no dialog e volta ao gatilho ao fechar; Tab circular sem armadilha fora do dialog; fundo inerte; Esc cancela, exceto quando `busy` (envio em S11); botão de perigo nunca recebe o foco inicial.
- GREEN: componente com gerenciamento de foco.
- REFACTOR: hook `useFocusTrap` isolado.
- Definition of done: DoD-P; zero violações `axe`.

## T-100: RateLimitNotice e hook de envio (aviso após 2 s, timeout de 20 s)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-06 (`opacity` 0 a 1 em 160ms; saída sem animação); WC-12 (respostas de 3 s, 12 s e 20 s com relógio falso: botão em loading sem mudar de largura, campos somente leitura, aviso após 2 s com o espaço reservado no momento do envio, sem duplo envio, estouro de 20 s vira erro genérico).
- Habilita: A11Y-17; REQ-032 e REQ-041 na UI (decisão U14); timeouts do design
- Blocked by: T-096
- Demo: O aviso de limite aparece após 2 s de espera e o envio expira em 20 s.
- RED: com timers falsos: região `role="status"` já presente no DOM; o texto "Por segurança, esta resposta está demorando um pouco mais. Aguarde, não é necessário enviar de novo." entra só após 2 s (A11Y-17); sem contagem regressiva e sem "bloqueada"; o hook `useSubmit` desabilita duplo envio, deixa campos somente leitura durante o envio e, ao passar 20 s (30 s no upload), cancela e devolve erro genérico de rede/servidor, **nunca** "credenciais inválidas".
- GREEN: componente e hook.
- REFACTOR: tempos em constantes.
- Definition of done: DoD-P.

## T-101: FileUpload e AvatarPicker
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-14 (pré-visualização: `opacity` 150ms); MOT-01/MOT-02 no tile de preset e MOT-15 (seleção do preset instantânea); WC-09 (8 e 12 presets em grade de 4 colunas, 3 a 320px, alvos de 44px; lista vazia ou com falha mostra "Não foi possível carregar os avatares. Tente de novo." e o envio de arquivo continua disponível; carregando mostra círculos cinza); WC-11 (nome de arquivo de 200 caracteres quebra sem estourar; pré-visualização de 1x1, 4096x4096 e 1:5 com `object-fit: cover` no quadro de 96px).
- Habilita: A11Y-15, A11Y-27, A11Y-29; S10
- Blocked by: T-096, T-098
- Demo: Escolher imagem e galeria de avatares funciona por teclado e mouse.
- RED: `<input type="file" accept="image/jpeg,image/png,image/webp">` nativo com rótulo e dica "JPEG, PNG ou WebP, até 2 MB. Removemos os metadados da imagem." (A11Y-29); nome do arquivo lido; validação de conveniência do cliente ("Escolha uma imagem JPEG, PNG ou WebP.", "A imagem precisa ter no máximo 2 MB." com 2.097.152 bytes como limite, "Escolha um arquivo para enviar."); pré-visualização com `alt`; presets em RadioGroup com nome acessível por avatar (A11Y-15/27) e preset atual pré-selecionado; sem arrastar-e-soltar.
- GREEN: dois componentes.
- REFACTOR: revogar `URL.createObjectURL` ao desmontar.
- Definition of done: DoD-P; zero violações `axe`.

## T-102: AppHeader, AccountMenu e SkipLink
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-11 (painel do AccountMenu: `scale` 0.97 a 1 com origem no gatilho e `opacity`, 150ms entrada / 100ms saída; com movimento reduzido só `opacity`), MOT-01 (botão de avatar), MOT-02 (itens do menu), MOT-15 (troca do avatar instantânea); WC-03 (nome acima da largura disponível é cortado no fim com reticências, o nome completo aparece no painel do menu e no `aria-label` do botão, e o botão nunca sai da tela a 320px); decisão do usuário: o AppHeader oculta o nome abaixo de 480px (só o avatar); WC-01 (nomes de 30 "W", 30 emojis compostos, 30 ideogramas, árabe, tailandês e vietnamita no AccountMenu a 320, 768 e 1280px sem rolagem horizontal nem texto cortado); A11Y-38 (painel com borda de 1px); ART-01 (sombra só no painel).
- Habilita: A11Y-23, A11Y-30, A11Y-33; UX-07 (avatar no header sem recarregar)
- Blocked by: T-096
- Demo: O cabeçalho, o menu da conta e o link de pular conteúdo funcionam.
- RED: SkipLink é o primeiro foco e leva ao `<main>` (A11Y-23); visitante vê wordmark, "Entrar" e "Criar conta"; Jogador vê botão de avatar com nome e menu ("Meu perfil", "Sair") por botão de divulgação com `aria-expanded`, Esc fecha e devolve o foco (A11Y-33); header consistente entre telas (A11Y-30); avatar do header atualiza quando o contexto de sessão muda.
- GREEN: componentes.
- REFACTOR: menu sem `role="menu"`.
- Definition of done: DoD-P; zero violações `axe`.

## T-103: `apiClient` (CSRF, Problem Details, timeouts, 401)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: todas as telas; REQ-011 (tratamento de 401) e REQ-005 (erro genérico) na UI
- Blocked by: T-003
- Demo: Testes mostram o cliente de API tratando CSRF, Problem Details, timeouts e 401.
- RED: com MSW: obtém `GET /api/v1/csrf` uma vez e envia `X-CSRF-Token` em POST/PUT/DELETE; mapeia Problem Details para `{code, fieldErrors, traceId}`; 401 aciona o callback de sessão expirada, exceto quando o `code` do Problem Details é `incorrect_password` (emenda 2026-11-01, R-24: o erro é lançado como os demais, mas o callback não é chamado, e o S11/S5 mantém o dialog/a tela); timeout de 20 s (30 s no upload) vira erro de rede genérico; `traceId` só vai ao console em desenvolvimento; credenciais incluídas só para a mesma origem; nenhuma senha/token em logs do cliente.
- GREEN: cliente fetch tipado com os tipos compartilhados (`Avatar`, `SessionSummary`, `Profile`).
- REFACTOR: tipos gerados/escritos num único módulo `shared/api`.
- Emenda 2026-11-01 (R-24, aprovada pelo usuário na triagem do retro; design.md "Arquitetura de informação", "401 no cliente de API"): o RED original dizia só "401 aciona o callback"; o código já excluía `incorrect_password` sem teste direto. A rodada de correção acrescenta em `apiClient.test.ts` dois casos: 401 com `code = incorrect_password` não chama `onSessionExpired` e rejeita com `ApiError(401, "incorrect_password")`; 401 com qualquer outro código (ou sem corpo) chama o callback uma vez. Guarda de regressão de comportamento existente: o vermelho se prova removendo a exceção no código (temporário, revertido).
- Definition of done: DoD-P.

## T-104: Validadores do cliente e contagem de graphemes
- Status: done
- Covers: REQ-003, REQ-015, REQ-038, REQ-039, REQ-040
- Owner: implementer
- Blocked by: T-003
- Demo: Testes mostram os validadores do cliente contando graphemes como o servidor.
- RED: `validators.test.ts`: email (formato), senha 8-128 em **code points**, nome 3-30 após `trim` e em grapheme clusters (`Intl.Segmenter`: `"  Bi  "` = 2; 2 letras + emoji composto = 3), caracteres de controle em nome e bio (quebra de linha permitida só na bio), bio <= 280; as mesmas fronteiras 7/8/128/129, 2/3/30/31, 280/281 do backend; o cliente é conveniência (o servidor é a autoridade).
- GREEN: módulo `shared/validation` e `graphemeLength`.
- REFACTOR: trocar o stub de contagem do Textarea (T-098) por esta função.
- Definition of done: DoD-P.

## T-105: Mapa de mensagens pt-BR por código de erro
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: A11Y-05 e as mensagens de S1-S11; REQ-005, REQ-031 e REQ-009 (mensagens genéricas)
- Blocked by: T-103
- Demo: Testes mostram cada código de erro mapeado para uma mensagem em pt-BR.
- RED: teste de tabela com cada `code` e código de campo do contrato (`required`, `invalid_format`, `too_short`, `too_long`, `length`, `invalid_characters`, `invalid`, `unavailable`, `version_mismatch`; `invalid_credentials`, `incorrect_password`, `reset_link_invalid`, `reauth_expired`, `avatar_rejected`, `email_unavailable`, `pending_not_found`) -> texto exato das tabelas de estados do design, incluindo o estado novo de S10 "Não foi possível carregar os avatares. Tente de novo."; mensagens indistinguíveis iguais (login, redefinição, link, Google); todo erro diz como corrigir (A11Y-05); código desconhecido -> mensagem genérica de servidor.
- GREEN: módulo `shared/messages`.
- REFACTOR: textos centralizados, sem texto vindo do servidor.
- Definition of done: DoD-P.

## T-106: Validação de `retorno`, rotas, provider de sessão e guards (lógica, sem estilo)
- Status: done
- Covers: REQ-010, REQ-011, REQ-012
- Owner: implementer
- Blocked by: T-102, T-103
- Demo: Testes mostram o retorno validado, as rotas e os guards decidindo pela sessão.
- RED: `safeReturnTo` aceita só caminho interno conhecido (começa com `/`, sem `//`, sem `\`, sem esquema; fora da lista -> `/perfil`) - UX-12/A10; `SessionProvider` consulta `GET /auth/session`; rota de Jogador sem sessão -> `/entrar?aviso=sessao-expirada&retorno=<rota>` com o parâmetro `aviso` correto (o `role="alert"` e o foco no aviso são verificados em T-128/A11Y-31); rota de visitante (S1, S2, S6, S7) com sessão -> `/perfil`; 401 em qualquer chamada leva ao login sem exibir dados; não existe rota com id de outro Jogador.
- GREEN: router, guards e provider de sessão.
- REFACTOR: lista de rotas conhecidas num módulo compartilhado com os testes.
- Definition of done: DoD-P.

## T-107: S1 Cadastro - formulário, validação do cliente e sucesso
- Status: done
- Covers: REQ-001, REQ-003, REQ-033, REQ-034, REQ-038, REQ-040, REQ-044
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: ART-02 (um único botão preenchido, "Criar conta"; no máximo 4 matizes fora o "G" do Google); WC-06 (email de 254 e senha de 128 sem estourar o painel); WC-15 (rótulo longo dos termos quebra a 320px sem corte, caixa alinhada à primeira linha); MOT-04 (erros sem animação); face da tela de entrada = wordmark de texto no topo.
- Blocked by: T-097, T-098, T-100, T-104, T-105, T-106
- Demo: A tela de cadastro valida no cliente e mostra o sucesso.
- RED: `CadastroPage.test.tsx` (RTL + MSW + axe): campos Email, Senha (dica "de 8 a 128 caracteres"), Nome de exibição (dica de 3 a 30), checkbox de Termos e Política (links com "(abre em nova aba)", abrir não altera o checkbox - A11Y-24/25), checkbox 18+, botão "Criar conta", botão Google, link "Entrar"; foco inicial no `<h1>` ao chegar por transição de rota na SPA, e na carga completa da página sem foco programático (o primeiro Tab chega ao SkipLink; A11Y-23/34; emenda de 2026-10-31, QA-03); envio com erros move o foco ao ErrorSummary com as mensagens exatas do design (email, senha curta/longa, nome, caracteres de controle, termos, 18+) - A11Y-02/04/05; senha limpa em falha; sucesso -> `/perfil` com "Conta criada. Bem-vindo(a) ao Deckloom!" em `role="status"`; envia `termsVersion` obtido de `/legal/terms` e `adultDeclaration`; o formulário não é enviado ao alterar foco/campos (A11Y-25); `autocomplete` correto (A11Y-22).
- GREEN: página S1 e rota `/cadastro`.
- REFACTOR: mapeamento de erros de campo para o ErrorSummary reutilizável por S4/S9.
- Definition of done: DoD-P.

## T-108: S1 Cadastro - erros do servidor, email indisponível, versão e atraso
- Status: done
- Covers: REQ-002, REQ-041, REQ-043
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: WC-12 (respostas de 3 s, 12 s e 20 s: loading sem mudar largura, aviso de atraso após 2 s com espaço reservado, sem duplo envio, estouro de 20 s vira erro genérico); MOT-05/MOT-06 (Alert e aviso entram só com `opacity`).
- Blocked by: T-107
- Demo: A tela de cadastro mostra erros do servidor, email indisponível e atraso.
- RED: `409 email_unavailable` -> erro no campo email "Não foi possível usar este email. Tente outro ou entre na sua conta."; `version_mismatch` -> "Os Termos de Uso ou a Política de Privacidade foram atualizados. Revise e aceite novamente." no campo de aceite e nova leitura da versão; resposta > 2 s mostra o RateLimitNotice (só atraso por IP, sem texto de bloqueio); 5xx/rede/timeout -> Alert "Não foi possível criar a conta agora. Tente de novo." com dados preservados e senha limpa.
- GREEN: tratamento de erros em S1.
- REFACTOR: reutiliza `useSubmit`.
- Definition of done: DoD-P.

## T-109: S2 Login
- Status: done
- Covers: REQ-004, REQ-005, REQ-009, REQ-027, REQ-032, REQ-035
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-05 (os `aviso` entram com `opacity` 160ms, sem deslocamento); ART-02 ("Entrar" é o único botão preenchido); WC-12 (resposta lenta e estouro de 20 s); WC-06 (email de 254 e senha de 128).
- Blocked by: T-097, T-098, T-100, T-105, T-106
- Demo: A tela de login entra com email e senha e mostra os erros.
- RED: `LoginPage.test.tsx`: campos Email/Senha, "Lembrar-me neste dispositivo por 30 dias" (envia `rememberMe`), "Entrar", "Esqueci minha senha", botão Google e dica fixa; campos vazios -> "Informe seu email."/"Informe sua senha."; **UX-01**: senha errada, email inexistente, conta excluída, conta só-Google e senha > 128 mostram o **mesmo** Alert com foco "Email ou senha inválidos." (mesma estrutura), senha limpa e email mantido; atraso > 2 s -> RateLimitNotice; timeout/rede -> "Não foi possível entrar agora. Tente de novo." (nunca "credenciais inválidas"); `aviso` (`sessao-expirada` com `role="alert"`; `saiu`, `senha-alterada`, `conta-excluida`, `google-falhou` com `role="status"`) com os textos exatos; sucesso -> `retorno` válido ou `/perfil`, `retorno` externo ignorado (UX-12).
- GREEN: página S2.
- REFACTOR: tabela `aviso` -> mensagem.
- Definition of done: DoD-P.

## T-110: S3 Retorno do Google
- Status: done
- Covers: REQ-006, REQ-007, REQ-008, REQ-009, REQ-023, REQ-042
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-08 (spinner com rotação de 800ms e arco estático com movimento reduzido, sempre com o texto "Aguarde um instante..."); MOT-10 (troca de rota sem animação, foco no `<h1>` em transição de rota, e na carga completa da página sem foco programático; A11Y-23/34; emenda de 2026-10-31, QA-03).
- Blocked by: T-106
- Demo: A tela de retorno do Google leva o jogador ao destino certo.
- RED: `GoogleReturnPage.test.tsx`: `<h1>` "Concluindo o login com Google" com spinner em `role="status"`; `ok` -> `retorno` revalidado ou `/perfil`; `concluir` -> S4; `vincular` -> S5; `reautenticado` -> `/perfil?exclusao=confirmar`; `reautenticacao-falhou` -> `/perfil?exclusao=falhou`; `falhou`, ausente ou desconhecido -> `/entrar?aviso=google-falhou`; `retorno` externo ignorado; foco no `<h1>` ao transitar (A11Y-34); a S3 aberta por carga completa (redirect do backend após o Google) não recebe foco no `<h1>`: o foco fica no início do documento e o primeiro Tab chega ao SkipLink (A11Y-23); a regra de foco vale para as transições de saída da S3 dentro da SPA (emenda de 2026-10-31, QA-03); sem formulário.
- GREEN: página S3.
- REFACTOR: mapa resultado -> destino.
- Definition of done: DoD-P.

## T-111: S4 Concluir cadastro com Google
- Status: done
- Covers: REQ-006, REQ-033, REQ-034, REQ-039, REQ-040, REQ-043, REQ-044, REQ-048
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: WC-07 (nome do Google vazio, de 2, de 31+ elementos e só de emoji: mostrado sem truncar, erro só no envio); WC-01 (nomes longos de A.8.5 sem rolagem horizontal nem texto cortado em S4 a 320, 768 e 1280px); WC-06 (email verificado de 254 caracteres quebra com `overflow-wrap: anywhere`); WC-15 (rótulo dos termos); ART-02 ("Criar conta" único preenchido).
- Blocked by: T-107, T-110
- Demo: A tela de concluir cadastro com Google cria a conta.
- RED: `GoogleSignupCompletePage.test.tsx`: lê `GET /auth/google/pending` e mostra o email verificado; nome pré-preenchido com o do Google, editável, **sem truncar** (40 caracteres vêm inteiros e enviar sem editar mostra erro de 3 a 30); `"  Bi  "` rejeitado, emoji composto contado como 1; Cc rejeitado; termos e 18+ obrigatórios com as mesmas mensagens de S1 e `version_mismatch` (**UX-04**); "Cancelar" chama `DELETE /auth/google/pending` e volta a S2; pendência expirada/inexistente -> `/entrar?aviso=google-falhou`; sucesso -> `/perfil` com "Conta criada. Bem-vindo(a) ao Deckloom!"; foto do Google não exibida.
- GREEN: página S4.
- REFACTOR: compartilha campos e erros com S1.
- Definition of done: DoD-P.

## T-112: S5 Vincular Google com senha
- Status: done
- Covers: REQ-008, REQ-032, REQ-036, REQ-038
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: WC-06 (email verificado de 254 caracteres quebra dentro do painel; senha de 128 com "Mostrar" visível); WC-12 (resposta lenta); ART-02 ("Vincular e entrar" único preenchido).
- Blocked by: T-110, T-097, T-100
- Demo: A tela de vincular Google pede a senha e vincula.
- RED: `GoogleLinkPage.test.tsx`: texto com o email verificado e campo de senha; senha vazia "Informe a senha da conta."; > 128 "A senha pode ter no máximo 128 caracteres." sem chamada de vínculo; senha incorreta -> Alert "Senha incorreta. O Google não foi vinculado." (permanece em S5); atraso > 2 s -> RateLimitNotice; "Esqueci minha senha" e "Cancelar" (descarta a pendência e volta a S2); pendência expirada -> `/entrar?aviso=google-falhou`; sucesso -> `/perfil` com "Google vinculado. Da próxima vez você pode entrar com o Google."
- GREEN: página S5.
- REFACTOR: reutiliza PasswordField.
- Definition of done: DoD-P.

## T-113: S6 Esqueci minha senha
- Status: done
- Covers: REQ-028, REQ-032
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-05 (Alert de sucesso só com `opacity`); WC-06 (email de 254 caracteres no campo); WC-12 (resposta lenta e aviso após 2 s); ART-02 ("Enviar instruções" único preenchido).
- Blocked by: T-097, T-100, T-105
- Demo: A tela de esqueci minha senha pede o email e confirma o envio.
- RED: `ForgotPasswordPage.test.tsx`: erro de formato com as mensagens de S1 (não revela conta); **UX-02**: o mesmo Alert `role="status"` "Se existir uma conta com esse email, enviaremos instruções para redefinir a senha. Confira também a caixa de spam." para `202` de email existente, só-Google e inexistente e também quando o envio falha no servidor (o servidor responde 202); atraso > 2 s -> RateLimitNotice; erro de rede -> "Não foi possível processar o pedido agora. Tente de novo."; link "Voltar para entrar".
- GREEN: página S6.
- REFACTOR: texto genérico em constante única.
- Definition of done: DoD-P.

## T-114: S7 Redefinir senha
- Status: done
- Covers: REQ-029, REQ-030, REQ-031, REQ-038
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: WC-06 (senha de 128 com "Mostrar" visível); ART-02 ("Salvar nova senha" ou, na tela de link inválido, "Solicitar novo link" como único preenchido); MOT-10 (sem animação de troca de tela).
- Blocked by: T-097, T-105, T-106
- Demo: A tela de redefinir senha troca a senha com o token.
- RED: `ResetPasswordPage.test.tsx`: ao carregar lê `#token=` do fragmento, guarda só em memória e chama `history.replaceState` (**UX-11**: o token some da barra de endereço e do histórico; recarregar leva à tela de link inválido); senha curta/longa -> erro de campo, campo limpo com foco, token continua aceito num segundo envio válido (**UX-03**); token inválido/expirado/usado/ausente -> tela "Link inválido ou expirado" + "Este link de redefinição não pode ser usado. Solicite um novo." + botão "Solicitar novo link" (mesmo texto nos três casos); sucesso -> `/entrar?aviso=senha-alterada` sem sessão iniciada; página servida sem `Referer` (verificado em T-005/T-077).
- GREEN: página S7.
- REFACTOR: hook `useResetToken`.
- Emenda 2026-11-01 (R-26, aprovada pelo usuário na triagem do retro; design.md S7 e A.8.1): "Solicitar novo link" é um **link** independente para `/esqueci-senha` (semântica de navegação), não um botão; no RED acima e no critério ART-02, onde se lê "botão 'Solicitar novo link'" / "como único preenchido", vale: a tela de link inválido não tem botão preenchido, e o link é o maior elemento. Com senha curta/longa o ErrorSummary aparece e o foco termina no campo (exceção ao A11Y-04 registrada no design). Comportamento já implementado; sem tarefa de código nova.
- Definition of done: DoD-P.

## T-115: S8 Perfil
- Status: done
- Covers: REQ-013, REQ-016, REQ-017, REQ-044, REQ-045, REQ-046
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: WC-01 (nomes longos a 320, 768 e 1280px sem rolagem horizontal nem texto cortado), WC-02 (`overflow-wrap: anywhere`, `min-width: 0`, avatar com `flex-shrink: 0`, `dir="auto"` no nome e na bio, altura de linha >= 1,4), WC-04 (bio de 280 elementos, de 280 quebras de linha e de uma palavra de 280 caracteres limitada a 10 linhas com "Mostrar tudo", `aria-expanded`, texto completo alcançável, sem animar `height` - MOT-15), WC-05 (`<b>x</b>`, `&amp;` e `**x**` literais), WC-08 (Tags de Formatos quebram; 0, 1, 50 e nome de 60 caracteres), WC-10 (avatar que não chega), WC-13 (Skeleton no tamanho real, sem brilho - MOT-09 -, "Carregando..." após 2 s, sem deslocamento de layout), WC-17 (todos os opcionais ausentes: quadro, título, avatar, "Nenhuma bio." e "Nenhum formato selecionado." mantidos); MOT-05 (alerts de sucesso); ART-02 ("Editar perfil" único preenchido; "Excluir conta" em contorno de perigo na zona de perigo); a ficha (TCG favorito e Formatos em Tags) aparece acima da dobra a 320px.
- Blocked by: T-096, T-102, T-103, T-106
- Demo: A tela de perfil mostra os dados do jogador.
- RED: `ProfilePage.test.tsx`: Skeleton `aria-busy` e texto "Carregando..." após 2 s; mostra avatar 96 px, nome, "Magic: The Gathering" (conta nova já exibe Magic - UX-06), Formatos como Tags ou "Nenhum formato selecionado.", bio ou "Nenhuma bio."; **UX-05**: nem a tela nem a resposta de rede contêm email ou hash; **A11Y-35**: nome e bio com HTML digitado aparecem literalmente como texto (sem `dangerouslySetInnerHTML`), quebras de linha da bio preservadas; **UX-13**: Formato inativado não aparece e reaparece se reativado; erro de carga -> "Não foi possível carregar seu perfil." + "Tentar de novo"; 401 -> S2 sessão expirada; `?exclusao=confirmar` abre a confirmação final de S11 e `?exclusao=falhou` mostra "Não foi possível confirmar sua identidade. Sua conta não foi excluída."; o parâmetro é removido da URL após lido; botões "Editar perfil", "Alterar avatar", "Excluir conta" na zona de perigo; alert de sucesso `role="status"` quando vem de salvar/criar/vincular/avatar.
- GREEN: página S8.
- REFACTOR: leitura do perfil em hook, sem cache (design.md: perfil e sessão não são cacheados).
- Definition of done: DoD-P.

## T-116: S9 Editar perfil
- Status: done
- Covers: REQ-014, REQ-015, REQ-016, REQ-017, REQ-037, REQ-040, REQ-046
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: WC-01 (nomes longos no campo de nome), WC-05 (marcação digitada permanece literal), WC-08 (Formatos: S9 em uma coluna, duas a partir de 640px, sem rolagem interna; catálogo em carregamento mostra Skeleton; falha mostra a mensagem e o salvar continua), WC-13 (Skeleton sem deslocamento), WC-18 e ART-05 (contador da bio com `Intl.NumberFormat("pt-BR")` e `tabular-nums`, "281 de 280" sem "NaN"); MOT-15 (contador sem animação); ART-02 ("Salvar alterações" único preenchido).
- Blocked by: T-098, T-104, T-115
- Demo: A tela de editar perfil salva e mostra erros por campo.
- RED: `EditProfilePage.test.tsx`: carrega nome, bio, TCG (Select só com "Magic: The Gathering"), Formatos como caixas vindas de `GET /catalog/formats` (catálogo vazio/falha -> "Nenhum formato disponível no momento." e salvar continua possível); contador em grapheme clusters ("281 de 280" vira erro) ligado por `aria-describedby`; mensagens exatas (nome 3-30, caracteres de controle, bio máxima, bio com caracteres de controle, "Escolha um TCG favorito válido.", "Escolha apenas formatos da lista."); quebra de linha na bio aceita; nome repetido aceito; Formato inativo já salvo não aparece e salvar funciona sem removê-lo (**UX-13**); sucesso -> S8 com "Perfil atualizado."; erro de servidor "Não foi possível salvar. Tente de novo." preservando dados; Cancelar volta a S8 sem confirmação de descarte; "Alterar avatar" abre S10 sem enviar nem descartar os demais campos.
- GREEN: página S9.
- REFACTOR: formulário e ErrorSummary compartilhados.
- Definition of done: DoD-P.

## T-117: S10 Dialog alterar avatar
- Status: done
- Covers: REQ-018, REQ-019, REQ-020, REQ-021, REQ-022, REQ-049
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: estado novo decidido pelo usuário - falha ou lista vazia de presets mostra "Não foi possível carregar os avatares. Tente de novo." e o envio de arquivo continua disponível (WC-09); WC-09 (8 e 12 presets em 4 colunas, 3 a 320px, alvos de 44px; carregando = círculos cinza), WC-10 (imagem do preset que não chega), WC-11 (nome de arquivo de 200 caracteres; pré-visualização 1x1, 4096x4096 e 1:5); MOT-12/MOT-13 (abrir/fechar), MOT-14 (pré-visualização), MOT-15 (troca do avatar no perfil e no AppHeader instantânea); ART-02 ("Usar este avatar" único preenchido, "Enviar imagem" secundário).
- Blocked by: T-099, T-101, T-115
- Demo: O diálogo de avatar troca entre preset e imagem enviada.
- RED: `AvatarDialog.test.tsx`: preset atual pré-selecionado; "Usar este avatar" chama `PUT /me/avatar/preset` e fecha com foco no gatilho e "Avatar atualizado." (A11Y-11); validação do cliente (tipo, 2 MB, arquivo ausente); upload chama `POST /me/avatar/custom` com "Enviando imagem..."; **qualquer** rejeição do servidor (`size`, `type`, `content`, animada, dimensões, 4097 px) exibe a mesma "Não foi possível usar este arquivo. Envie uma imagem JPEG, PNG ou WebP de até 2 MB." e mantém o avatar atual; erro de rede "Não foi possível enviar agora. Tente de novo."; **UX-07** o avatar do perfil e do header muda sem recarregar após sucesso e permanece após erro; Esc fecha; sem arrastar-e-soltar; o preset padrão é o avatar inicial (REQ-022).
- GREEN: dialog S10.
- REFACTOR: atualização do contexto de sessão com o novo `Avatar`.
- Definition of done: DoD-P.

## T-118: S11 Dialog excluir conta - conta com senha
- Status: done
- Covers: REQ-023, REQ-024, REQ-025, REQ-026, REQ-027, REQ-032
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-12/MOT-13; WC-14 (diálogo de 320x480, texto a 200% e celular deitado rola por inteiro com botões alcançáveis); WC-15 ("Excluir minha conta definitivamente" quebra em duas linhas a 320px, em largura total, altura >= 44px); WC-06 (senha de 128 com "Mostrar"); WC-12 (resposta lenta); ART-02 (botão de perigo é o único preenchido e nunca o foco inicial); A11Y-38 (borda do diálogo).
- Blocked by: T-099, T-100, T-115
- Demo: O diálogo de exclusão confirma com a senha.
- RED: `DeleteAccountDialog.test.tsx` (`alertdialog`): texto de aviso (imediata, irreversível, dados e decks), caixa "Entendo que a exclusão é definitiva.", campo de senha; caixa desmarcada -> "Marque a caixa para confirmar que entende que a exclusão é definitiva."; senha vazia -> "Informe sua senha para confirmar."; senha incorreta -> "Senha incorreta. Sua conta não foi excluída." e a conta segue existente; o botão de perigo nunca é o foco inicial; Esc e "Cancelar" fecham sem excluir e devolvem o foco a "Excluir conta" (A11Y-06/11/12); durante o envio o Esc não fecha; atraso > 2 s -> RateLimitNotice; sucesso -> `/entrar?aviso=conta-excluida` sem sessão (**UX-08**: "Voltar" não exibe S8/S9 com dados).
- GREEN: dialog S11, ramo `hasPassword`.
- REFACTOR: textos de aviso em constantes.
- Definition of done: DoD-P.

## T-119: S11 Dialog excluir conta - conta Google e confirmação final
- Status: done
- Covers: REQ-023, REQ-026, REQ-042
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-12/MOT-13 também na troca para o diálogo "Confirmar exclusão"; WC-14, WC-15 e ART-02 como em T-118 (botão de perigo único preenchido, não é o foco inicial); A11Y-37 (só opacidade com movimento reduzido).
- Blocked by: T-118, T-110
- Demo: O diálogo de exclusão confirma com o Google e a confirmação final.
- RED: `DeleteAccountGoogleDialog.test.tsx`: sem senha local mostra "Para confirmar, entre novamente com o Google." e "Reautenticar com o Google" (navega para `start?intent=reauth`, exige a caixa marcada); após `/perfil?exclusao=confirmar` abre "Confirmar exclusão" ("Você confirmou sua identidade. Excluir sua conta agora?") com Cancelar e botão de perigo (não é o foco inicial); `POST /me/deletion` `204` -> `/entrar?aviso=conta-excluida`; `403 reauth_expired` -> "Sua confirmação expirou. Reautentique com o Google para excluir a conta." e volta ao primeiro dialog, conta intacta (**UX-14**); Cancelar/Esc -> perfil inalterado; `?exclusao=falhou` -> aviso em S8.
- GREEN: ramo Google do dialog.
- REFACTOR: escolha do ramo por `hasPassword` em um único ponto.
- Definition of done: DoD-P.

## T-120: Logout pelo AccountMenu e histórico sem dados
- Status: done
- Covers: REQ-010, REQ-011
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: MOT-10 (sem animação na troca de rota), MOT-11 (o menu fecha com a saída de 100ms antes de navegar) e MOT-15 (limpeza instantânea do avatar e do nome no header).
- Blocked by: T-102, T-106, T-115
- Demo: O logout pelo menu leva à tela inicial sem dados do jogador no histórico.
- RED: "Sair" chama `POST /auth/logout`, limpa o contexto de sessão (sem cache de perfil, conforme design.md), vai a `/entrar?aviso=saiu` com "Você saiu da sua conta."; **UX-09**: "Voltar" do navegador após logout não mostra S8/S9 com dados (re-consulta a sessão e vai ao login); idem após exclusão de conta; 401 posterior limpa o estado.
- GREEN: ação de logout e limpeza.
- REFACTOR: um ponto de "encerrar sessão no cliente".
- Definition of done: DoD-P.

## Grupo G - Ponta a ponta (Playwright)

## T-121: Ambiente E2E (host de teste, Playwright, ganchos só de teste)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: fluxos F1-F5 no navegador (T-122 a T-127)
- Blocked by: T-094, T-120
- Demo: O ambiente E2E sobe e um teste Playwright mínimo passa.
- RED: `E2eEnvironmentGuardTests` em `Api.Tests`: os endpoints só de teste (leitura do último link de redefinição enviado e **avanço do relógio falso** `POST /__test/clock/advance`) respondem `404` em qualquer ambiente diferente de `E2E` (testado em `Development`, `Production` e sem ambiente) e funcionam em `E2E` (o avanço move o `FakeTimeProvider` e expira sessão/pendência/reautenticação); a regra do T-002 garante que `Deckloom.Api` de produção não referencia `*.Testing` nem o host de teste; em `E2E` a API usa persistência em memória, `StubGoogleOidcServer` e o processador de imagem SkiaSharp **real**; nenhum segredo real; o smoke de Playwright `smoke.spec.ts` (abrir `/entrar` e ver o `<h1>`) falha antes do ambiente existir.
- GREEN: criar o projeto de host `backend/tests/Deckloom.Api.E2E.Host` e incluí-lo na solution (referencia `Deckloom.Api` e `Testing`; hospeda os endpoints só de teste e a composição com fakes em memória; não faz parte do artefato de produção, e a API de produção nunca referencia fakes), configuração do Playwright em `frontend/e2e/`, script `npm --prefix frontend run e2e`, `@axe-core/playwright`.
- REFACTOR: fixtures de Playwright para sessão e para o stub do Google.
- Definition of done: DoD-P; o ambiente E2E nunca usa o banco do sistema.

## T-122: E2E - cadastro, login, logout e sessão
- Status: done
- Covers: REQ-001, REQ-004, REQ-010, REQ-011
- Owner: implementer
- Blocked by: T-121, T-107, T-109, T-120
- Demo: O E2E de cadastro, login, logout e sessão passa no navegador.
- RED: `auth-email.spec.ts`: cadastro com termos e 18+ cai em `/perfil` com "Conta criada. Bem-vindo(a) ao Deckloom!" e mostra Magic; logout leva a `/entrar` com "Você saiu da sua conta." e "Voltar" não mostra dados (UX-09); login com lembrar-me; rota protegida sem sessão -> login com aviso e `retorno` interno respeitado, externo ignorado; verificação `axe` em cada tela do fluxo.
- GREEN: ajustes mínimos que o fluxo expuser.
- REFACTOR: page objects.
- Definition of done: DoD-P; spec verde no ambiente E2E.

## T-123: E2E - fluxos do Google (novo, existente, vínculo, cancelamento)
- Status: done
- Covers: REQ-006, REQ-007, REQ-008, REQ-009, REQ-036, REQ-039
- Owner: implementer
- Blocked by: T-121, T-111, T-112, T-110
- Demo: O E2E dos fluxos do Google passa no navegador.
- RED: `auth-google.spec.ts` com o stub: email novo -> S4 com nome do Google editável, conta só criada após nome/termos/18+ e entrada em `/perfil`; conta Google existente entra direto sem S4; email de conta com senha -> S5, senha correta vincula, errada não vincula; cancelamento e email não verificado -> `/entrar` com a mensagem única; pendência expirada (avanço do relógio pelo endpoint de teste de T-121, > 10 min) -> volta ao login.
- GREEN: ajustes mínimos.
- REFACTOR: stub parametrizável por teste.
- Definition of done: DoD-P.

## T-124: E2E - redefinição de senha
- Status: done
- Covers: REQ-028, REQ-029, REQ-030, REQ-031
- Owner: implementer
- Blocked by: T-121, T-113, T-114
- Demo: O E2E da redefinição de senha passa no navegador.
- RED: `password-reset.spec.ts`: pedir redefinição (mesma mensagem para email existente e inexistente); abrir o link capturado pelo endpoint de teste; o token some da URL e do histórico (UX-11); senha curta não consome o token; nova senha válida -> `/entrar` com "Senha alterada. Entre com a nova senha." e a sessão anterior encerrada; reutilizar o link ou um link antigo -> "Link inválido ou expirado" (UX-03); novo pedido invalida o anterior.
- GREEN: ajustes mínimos.
- REFACTOR: helper de leitura do último email.
- Definition of done: DoD-P.

## T-125: E2E - perfil e avatar (SkiaSharp real)
- Status: done
- Covers: REQ-014, REQ-018, REQ-019, REQ-020, REQ-049
- Owner: implementer
- Blocked by: T-121, T-116, T-117
- Demo: O E2E de perfil e avatar passa com o SkiaSharp real.
- RED: `profile-avatar.spec.ts`: editar nome, bio e Formatos e ver o perfil atualizado; escolher preset; upload de JPEG válido com EXIF -> novo avatar no perfil e no header sem recarregar (UX-07); `.exe`, GIF, imagem > 2 MB, arquivo de texto "foto.png", WebP animado e 4097x4096 -> a mesma mensagem e o avatar anterior mantido; 4096x4096 aceita; a imagem servida é `image/webp` sem EXIF.
- GREEN: ajustes mínimos.
- REFACTOR: fixtures compartilhadas com os testes do processador (T-070 a T-072).
- Definition of done: DoD-P.

## T-126: E2E - exclusão de conta (senha e Google)
- Status: done
- Covers: REQ-023, REQ-024, REQ-025, REQ-026, REQ-027, REQ-042
- Owner: implementer
- Blocked by: T-121, T-118, T-119
- Demo: O E2E de exclusão de conta passa para conta com senha e Google.
- RED: `account-deletion.spec.ts`: conta com senha - senha errada mantém a conta, certa exclui e leva a `/entrar` com "Sua conta foi excluída."; login com as credenciais antigas falha com a mensagem genérica; novo cadastro com o mesmo email cria perfil vazio; conta Google - reautenticação e confirmação em 2 min exclui; confirmação após 6 min (avanço do relógio pelo endpoint de teste de T-121) mostra "confirmação expirada" e a conta continua (UX-14); "Voltar" após excluir não mostra dados (UX-08).
- GREEN: ajustes mínimos.
- REFACTOR: reaproveita fixtures de T-122/T-123.
- Definition of done: DoD-P.

## T-127: E2E - varredura de acessibilidade, teclado, responsivo e direção de arte
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Critérios de direção de arte (design A.8), no RED e no DoD: ART-02 (contagem no e2e: no máximo um botão preenchido por tela e no máximo 4 matizes, a 1280, 768 e 390px), ART-08 (altura >= 44px e sem rolagem horizontal a 320px), WC-14 (e2e: diálogo mais alto que a janela a 320x480, texto a 200%, celular deitado), WC-01 (nomes longos a 320, 768 e 1280px nas telas S4, S8, S9 e AccountMenu, com `?data=worst` do build de desenvolvimento), A11Y-37/A11Y-38 com as mídias emuladas, A11Y-39 (revisão); ART-09: rodar `inspect.mjs --app` a 1440, 768 e 390 sem `FAIL` (o botão primário azul é a decisão E1, relatado como decidido, sem `--allow` silencioso), ler os screenshots e seguir o formato de `references/review.md`; o `frontend` declara no PR se não conseguiu rodar por falta de Node 22 ou Chrome.
- Habilita: A11Y-07, A11Y-10, A11Y-18, A11Y-19, A11Y-32; UX-10
- Blocked by: T-122, T-123, T-124, T-125, T-126
- Demo: A varredura de acessibilidade, teclado e responsivo passa em todas as telas.
- RED: `accessibility.spec.ts`: `@axe-core/playwright` sem violações WCAG 2.1 AA em S1-S11 (e nos dialogs); fluxo completo só por teclado em cada tela, ordem de Tab = ordem do DOM sem `tabindex` positivo (A11Y-07/10); viewports 320, 768 e 1280 px sem sobreposição nem rolagem horizontal, e zoom 400% sem perda de função (A11Y-18, UX-10); texto a 200% e espaçamento aumentado sem corte (A11Y-19); nada essencial depende de hover (A11Y-32). Itens que exigem julgamento humano ficam na lista manual abaixo.
- GREEN: correções de CSS/semântica que os testes expuserem.
- REFACTOR: tokens ajustados sem quebrar contraste (T-095).
- Definition of done: DoD-P; lista manual para o `qa` entregue no PR: leitor de tela anuncia sucesso, erros e aviso de atraso (A11Y-16, A11Y-17, A11Y-31, A11Y-34), qualidade do texto alternativo (A11Y-15) e legibilidade a 200% (A11Y-19).

## T-128: Foco no `<h1>` em transição de rota (na carga completa sem foco programático, A11Y-23/34; emenda de 2026-10-31, QA-03), título único por rota e aviso de sessão expirada (comportamento de tela)
- Status: done
- Covers: REQ-011, REQ-012
- Owner: frontend
- Blocked by: T-106, T-102
- Demo: Ao trocar de rota, o foco vai ao `<h1>` e o título da página é único.
- Habilita: A11Y-20, A11Y-31, A11Y-34, MOT-10 em todas as telas S1-S11 (todas as tarefas T-107 a T-120 usam esta; ordem recomendada: antes das telas)
- RED: `RouteFocus.test.tsx` (RTL + axe): em toda transição de rota o foco vai ao `<h1>` (`tabindex="-1"`) e o `<title>` é único por tela, com exatamente um `<h1>` e níveis sem saltos (A11Y-20/34); a troca de rota não anima (MOT-10); rota de Jogador com sessão expirada vai a `/entrar?aviso=sessao-expirada` com o aviso `role="alert"` recebendo o foco em transição de rota na SPA e nenhum dado visível (A11Y-31); exceção de carga completa (ex.: `/entrar?aviso=sessao-expirada` aberto direto): sem movimento de foco, o aviso é anunciado pelo `role="alert"` após a montagem e o primeiro Tab chega ao SkipLink (A11Y-23/31/34; emenda de 2026-10-31, QA-03); `prefers-reduced-motion` não altera nada aqui (A11Y-37).
- GREEN: componente/hook de foco de rota e títulos por rota sobre o router de T-106.
- REFACTOR: tabela rota -> título num módulo.
- Definition of done: DoD-P (incluindo DoD-F); zero violações `axe`.

## T-129: Arquivos SVG dos avatares preset (8 a 12, neutros) e correspondência com a carga do banco
- Status: done
- Covers: REQ-018, REQ-022
- Owner: frontend
- Blocked by: T-065, T-096
- Demo: Os SVG dos avatares existem e correspondem à carga do banco.
- RED: `PresetAssetsTests` (higiene do repositório, leitura de arquivos): para cada id de preset da carga de T-065 existe `frontend/public/avatars/presets/<id>.svg`, e vice-versa; entre 8 e 12 arquivos; cada SVG é autocontido (sem `href` externo, sem script), tem `viewBox` quadrado e peso baixo; o preset padrão existe (REQ-022); teste visual do `Avatar` com cada preset a 32, 40 e 96px sem deformar (WC-02/WC-09).
- GREEN: os SVGs neutros (decisão U11/M5: placeholders sem personagem de terceiros, sem cor de marca além dos tokens, ART-01 sem gradiente nem brilho) e rótulos acessíveis em pt-BR coerentes com `avatar_preset.label`.
- REFACTOR: um único gabarito de SVG para todos os presets.
- Definition of done: DoD-P (incluindo DoD-F); ART-09 declarado.

## T-130: Fixtures de dados extremos atrás de `?data=worst` (só em desenvolvimento)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: frontend
- Blocked by: T-003, T-103
- Demo: Com `?data=worst` as telas mostram dados extremos em desenvolvimento.
- Habilita: WC-01 a WC-18 e o ART-09 nas telas (as telas T-107 a T-120 e T-127 usam estas fixtures)
- RED: `worstFixtures.test.ts`: o módulo fornece as fixtures de A.8.5 (vazio, um, enorme, longo, curto, faltando, estranho, lento, erro) como respostas do `apiClient` (MSW); **WC-16**: com o build de produção (`vite build`), nenhum arquivo de `dist/` contém as fixtures nem a leitura de `?data=worst` (varredura de marcadores no pacote final); em desenvolvimento, `?data=worst&fixture=<nome>` ativa a fixture; `?data=worst` é ignorado quando `import.meta.env.DEV` é falso.
- GREEN: módulo de fixtures importado dinamicamente só sob `import.meta.env.DEV`, para ser eliminado no build.
- REFACTOR: fixtures nomeadas por A.8.5, reutilizadas pelos testes de componente.
- Definition of done: DoD-P (incluindo DoD-F); teste do build de produção verde.

## Grupo H - Controle de acesso ao banco (emenda ADR-0015; decisões DB1-DB12 e UA1-UA5 do design)

Todas as tarefas deste grupo são estáticas ou com fakes/SQLite em memória: nenhuma conecta a um PostgreSQL, lê `.env` ou executa scripts de banco. O que não dá para provar assim está em "Validação manual por humano (ADR-0015)". `Owner: implementer` em todas (sem tela). Exceção de numeração: as tarefas ficam fora da ordem dos grupos A-G porque nasceram da emenda, mas respeitam as dependências indicadas.

## T-131: Regra de arquitetura - nenhuma migração nem DDL em tempo de execução (estende T-002)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: DB6 (nenhuma DDL/migração em tempo de execução); defesa em profundidade de `deckloom_app` sem `CREATE` (ADR-0015); T-002 já concluída, esta tarefa acrescenta a regra 9 sem reescrevê-la
- Blocked by: T-002
- Demo: O teste de arquitetura falha se houver migração ou DDL em tempo de execução.
- RED: em `Deckloom.ArchitectureTests` (`NoRuntimeMigrationTests`), varredura sintática dos arquivos `.cs` de `backend/src/**` (exclui `bin/`, `obj/` e **todo `backend/tests/`**, pois as fixtures SQLite usam `EnsureCreated` legitimamente; Roslyn ou regex que ignora comentários e literais de string): falha se qualquer invocação tiver como nome de membro `Migrate`, `MigrateAsync`, `EnsureCreated`, `EnsureCreatedAsync`, `EnsureDeleted`, `EnsureDeletedAsync`, `GetPendingMigrations` ou `GetPendingMigrationsAsync`; a mensagem de falha aponta o arquivo/linha e a regra do design ("Nenhuma DDL nem migração em tempo de execução"). Teste da própria regra com trechos de código em string (positivo e negativo, inclusive comentário e literal que citam o nome sem chamar, que **não** falham). Provar o vermelho adicionando temporariamente `db.Database.Migrate()` em `Deckloom.Api` e `EnsureCreated()` em `Infrastructure`, e revertendo. Segundo teste: nenhum registro de health check em `backend/src` usa `AddDbContextCheck` com consulta de migrações (a regra acima cobre o método; o teste confirma que o health check não chama `GetPendingMigrations`).
- GREEN: implementar a regra como dados (lista de nomes proibidos) ao lado das regras de T-002.
- REFACTOR: mensagens com a regra do design; lista de nomes proibidos num único ponto; nota no cabeçalho do teste explicando por que `tests/` fica fora.
- Definition of done: DoD-P; regra verde no código atual e comprovadamente vermelha com a violação temporária revertida.

## T-132: Parser da matriz de privilégios e conformidade com `Database/tables/`
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: DB3 (privilégios por tabela/coluna); base de T-066 (GRANT das migrações) e T-133 (UPDATE gerado); ADR-0015
- Blocked by: T-002
- Demo: Um teste lê a matriz de privilégios e confere cada tabela de `Database/tables/`.
- RED: em `Deckloom.ArchitectureTests` (`PrivilegeMatrixTests`, helper `PrivilegeMatrix` em `Support/`): (1) o parser lê a tabela "Tabelas" de `Database/roles/privilege-matrix.md` no formato estrito do próprio arquivo (`x` = tabela inteira, `-` = não concedido, `(col_a, col_b)` = `UPDATE` só nessas colunas) e devolve, por tabela qualificada, `Select`, `Insert`, `Delete` e `UpdateColumns` (testes do parser com tabelas em string: linha válida, `UPDATE` `-`, `UPDATE` com lista, `x` em `UPDATE` tratado como tabela inteira, célula inválida e cabeçalho ausente falham com mensagem clara); (2) **exatamente uma linha por arquivo** `Database/tables/<schema>.<tabela>.md` (README excluído): falha se faltar linha para uma tabela, se sobrar linha sem arquivo ou se houver linha duplicada; (3) toda coluna listada em `UPDATE (...)` existe na tabela correspondente em `Database/tables/<tabela>.md`; (4) invariantes da matriz: nenhuma linha concede `UPDATE` em `accounts.access_attempt`, `accounts.avatar_image`, `accounts.pending_google_auth`, `accounts.player_external_identity`, `accounts.player_preferred_format`; `accounts.avatar_preset` e `deck_suggestion.format` só `SELECT`; `email`, `id`, `terms_version`, `terms_accepted_at`, `adult_declared` e `created_at` nunca aparecem em `UPDATE` de `accounts.player`; as tabelas `__ef_migrations_history` e a sequência de identidade constam como "nenhum". Provar o vermelho removendo temporariamente uma linha da matriz e acrescentando uma coluna inexistente, e revertendo.
- GREEN: o parser e os testes; se o arquivo real divergir de `Database/tables/`, **parar e reportar** (o `architect` corrige a matriz; nada é alterado em silêncio).
- REFACTOR: o parser é o único leitor da matriz (reutilizado por T-066 e T-133); mensagens citam a linha da matriz.
- Definition of done: DoD-P; testes verdes contra os arquivos reais e comprovadamente vermelhos com a violação temporária.

## T-133: Adapters EF - colunas dos `UPDATE` gerados contidas na matriz (UA1)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: DB3 e UA1 (`UPDATE` por coluna); garante que `deckloom_app` nunca precise de privilégio além da matriz; imutabilidade de `email` e da evidência do aceite
- Blocked by: T-132, T-063
- Demo: Testes mostram que os `UPDATE` gerados só tocam colunas permitidas pela matriz.
- RED: em `Deckloom.Accounts.Infrastructure.Tests` (`GeneratedUpdateColumnsTests`, SQLite em memória, ADR-0012): interceptor `UpdateStatementCapture` (`DbCommandInterceptor`) registra cada `UPDATE`/`DELETE`/`INSERT` executado; para cada fluxo (edição de perfil com nome/bio/TCG/Formatos, troca de avatar preset -> custom -> preset, redefinição de senha e `NeedsRehash`, emissão do token de redefinição que sobrescreve a linha, renovação por requisição, revogação, `RevokeAllFor` e reautenticação da sessão, consumo de pendência Google, registro e limpeza de `access_attempt`, exclusão em cascata) afirma: toda coluna do `SET` de cada `UPDATE` pertence ao `UPDATE (...)` da matriz para a tabela (tabela sem `UPDATE` na matriz -> nenhum `UPDATE` gerado); `DELETE`/`INSERT` só em tabelas com `x` na matriz; nenhum `UPDATE` contém `email`, `terms_*`, `adult_declared`, `created_at`, `id`, `token_hash` de sessão ou `player_id`. Segundo teste estático (varredura de `backend/src/**/Infrastructure`, mesmo método de T-131): proibidos `DbSet.Update`, `UpdateRange`, `Attach`, `AttachRange`, `DbContext.Update(...)` e `Entry(...).State = EntityState.Modified`/`Entry(...).Property(...).IsModified = true` em grafos desanexados (o EF só deve gerar `UPDATE` das colunas realmente alteradas de entidades rastreadas). Provar o vermelho trocando temporariamente um repositório por `DbSet.Update(entity)` (ele gera `SET` de todas as colunas, inclusive `email`) e revertendo.
- GREEN: ajustar os repositórios para entidades rastreadas e atribuição só das propriedades alteradas; concorrência otimista continua por token (`xmin` em PostgreSQL, token de teste no SQLite), que só aparece no `WHERE`.
- REFACTOR: o interceptor vira utilitário compartilhado em `Deckloom.Accounts.Testing` (reutilizado por T-138); mapa fluxo -> tabela legível.
- Definition of done: DoD-P; todos os fluxos de escrita cobertos; comprovado vermelho com `DbSet.Update`. Limite conhecido (ADR-0012): o SQL do PostgreSQL/Npgsql pode diferir do SQLite (`RETURNING`, `xmin`); a conferência no PostgreSQL real é o item H-3/H-8 da validação manual.

## T-134: Decisão da verificação de papel na inicialização (DB7) - função pura
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: DB7 e UA2 (a API recusa-se a subir com papel privilegiado); mitigação "superusuário usado pela API" do threat model
- Blocked by: T-001
- Demo: Testes mostram a função pura decidindo se o papel de conexão é aceitável.
- RED: em `Deckloom.Accounts.Infrastructure.Tests` (`DatabaseRolePolicyTests`, dados falsos, sem conexão): `DatabaseRolePolicy.Evaluate(RoleSnapshot)` devolve `Accepted` para o papel conforme (nenhum atributo perigoso, sem `CREATE` no banco e nos schemas `accounts`/`deck_suggestion`, sem posse de objetos); devolve `Rejected(reasons)` para **cada** condição isoladamente: superusuário, `CREATEROLE`, `CREATEDB`, `BYPASSRLS`, `CREATE` no banco, `CREATE` em `accounts`, `CREATE` em `deck_suggestion`, dono de objeto; combinações devolvem todas as razões; a mensagem de cada razão é um código estável que diz **só qual condição falhou** (sem nome de papel, host, banco nem valor de connection string; teste de varredura de strings nas mensagens).
- GREEN: `RoleSnapshot` (record de atributos), `DatabaseRolePolicy` (função pura) e as razões.
- REFACTOR: razões como enum/constantes; lista de condições como dados.
- Definition of done: DoD-P; cada uma das oito condições coberta isoladamente. A consulta ao catálogo é T-135 e é validada manualmente (limite do SQLite).

## T-135: Verificação de papel na inicialização (DB7) - consulta e falha rápida
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: DB7 e UA2 ponta a ponta na inicialização da API
- Blocked by: T-134, T-094
- Demo: Testes mostram a API falhando rápido na inicialização com papel indevido.
- RED: `StartupRoleCheckTests` em `Deckloom.Api.Tests` com `IDatabaseRoleInspector` falso: com o adapter de persistência PostgreSQL registrado, a inicialização do host executa a verificação **antes** de aceitar requisições; snapshot não conforme -> o host não inicia (exceção de inicialização) com mensagem que lista só os códigos das condições, sem valores de conexão; snapshot conforme -> inicia; **falha ao consultar** (banco indisponível, permissão negada ao catálogo) -> também recusa iniciar com mensagem genérica (fail closed; o design não prevê outro comportamento); com os fakes de persistência da fábrica de teste e do host `E2E` a verificação **não** é registrada nem executada (teste de que o inspetor não é chamado); nenhuma verificação de migrações pendentes.
- GREEN: porta `IDatabaseRoleInspector` e `NpgsqlDatabaseRoleInspector` (consulta somente leitura ao catálogo: atributos de `current_user` em `pg_roles`, `has_database_privilege`/`has_schema_privilege` com `CREATE` e posse de objetos em `accounts`/`deck_suggestion`; não validável pelo SQLite), registrados em `Deckloom.Api.Composition` só com o adapter PostgreSQL; `IHostedService` de inicialização (ou `IStartupFilter`) que aplica `DatabaseRolePolicy`.
- REFACTOR: consulta em constante única e documentada; log da recusa sem valores sensíveis.
- Definition of done: DoD-P; nenhum teste conecta a banco; a consulta real é validada por humano (H-2, H-9).

## T-136: TLS do banco - `SSL Mode=VerifyFull` obrigatório fora de `Development` e configuração sem credencial privilegiada
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: DB8, UA3 (TLS no transporte do banco); DB1/DB2 (a API nunca recebe credencial de superusuário, dono ou migrador); mitigação "banco acessado sem TLS" e "superusuário usado pela API"
- Blocked by: T-094
- Demo: Testes mostram a API recusando TLS fraco fora de Development e credencial privilegiada.
- RED: `DatabaseConnectionOptionsTests` e `CompositionTests` (acréscimos) em `Deckloom.Api.Tests`, com connection strings fictícias escritas no teste: fora de `Development` (`Production`, `Staging` e nome desconhecido) a inicialização **recusa** `SSL Mode` ausente (padrão do Npgsql), `Disable`, `Allow`, `Prefer`, `Require` e `VerifyCA`, e `VerifyFull` sem `Root Certificate`; aceita `VerifyFull` com `Root Certificate`; em `Development` aceita `SSL Mode=Disable` (exceção do compose de dev); `Username` igual a `postgres`, `deckloom_owner` ou `deckloom_migrator` é recusado em qualquer ambiente; a configuração carregada (`IConfiguration`, incluindo variáveis de ambiente achatadas) com chave que comece por `POSTGRES_` ou `DECKLOOM_MIGRATOR_` faz a inicialização falhar; a mensagem de falha diz só qual regra foi violada (sem host, usuário, senha nem caminho do certificado; varredura de strings); teste estático: nenhum `appsettings*.json` de `Deckloom.Api` contém `Password=`, connection string nem essas chaves.
- GREEN: `DatabaseConnectionOptions` com validação (`ValidateOnStart`) usando `NpgsqlConnectionStringBuilder`, ligada em `AddAccountsModule`; as fábricas de teste e o host `E2E` (persistência fake) fornecem connection string fictícia só para satisfazer a opção obrigatória, sem conectar.
- REFACTOR: lista de usuários proibidos e prefixos de chave proibidos como dados; mensagens em códigos.
- Definition of done: DoD-P; cada `SSL Mode` coberto; nenhuma credencial real; a configuração do servidor (`ssl = on`, `hostssl`) é fase 8 e a conexão TLS real é validada por humano (H-6).

## T-137: Nenhuma transação aberta durante atraso, hash, Google, SMTP e processamento de imagem
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: DB9 e UA3 (`idle_in_transaction_session_timeout 15s`, `CONNECTION LIMIT 30`, pool de 20); mitigação "conexões presas por atraso" e "consultas longas, locks e conexões esgotadas"
- Blocked by: T-027, T-029, T-033, T-035, T-041, T-043, T-048, T-049, T-050, T-028
- Demo: Testes mostram que nenhuma transação fica aberta durante esperas e chamadas externas.
- RED: o design diz *o que* (a transação de `IUnitOfWork` só em volta da persistência) mas não *como verificar*; **proposta do planner, a confirmar na reaprovação**: sondas nos dublês. Em `Application.Tests`, `NoOpenTransactionTests`: `InMemoryUnitOfWork.IsTransactionOpen` (T-027) é lido por `RecordingDelayer`, `FakePasswordHasher` (hash e verificação), `FakeGoogleIdentityVerifier`, `RecordingPasswordResetNotifier`/enfileiramento e `FakeAvatarImageProcessor`; cada chamada **registra o estado e o teste falha se for verdadeiro**. Tabela de casos (caso de uso -> chamada que não pode ocorrer com transação aberta): `AccessThrottle` e atraso em `LoginWithPassword`, `RegisterWithPassword`, `RequestPasswordReset`, `LinkGoogleWithPassword`, `DeleteMyAccount` (atraso e `Verify` da senha antes de abrir a transação da exclusão); hash de senha em `RegisterWithPassword`, `ResetPassword`, `CompleteGoogleSignup` e rehash do login; verificador Google em `HandleGoogleCallback`; envio/enfileiramento de email em `RequestPasswordReset`; processador de imagem em `UploadCustomAvatar`. Também afirma que a transação **é** aberta em torno da persistência (o `IsTransactionOpen` do commit é verdadeiro) e que a exclusão continua atômica (purger e remoções na mesma transação, T-049).
- GREEN: reordenar os casos de uso para abrir a unidade de trabalho só no bloco de persistência (atraso, hash, Google, SMTP e imagem antes); sem mudar comportamento observável dos testes existentes.
- REFACTOR: operação `uow.ExecuteAsync(...)` pequena e reutilizada; comentário apontando "Resilience" do design. A prova do lado EF (`CurrentTransaction` nulo fora do bloco) está em T-063.
- Definition of done: DoD-P; todos os casos da tabela verdes e comprovadamente vermelhos movendo temporariamente o atraso para dentro do bloco.

## T-138: Job de limpeza - exclusão em lotes
- Status: done
- Covers: REQ-047
- Owner: implementer
- Blocked by: T-075, T-133
- Demo: Testes mostram o job de limpeza excluindo em lotes.
- RED: `CleanupJobBatchingTests` (SQLite + `FakeTimeProvider` + `UpdateStatementCapture` de T-133): com `Cleanup:BatchSize` pequeno no teste (ex.: 10) e 35 eventos de acesso > 24 h, o ciclo executa quatro `DELETE`s, cada um removendo no máximo o tamanho do lote, até restar zero velhos; os eventos recentes permanecem; vale para `access_attempt`, sessões expiradas/revogadas, tokens de redefinição e pendências expiradas; cada lote roda em transação curta própria (`CurrentTransaction` nulo entre lotes); idempotente (segunda execução não altera); cancelamento (`CancellationToken`) entre lotes interrompe sem erro e sem deixar transação aberta; duas instâncias simultâneas não falham; nenhum comando é um `DELETE` sem limite (varredura dos comandos capturados); `PurgeExpiredData` e o job continuam respeitando REQ-047 (retenção de 24 h do rate limit e exclusão por HMAC da conta ficam em T-049/T-060).
- GREEN: parâmetro de lote nos métodos de limpeza das portas (`DeleteOlderThan(cutoff, batchSize)`) e nos adapters EF de T-057 a T-060, laço no job em T-075; opção `Cleanup:BatchSize` (padrão 1000, valor de engenharia do planner, ajustável por configuração) com validação de faixa (> 0).
- REFACTOR: um laço de lote reutilizado pelos quatro tipos de dado; log só com contagens.
- Definition of done: DoD-P; o desempenho real e o tempo de cada lote (bem abaixo de `statement_timeout 5s`) são validados por humano (H-7).

## T-139: Evento de log 1021 `DatabaseCommandFailed` e métrica `deckloom.db.command_failures`
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: observabilidade de DB9/ADR-0015 (alertas de `57014`/`55P03`/`53300` e de `42501` = permissão faltando); catálogo passa de 20 para 21 eventos (T-093)
- Blocked by: T-079, T-076, T-092
- Demo: Testes mostram o evento de log 1021 e a métrica `deckloom.db.command_failures` em falha de comando.
- RED: `DatabaseFailureTests` em `Deckloom.Api.Tests` com exceções de teste portando `SqlState` (`PostgresException` de teste ou `DbException` com `SqlState` conhecido) lançadas por um fake de persistência: `42501`, `57014`, `55P03`, `25P03` e `53300` viram `500 internal_error` genérico (Problem Details sem `SqlState`, SQL nem mensagem do servidor), **sem retry**; emitem o evento `1021 DatabaseCommandFailed` com apenas `sqlState` e `operation` (nome do caso de uso ou job) e incrementam `deckloom.db.command_failures` com o atributo `sqlstate` (via `MeterListener`; nenhum outro atributo: sem email, IP, id, SQL); `LogCatalogTests` passa a exigir 1001-1021; um `SqlState` fora dessa lista cai no tratamento genérico de exceção não tratada existente (T-076), sem 1021; o job de limpeza com falha de banco registra 1021 com `operation` do job e continua no próximo ciclo; `LogRedactionTests` (T-093) cobre o 1021.
- GREEN: classificador `DatabaseFailureClassifier` (SqlState -> categoria), método `LoggerMessage` do 1021 em `AccountsLog`, medidor `deckloom.db.command_failures` e integração no middleware de exceções e no job.
- REFACTOR: lista de SQLSTATEs como dados; nomes de métrica em constantes (junto às de T-092).
- Definition of done: DoD-P; alertas de `57014`/`55P03`/`53300` documentados para a fase 8 junto com os de T-092 (roteamento fora de escopo).

## Rodada de correção pós-retro (2026-11-01)

Aprovada pelo usuário em 2026-11-01 (retro.md: itens 1 e 3 de "Carried forward" e a linha "Corrigir agora" da triagem dos Lows). Escopo: T-140 a T-158. Nenhum REQ, tarefa existente ou comportamento observável novo; as tarefas corrigem divergências fake x adapter, fecham Lows do `findings-ledger.md` (R-24, R-27, R-28, R-29, R-36, R-39, R-41, R-43, QA-04, QA-11, SEC-10, SEC-16, SEC-17) e acrescentam a guarda de composição real. Fora desta seção, por serem emendas de documentação do `architect`: SEC-19, a emenda do T-103/design para R-24 e a emenda do design para R-26.

Regras desta rodada:
- **Suítes de contrato (T-140 a T-146)**, conforme `docs/test-strategy.md` ("Fixtures and test doubles"): uma classe abstrata por porta de persistência, com os testes escritos como os casos de uso chamam a porta, executada uma vez contra o fake em memória (`Deckloom.Accounts.Testing`, projeto de teste de Application) e uma vez contra o adapter EF (SQLite em memória, `SqliteInMemoryFixture`, projeto de teste de Infrastructure). A classe abstrata mora ao lado do fake, em `Deckloom.Accounts.Testing`. Portas e pares: `IUnitOfWork` (`InMemoryUnitOfWork` x `EfUnitOfWork`), `IPlayerRepository` (`InMemoryPlayerRepository` x `EfPlayerRepository`), `ISessionRepository` (`InMemorySessionRepository` x `EfSessionRepository`), `IPendingGoogleAuthStore` (`InMemoryPendingGoogleAuthStore` x `EfPendingGoogleAuthStore`), `IAccessAttemptStore` (`InMemoryAccessAttemptStore` x `EfAccessAttemptStore`), `IAvatarPresetCatalog` (`InMemoryAvatarPresetCatalog` x `EfAvatarPresetCatalog`), `IFormatCatalog` (`InMemoryFormatCatalog` x `EfFormatCatalog`, em `Deckloom.DeckSuggestion.Infrastructure`) e `IAvatarStorage` (`InMemoryAvatarStorage` x `PostgresAvatarStorage`).
- **Prova do RED quando o comportamento já existe**: se o contrato passa nos dois lados na primeira execução (não há divergência), o RED é demonstrado por mutação temporária (revertida) no fake ou no adapter, descrita na tarefa, e registrada no PR; mesmo tratamento do T-052 e do T-124. Se o contrato falha em um dos lados, a falha real é o RED e a divergência é corrigida no GREEN (a regra do fake é a do caso de uso; em dúvida, o design decide).
- Nenhuma tarefa abaixo acessa o banco do sistema, `.env` ou credenciais; PostgreSQL real continua só na validação manual (H-1..H-10).

## T-140: Contrato de `IUnitOfWork` (fake x `EfUnitOfWork`) e base comum das suítes de contrato
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 1 (suíte de contrato por fake de persistência); regressão do defeito corrigido em `96e0919` (`EfUnitOfWork.ExecuteAsync` não gravava o que o bloco enfileirava); base reutilizada por T-141 a T-146
- Demo: A mesma suíte roda verde contra `InMemoryUnitOfWork` e `EfUnitOfWork`, inclusive `ExecuteAsync` sem `CommitAsync`.
- RED: `UnitOfWorkContract` (abstrata, em `Deckloom.Accounts.Testing`) com `InMemoryUnitOfWorkContractTests` (Application.Tests) e `EfUnitOfWorkContractTests` (Infrastructure.Tests, SQLite em memória): (a) `ExecuteAsync` cujo bloco só enfileira uma alteração (ex.: adicionar um Jogador) e **nunca chama `CommitAsync`** -> a alteração fica visível numa leitura posterior, em escopo novo; (b) `ExecuteAsync<T>` devolve o valor do bloco e grava igual; (c) exceção no bloco -> nada gravado e a exceção propaga; (d) `CancellationToken` cancelado antes do bloco -> nada gravado; (e) `IsTransactionOpen` é verdadeiro dentro do bloco e falso fora (T-137/T-063); (f) blocos aninhados ou duas execuções seguidas não vazam estado de uma para a outra; (g) participantes de commit (`ICommitParticipant`, usados por `IAvatarStorage`) são confirmados junto no `ExecuteAsync`. Como reproduzir o RED real: com `EfUnitOfWork.ExecuteAsync` sem o `SaveChanges`/commit ao fim do bloco (estado anterior a `96e0919`), o caso (a) falha apenas no lado EF por "alteração não encontrada"; o caso passa no fake. Registrar essa reprodução no PR (mutação temporária, revertida).
- GREEN: ajustar o que a suíte apontar (o esperado é nada em `EfUnitOfWork`, já corrigido); extrair o `AccountsContractFixture` mínimo (criação do sistema sob teste e de um segundo escopo de leitura) para `Deckloom.Accounts.Testing`, que as suítes T-141 a T-146 reutilizam; referências de projeto/xUnit necessárias ao `Deckloom.Accounts.Testing` (confirmar que a regra de arquitetura de T-002 permite).
- REFACTOR: nomes de teste `UoW_contract_*`; um único lugar para criar o escopo de leitura; sem duplicar os testes por lado.
- Definition of done: DoD-P; a suíte roda duas vezes (fake e EF) e o nome de cada teste aparece nas duas execuções; mutação do RED provada e revertida; sem banco real.

## T-141: Contrato de `IPlayerRepository` (fake x `EfPlayerRepository`)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 1 para a porta de Jogador (cadastro, login, redefinição, vínculo Google, exclusão: REQ-001, REQ-002, REQ-024, REQ-027, REQ-030)
- Blocked by: T-140
- Demo: A mesma suíte roda verde contra `InMemoryPlayerRepository` e `EfPlayerRepository`.
- RED: `PlayerRepositoryContract` (abstrata) com `InMemoryPlayerRepositoryContractTests` e `EfPlayerRepositoryContractTests`, sempre persistindo por `IUnitOfWork.ExecuteAsync` **sem `CommitAsync`** como os casos de uso fazem: adicionar e buscar por id, por email (normalizado, igualdade sem diferenciar caixa) e por identidade Google (provider+subject); email duplicado -> o mesmo desfecho observável nos dois lados (a corrida de REQ-002 é decidida pela unicidade); atualizar perfil, avatar, formatos preferidos (inclusive Formato inativo preservado, REQ-046) e ler de volta em escopo novo; hash de senha e token de redefinição (emitir, invalidar o anterior, consumir uma vez, `DeleteExpiredResetTokens` em lotes); vínculo Google; exclusão do Jogador remove tudo o que ele possui (formatos, identidade, sessões, avatar, pendência de vínculo) e a busca seguinte devolve vazio; leitura de Jogador inexistente devolve ausente, não exceção. Cada divergência encontrada entre fake e adapter é registrada no PR (se não houver, o RED é a mutação: no fake, trocar a comparação de email para diferenciar caixa -> o lado fake falha).
- GREEN: corrigir o lado que divergir do uso real pelos casos de uso (fake ou adapter) e acrescentar ao fake/adapter o que a suíte exigir.
- REFACTOR: dados de teste por construtores (`PlayerBuilder`) já existentes; nada específico de lado dentro da classe abstrata.
- Definition of done: DoD-P; todos os métodos públicos da porta aparecem em pelo menos um teste do contrato (conferência por lista no PR); mutação do RED provada.

## T-142: Contrato de `ISessionRepository` (fake x `EfSessionRepository`)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 1 para a porta de sessões (REQ-004, REQ-010, REQ-012, REQ-030, REQ-035)
- Blocked by: T-140
- Demo: A mesma suíte roda verde contra `InMemorySessionRepository` e `EfSessionRepository`.
- RED: `SessionRepositoryContract` (abstrata) com os dois executores, persistindo por `ExecuteAsync` sem `CommitAsync`: adicionar e buscar por hash do token (nunca pelo token em claro); `Touch` (janela deslizante) persiste `LastSeenAt`; revogar uma sessão e revogar todas as sessões de um Jogador (REQ-030) e ler de volta; sessão revogada ou expirada continua legível mas inválida; `DeleteExpiredOrRevoked(now, batchSize)` respeita a fronteira `expires_at <= agora` e o tamanho do lote, e remove a revogada antes de expirar (R-18); busca por token inexistente devolve ausente. Mesma regra de RED por mutação da T-141 (ex.: no fake, remover o filtro de revogadas em `DeleteExpiredOrRevoked`).
- GREEN: alinhar fake ou adapter ao que a suíte apontar.
- REFACTOR: relógio sempre por `FakeTimeProvider`; fábrica de sessão compartilhada.
- Definition of done: DoD-P; todos os métodos da porta cobertos; mutação do RED provada.

## T-143: Contrato de `IPendingGoogleAuthStore` (fake x `EfPendingGoogleAuthStore`)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 1 para a porta de pendências Google (REQ-006, REQ-008, REQ-036, REQ-039)
- Blocked by: T-140
- Demo: A mesma suíte roda verde contra `InMemoryPendingGoogleAuthStore` e `EfPendingGoogleAuthStore`.
- RED: `PendingGoogleAuthStoreContract` (abstrata) com os dois executores, via `ExecuteAsync` sem `CommitAsync`: salvar e ler a pendência por hash do token; **uso único** (consumir uma vez; a segunda tentativa, inclusive concorrente de duas instâncias de escopo, devolve ausente: a corrida de T-042); expiração em 10 min (fronteira exata) tratada como ausente; `DeleteExpiredAsync(now, batchSize)` respeita fronteira e lote; pendência de cadastro e pendência de vínculo distintas pelo tipo; leitura inexistente devolve ausente. Mesma regra de RED por mutação (ex.: no fake, não marcar a pendência como consumida).
- GREEN: alinhar fake ou adapter ao que a suíte apontar.
- REFACTOR: relógio por `FakeTimeProvider`; construtor de pendência compartilhado.
- Definition of done: DoD-P; todos os métodos da porta cobertos; mutação do RED provada.

## T-144: Contrato de `IAccessAttemptStore` (fake x `EfAccessAttemptStore`)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 1 para a porta de tentativas de acesso (atraso progressivo e orçamento por conta: REQ-032, REQ-041)
- Blocked by: T-140
- Demo: A mesma suíte roda verde contra `InMemoryAccessAttemptStore` e `EfAccessAttemptStore`.
- RED: `AccessAttemptStoreContract` (abstrata) com os dois executores. Atenção: o `EfAccessAttemptStore` grava imediatamente (`SaveChangesAsync`, fora do `IUnitOfWork`, SEC-01); o contrato fixa isso nos dois lados: o evento gravado fica visível a outro escopo **sem** `ExecuteAsync` e sem `CommitAsync`. Casos: gravar evento provisório e receber o id; contar eventos por chave dentro de janela (fronteira `AttemptedAt >= since`, incluindo o próprio evento); contagem por chave de IP e por chave de conta independentes; `DeleteByIdAsync` remove só o evento do id; `ReleaseAsync(keys)` remove o evento provisório da tentativa e não os de outras tentativas; `DeleteOlderThan(cutoff, batchSize)` respeita fronteira e lote (REQ-047); chaves só como HMAC (nenhum valor em claro nos dados lidos). Mesma regra de RED por mutação (ex.: no fake, contar `> since` em vez de `>= since`).
- GREEN: alinhar fake ou adapter ao que a suíte apontar.
- REFACTOR: relógio por `FakeTimeProvider`; helper de chaves HMAC de teste compartilhado.
- Definition of done: DoD-P; todos os métodos da porta cobertos; mutação do RED provada.

## T-145: Contrato dos catálogos `IAvatarPresetCatalog` e `IFormatCatalog` (fakes x adapters EF)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 1 para as portas de catálogo (REQ-017, REQ-018, REQ-022, REQ-037, REQ-046)
- Blocked by: T-140
- Demo: As suítes de catálogo rodam verdes contra `InMemoryAvatarPresetCatalog`/`InMemoryFormatCatalog` e contra `EfAvatarPresetCatalog`/`EfFormatCatalog`.
- RED: `AvatarPresetCatalogContract` e `FormatCatalogContract` (abstratas) com executores fake e EF (o de Formato em `Deckloom.DeckSuggestion.Infrastructure.Tests`, com a referência de projeto a `Deckloom.Accounts.Testing` se faltar e se a regra de T-002 permitir; do contrário, parar e reportar). Preset: listar ativos em ordem estável; verificar existência de um id; id desconhecido ou inativo é recusado; preset padrão existe e é ativo; o cache de 5 min (T-062) não devolve dado antigo depois do prazo (`FakeTimeProvider`) e não mascara a primeira leitura. Formato: listar ativos; obter por id; Formato inativo é reconhecido como inativo mas não some do catálogo de leitura por id (REQ-046); a porta é somente leitura (sem método de escrita). Mesma regra de RED por mutação (ex.: no fake de Formato, devolver inativos na listagem).
- GREEN: alinhar fake ou adapter ao que a suíte apontar.
- REFACTOR: semente de dados de teste única para os dois lados.
- Definition of done: DoD-P; todos os métodos das duas portas cobertos; mutação do RED provada.

## T-146: Contrato de `IAvatarStorage` (fake x `PostgresAvatarStorage`)
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 1 para a porta de armazenamento de avatar (REQ-019, REQ-021, REQ-024)
- Blocked by: T-140
- Demo: A mesma suíte roda verde contra `InMemoryAvatarStorage` e `PostgresAvatarStorage` (SQLite em memória).
- RED: `AvatarStorageContract` (abstrata) com os dois executores. `IAvatarStorage` é `ICommitParticipant`: o contrato exercita o armazenamento **dentro de `IUnitOfWork.ExecuteAsync`, sem `CommitAsync`**: salvar e ler os bytes de volta em escopo novo (igualdade byte a byte); substituir remove a imagem anterior (REQ-021); remover e ler devolve ausente; excluir o Jogador remove o avatar (REQ-024); tamanho acima de `MaxBytes` é recusado de forma igual nos dois lados; bloco que lança -> nada gravado e a imagem anterior permanece; leitura de Jogador sem avatar customizado devolve ausente. Mesma regra de RED por mutação (ex.: no fake, não remover a imagem anterior ao substituir).
- GREEN: alinhar fake ou adapter ao que a suíte apontar.
- REFACTOR: gerador de bytes WebP de teste compartilhado.
- Definition of done: DoD-P; todos os métodos da porta cobertos; mutação do RED provada.

## T-147: Composição real - todo `Set-Cookie` com prefixo `__Host-` tem `Secure`, `Path=/` e não tem `Domain`
- Status: done
- Covers: habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: retro item 3 e `docs/test-strategy.md` > "Real composition"; guarda de SEC-18 (cookie `__Host-dl_csrf` sem `Secure` quebrava todas as mutações no compose por http) e de SEC-04; REQ-001, REQ-004, REQ-006, REQ-010 ponta a ponta na composição de produção
- Demo: Um teste de API na composição real (adapters EF em SQLite, http puro) falha se qualquer cookie `__Host-` sair sem `Secure`, sem `Path=/` ou com `Domain`.
- RED: `HostPrefixedCookieTests` em `Deckloom.Api.Tests` com uma fábrica nova `RealCompositionWebApplicationFactory`: ambiente `Development`, `Auth:PublicOrigin` em http (como o compose local), **sem** o atalho `Request.Scheme = https` do host E2E e **sem** os fakes de persistência da fábrica de teste; os adapters EF reais de `AddAccountsModule` apontam para SQLite em memória (troca só do provedor do `AccountsDbContext`/`FormatCatalogDbContext`, com o esquema criado pelo teste e os catálogos semeados); só as fronteiras externas ficam trocadas por dublês (verificador Google, SMTP, relógio quando necessário); o `DatabaseRoleStartupCheck` não é registrado. Fluxos que emitem cookie: `GET /api/v1/csrf` (`__Host-dl_csrf`); cadastro e login, com e sem "lembrar-me" (`__Host-dl_session`); logout (cookie de sessão expirado/apagado); `GET /auth/google/start` (`__Host-dl_oauth`); callback com email novo (`__Host-dl_pending`) e o apagamento dos cookies Google. Para **cada** `Set-Cookie` cujo nome começa por `__Host-`, inclusive os que apagam o cookie: atributo `Secure` presente, `Path=/` e nenhum `Domain`; mais a afirmação de que ao menos um cookie de cada um dos quatro nomes foi observado (o teste não passa vazio). Além disso, uma mutação (POST de cadastro) completa com o token CSRF obtido do cookie devolve `2xx` e **não** `403 csrf_failed`. RED como guarda de regressão (o código de SEC-18 já está corrigido): provar a falha por mutação temporária e revertida, em `SecureCookieBuilder`/`SessionCookieService`/`GoogleCookieService`, removendo `Secure` de cada um dos quatro cookies, um por vez; e confirmar que a fábrica não consegue passar se `Request.Scheme` fosse forçado a https (o teste afirma `Request.IsHttps == false` num endpoint de sonda de teste).
- GREEN: a fábrica `RealCompositionWebApplicationFactory` (reutilizável pelos testes seguintes) e o auxiliar `HostCookieAssertions.AssertAllHostPrefixed(response)`; ajustes mínimos de composição que a troca de provedor exigir, sem mudar o comportamento de produção.
- REFACTOR: o auxiliar de asserção é compartilhado com `CsrfTests` e `SessionCookieAuthTests` quando já verificam atributos do mesmo cookie, sem duplicar; nenhum valor de cookie em mensagem de falha.
- Definition of done: DoD-P; o teste roda sem conexão a banco real; mutação por cookie provada e revertida; não depende do host E2E.

## T-148: `RegisterAsync` usa `ReadJsonAsync` e os endpoints JSON recusam `Content-Type` diferente de `application/json` (R-29, SEC-10)
- Status: done
- Covers: R-29, SEC-10; habilitadora (sem REQ direto); preserva REQ-001, REQ-003, REQ-004 e REQ-028
- Owner: implementer
- Habilita: defesa em profundidade do CSRF (ASVS V13.2.5) e remoção de leitura duplicada em `backend/src/Deckloom.Api/Auth/AuthEndpoints.cs`
- Demo: Testes de API mostram `415` para corpo JSON com `Content-Type` errado e `RegisterAsync` passando pelo mesmo leitor dos demais endpoints.
- RED: `JsonContentTypeTests` em `Deckloom.Api.Tests`: para cada endpoint que lê JSON (`register`, `login`, `password-reset/request`, `password-reset/confirm`, `google/complete-signup`, `google/link`, `me/profile` `PUT`, `me/avatar/preset` `PUT`, `me/deletion`), um corpo válido enviado como `text/plain`, `application/x-www-form-urlencoded` ou sem `Content-Type` devolve `415` Problem Details (RFC 9457) com código `unsupported_media_type`, sem chamar o caso de uso (nenhum evento de acesso, nenhuma conta criada); `application/json` e `application/json; charset=utf-8` continuam aceitos (parâmetros permitidos); POST/DELETE sem corpo (ex.: logout) não são checados e nunca devolvem 415; o `415` não consome orçamento do atraso progressivo. Para R-29: teste de que `register` com corpo acima do limite e com JSON malformado responde exatamente como os demais endpoints (mesmos códigos e Problem Details), e uma varredura estática em `RepoHygieneTests` que falha se `AuthEndpoints.cs` ainda contiver uma segunda leitura limitada do corpo fora de `ReadJsonAsync`. Hoje: os `text/plain` devolvem o resultado do caso de uso (200/201/400) em vez de `415`, e a varredura acha a leitura duplicada em `RegisterAsync`.
- RED (acréscimo, decidido pelo usuário em 2026-11-01): o media type é comparado **sem diferenciar maiúsculas** (`Application/JSON` e `APPLICATION/json; charset=utf-8` -> aceito); "com corpo" = `Content-Length > 0` **ou** `Transfer-Encoding: chunked`; casos novos: `Application/JSON` -> aceito; corpo chunked com `text/plain` -> `415`; corpo chunked com `application/json` -> aceito; POST sem corpo (sem `Content-Length` e sem chunked) -> nunca `415`.
- GREEN: `ReadJsonAsync` só verifica o `Content-Type` quando há corpo (`Content-Length > 0` ou `Transfer-Encoding: chunked`), compara o media type sem diferenciar maiúsculas e ignora parâmetros, e devolve `415` quando não é `application/json`; `RegisterAsync` usa `ReadJsonAsync<RegisterRequest>`; o código `unsupported_media_type` entra no mapeamento de Problem Details existente (T-076).
- REFACTOR: constante única para o tipo aceito; remover o código morto da leitura duplicada; `GET`/`DELETE` sem corpo e o upload `multipart` do avatar (T-089) não são afetados.
- Definition of done: DoD-P; todos os endpoints JSON da lista cobertos; os testes existentes de cadastro (T-082) continuam verdes sem alteração de asserção; decidido pelo usuário em 2026-11-01: `code = unsupported_media_type` (o design só listava `415` para avatar); a checagem vale só para requisições com corpo; `application/json` é aceito com parâmetros (ex.: `; charset=utf-8`); POST/DELETE sem corpo não são checados; sem mensagem nova no frontend. Casos obrigatórios do RED: `text/plain` -> 415; `application/json; charset=utf-8` -> aceito; `Application/JSON` -> aceito (media type sem diferenciar maiúsculas); chunked com `text/plain` -> 415; POST sem corpo -> não 415. "Com corpo" = `Content-Length > 0` ou `Transfer-Encoding: chunked` (decidido pelo usuário em 2026-11-01).

## T-149: `Forwarding:TrustedProxies` escalar derruba o start em vez de cair no padrão loopback (R-36)
- Status: done
- Covers: R-36; REQ-032, REQ-041
- Owner: implementer
- Demo: Teste mostra a API recusando iniciar quando a lista de proxies confiáveis vem como valor escalar sem índice.
- RED: em `ConcurrencyLimiterTests` (ou `TrustedProxyListTests`) de `Deckloom.Api.Tests`: com a configuração `Forwarding__TrustedProxies=172.28.10.0/24` (valor escalar, sem `__0`), `TrustedProxyList.Parse` lança a mesma `InvalidOperationException` de entrada inválida (mensagem só com o nome da chave, sem o valor) (decidido pelo usuário em 2026-11-01: **lança**, o escalar não é aceito como entrada), porque o comentário do código define "entrada inválida derruba o start" e o compose usa `__0`. Hoje o teste falha porque a seção sem filhos cai em silêncio no padrão `127.0.0.1`/`::1` (o proxy real seria ignorado e voltaria o efeito de R-14). Decidido pelo usuário em 2026-11-01: o escalar derruba o start **em qualquer fonte de configuração**, então o RED cobre as duas: (1) variável de ambiente sem índice (`Forwarding__TrustedProxies=...`) e (2) `appsettings.json` com `"Forwarding": { "TrustedProxies": "172.28.10.0/24" }` (string, não array); ambas lançam `InvalidOperationException` no start, com mensagem só com o nome da chave. Casos que continuam: sem a chave -> padrão loopback; lista indexada válida -> aceita; entrada vazia, inválida ou `/33` -> lança (já cobertos).
- GREEN: `TrustedProxyList.Parse` lê também `section.Value`: preenchido e sem filhos -> `InvalidOperationException`.
- REFACTOR: mensagens em constantes; um único ponto para a forma "seção com filhos".
- Definition of done: DoD-P; mensagem de falha sem o valor configurado; decidido pelo usuário em 2026-11-01: o valor escalar derruba o start com `InvalidOperationException` (não é aceito como uma entrada).

## T-150: Validador do `id_token` aceita as duas formas do emissor do Google (R-39)
- Status: done
- Covers: R-39; REQ-006, REQ-007, REQ-009
- Owner: implementer
- Demo: Teste mostra o login Google aceitando `iss` com e sem esquema e recusando qualquer outro emissor.
- RED: em `Deckloom.Accounts.Infrastructure.Tests` (testes do `GoogleIdTokenValidator` com o `StubGoogleOidcServer` de T-067). Decidido pelo usuário em 2026-11-01: o `iss` aceito é o `Auth:Google:Authority` **configurado** e o mesmo valor sem o esquema (não um literal fixo de `accounts.google.com`). Casos obrigatórios: `iss` igual ao `Authority` configurado (com esquema) -> aceito (já passa); `iss` igual ao `Authority` sem esquema (ex.: `accounts.google.com`, forma documentada pelo Google) -> aceito; `iss` de outro host -> recusado. Com `Authority` de teste diferente do Google (ex.: o host do stub), valem as mesmas duas formas derivadas dele. Demais negativos: `iss` de outro domínio, `http://accounts.google.com`, `https://accounts.google.com.evil.example` ou vazio -> recusado como falha de validação (classificada como `provider_error`, sem sessão e sem conta); `aud`, assinatura, `exp` e `nonce` seguem exigidos. Hoje o caso sem esquema falha porque o validador compara `iss == Authority`.
- GREEN: `GoogleIdTokenValidator` aceita exatamente as duas formas (conjunto de dois valores derivado do `Auth:Google:Authority` configurado: o valor e o mesmo sem esquema; comparação ordinal exata, nunca prefixo ou `Contains`).
- REFACTOR: conjunto de emissores válidos em uma constante com comentário apontando a documentação OIDC do Google; `GoogleOidcClient.cs:132` usa a mesma fonte.
- Definition of done: DoD-P; todos os negativos acima verdes; não verificado contra o Google real (validação manual).

## T-151: Motivo de log da reautenticação com sessão inválida deixa de ser `subject_mismatch` (R-41)
- Status: done
- Covers: R-41; REQ-042, REQ-026
- Owner: implementer
- Demo: Testes mostram o evento 1010 com o motivo certo quando a sessão da reautenticação está ausente, expirada ou é de outro Jogador.
- RED: em `HandleGoogleCallbackTests` (Application.Tests), com o coletor de log de T-079: na reautenticação (`intent=reauth`), (a) sessão do cookie ausente, (b) sessão expirada ou revogada e (c) `PlayerId` do cookie diferente do da sessão registram o evento 1010 com `reason=session_invalid` e não `subject_mismatch`, mesmo quando o callback em si é aceitável; (d) `sub` do Google diferente da identidade vinculada continua `subject_mismatch`; decidido pelo usuário em 2026-11-01: `session_invalid` vale **só** para sessão ausente/expirada/inválida e `PlayerId` do cookie diferente do da sessão; o passo 4 (Jogador inexistente, Jogador sem identidade Google, subject inválido) continua `subject_mismatch`, e o RED tem um caso para cada uma dessas três situações afirmando `reason=subject_mismatch` (não `session_invalid`); o desfecho para o usuário (`reautenticacao-falhou`, nada excluído, REQ-026) não muda; `IsAcceptable` é avaliado uma vez por callback (contagem num verificador falso). Hoje (a) a (c) registram `subject_mismatch` e a avaliação ocorre duas vezes.
- GREEN: motivo novo `session_invalid` no catálogo do evento 1010 (`GoogleAuthFailureLogNames`) e uso nos três ramos de `HandleGoogleCallback.cs`; avaliar `IsAcceptable` uma vez.
- REFACTOR: ramos de reautenticação num método pequeno com nome do motivo; `LogRedactionTests` (T-093) continua cobrindo o 1010 sem dado pessoal.
- Definition of done: DoD-P; depende da emenda do design.md (catálogo do evento 1010, `design.md:1662`, hoje sem `session_invalid`), a cargo do `architect`; o GREEN só começa depois dessa emenda, que o `architect` está fazendo em 2026-11-01; motivo novo `session_invalid` decidido pelo usuário em 2026-11-01 (já usado no domínio em `Session.MarkReauthenticated`).

## T-152: Exclusões compensatórias do `AccessThrottle` não dependem do cancelamento da requisição (SEC-16)
- Status: done
- Covers: SEC-16; REQ-032, REQ-041
- Owner: implementer
- Demo: Teste mostra o evento provisório removido mesmo quando o cliente fecha a conexão depois da gravação.
- RED: em `AccessThrottleTests` (Application.Tests): com o `InMemoryAccessAttemptStore` (que registra o token recebido em `DeleteByIdAsync` e `ReleaseAsync` e respeita cancelamento como o EF) e um `CancellationTokenSource` cancelado **depois** da gravação do evento provisório e **antes** da exclusão compensatória: (a) recusa por orçamento (`BudgetExceeded`) -> o próprio evento é apagado e a contagem volta ao valor anterior; (b) erro de validação do cadastro/login -> `ReleaseAsync` remove o evento; (c) vínculo Google concluído -> `ReleaseAsync` remove o evento; em todos, o token passado às exclusões **não** é o da requisição (`CancellationToken.None` ou um token próprio com prazo curto) e o `OperationCanceledException` da requisição ainda propaga ao chamador depois de limpar. Como reproduzir hoje: o fake lança `OperationCanceledException` na exclusão com o token cancelado, o evento fica e a contagem sobe (quebra da garantia E4 da ADR-0005).
- GREEN: `AccessThrottle` usa `CancellationToken.None` (ou token próprio de prazo curto, 5 s, decisão do implementer registrada no PR) nas exclusões compensatórias de `DeleteByIdAsync` e `ReleaseAsync`; os pontos de chamada dos casos de uso (`LoginWithPassword`, `RegisterWithPassword`, `LinkGoogleWithPassword`, `DeleteMyAccount`, `RequestPasswordReset`) não passam mais o `ct` da requisição a essas duas chamadas.
- REFACTOR: um único método `ReleaseProvisional(...)` que concentra a regra; comentário apontando SEC-16/ADR-0005 E4.
- Definition of done: DoD-P; os testes de SEC-01 (100 tentativas paralelas, orçamento de 30/h) seguem verdes; o contrato do store (T-144) cobre `DeleteByIdAsync`/`ReleaseAsync` com o token recebido.

## T-153: Compose de desenvolvimento publica `web` e Mailpit só em `127.0.0.1` (SEC-17)
- Status: done
- Covers: SEC-17; habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: mitigação de exposição do ambiente de dev (links de redefinição legíveis no Mailpit por outros hosts da rede)
- Demo: Teste de higiene mostra que nenhuma porta do compose é publicada fora do loopback.
- RED: em `RepoHygieneTests` (`Deckloom.ArchitectureTests`, junto de `T005_web_is_attached_only_to_edge_and_api_to_edge_and_default`): para cada entrada de `ports:` de qualquer serviço de `docker-compose.yml`, o IP de publicação é `127.0.0.1` (ou `[::1]`), nunca vazio nem `0.0.0.0`; o `web` publica `127.0.0.1:8081:80` (8081 por decisão do usuário em 2026-11-04) e o Mailpit `127.0.0.1:8025:8025`; a `api`, o `db` e a porta SMTP do Mailpit não publicam porta nenhuma. Hoje falha em `"8080:80"` e `"8025:8025"` (publicadas em todas as interfaces).
- GREEN: `docker-compose.yml` com os dois mapeamentos prefixados por `127.0.0.1:`; o resto do compose e o `ASPNETCORE_ENVIRONMENT=Development` ficam como estão (os validadores já recusam as exceções de dev fora de Development).
- REFACTOR: comentário curto no compose explicando o loopback; sem mudar redes nem volumes.
- Definition of done: DoD-P; nenhum contêiner é subido pelo agente; a conferência no navegador por `http://localhost:8081` e a inacessibilidade a partir de outro host ficam para o smoke manual no compose (retro item 2).

## T-154: Tipos compartilhados `Avatar`/`SessionSummary` em `shared/api` e teste do 401 `incorrect_password` no `apiClient` (R-24, R-28)
- Status: done
- Covers: R-24, R-28; REQ-011, REQ-026
- Owner: implementer
- Habilita: tipagem de `SessionProvider`/`SessionHeader` e cobertura unitária do `apiClient`, em `frontend/src/shared/api`
- Demo: O `apiClient` tem teste unitário do 401 de senha incorreta e o header deixa de usar cast manual do avatar.
- RED: (R-24) em `apiClient.test.ts`: `401` com `code = incorrect_password` **não** aciona `onSessionExpired` e rejeita com o erro de domínio para a tela (o S11 mantém o dialog); `401` sem esse código (e `401` sem corpo) aciona `onSessionExpired` uma vez (T-103); o teste fixa os dois lados e passa a ser a referência do comportamento em vez de o teste indireto de `DeleteAccountDialog.test.tsx`. O comportamento já existe, então o RED é por mutação temporária revertida: remover o ramo de `incorrect_password` faz o teste falhar. (R-28) teste de tipo (`expectTypeOf`) de que `SessionSummary["avatar"]` é `Avatar` (união `PresetAvatar | CustomAvatar` do design, `design.md:1550-1561`) e de que `SessionHeader` aceita o avatar da sessão sem cast; hoje falha no `tsc` porque `avatar` é `unknown`.
- GREEN: tipos `Avatar`, `SessionSummary` e `Profile` exportados de `shared/api`; `SessionProvider` e `SessionHeader` usam o tipo e o cast manual sai; o código de domínio `incorrect_password` vira constante nomeada no `apiClient` (sem mudar o comportamento). Pré-condição de leitura: a emenda do T-103/design para R-24 é do `architect` (em andamento); esta tarefa não edita o design.
- REFACTOR: nenhum `as` restante em `SessionHeader.tsx`; imports pelo barrel de `shared/api`.
- Definition of done: DoD-P; `npm --prefix frontend run build` (tsc) verde; sem mudança visual.

## T-155: Remover o código morto `App.tsx` e `App.test.tsx` (R-27)
- Status: done
- Covers: R-27; habilitadora (sem REQ direto)
- Owner: implementer
- Demo: O frontend monta só `AppRoutes` e não há mais componente raiz órfão.
- RED: teste de higiene `deadCode.test.ts` em `frontend/src/app/` (leitura estática dos arquivos): `frontend/src/app/App.tsx` e `App.test.tsx` não existem; `main.tsx` monta `AppRoutes`; nenhum módulo de produção importa `./App`. Hoje falha porque os dois arquivos existem (e `App.tsx` é o único `<main>` sem `id="conteudo"`, ver R-16).
- GREEN: apagar `App.tsx` e `App.test.tsx`; nenhum outro arquivo muda.
- REFACTOR: nenhum; conferir que a cobertura do frontend não cai (o limite continua 80).
- Definition of done: DoD-P; `npm --prefix frontend run build` e a suíte verdes.

## T-156: Aviso de reautenticação do S11 Google ligado à caixa por `aria-describedby` e inserido após a montagem (R-43)
- Status: done
- Covers: R-43; REQ-042, A11Y-31
- Owner: frontend
- Demo: No dialog de exclusão por Google, o leitor de tela anuncia por que o dialog reabriu, ao focar a caixa de confirmação.
- RED: em `DeleteAccountGoogleDialog.test.tsx`: com o retorno `reauth_expired` (aviso "Sua confirmação expirou..."), a caixa de confirmação tem `aria-describedby` que inclui o id do aviso (e continua incluindo o id do erro quando houver `boxErrorId`), e o texto do aviso é **inserido no DOM depois da montagem** do dialog (a região `role="alert"` existe vazia na montagem e recebe o texto num efeito, como pede o A11Y-31): `findByRole("alert")` com o texto, e um teste que lê o conteúdo da região logo após o primeiro render e a vê vazia. Hoje o aviso já está no DOM na montagem e a caixa só recebe `boxErrorId`.
- GREEN: `DeleteAccountGoogleDialog.tsx`: região do aviso com id estável, montada vazia e preenchida após a montagem; o id entra no `aria-describedby` da caixa (ou do dialog) junto com o do erro; sem mudança de texto nem de estilo.
- REFACTOR: reaproveitar o padrão de `FocusedAlert`/`Alert` do design system para a inserção tardia em vez de lógica própria; DoD-F (320, 768 e 1280px, 200% de texto) sem alteração visual.
- Definition of done: DoD-P (inclui DoD-F); não verificado com leitor de tela real (anotar no PR para o smoke de acessibilidade da fase 6 seguinte).

## T-157: Digitação por colar nos testes restantes com `user.type` (QA-04)
- Status: done
- Nota 2026-11-01 (decisão do usuário): concluída a troca por `fillField`/`user.paste` e o teste de higiene `noTypeInFormTests`. O critério "sem timeout nas rodadas simultâneas" NÃO fechou: em 8 execuções sob carga houve timeouts em testes que já não usam `user.type` (ex.: `cadastroFlow` boas-vindas, só paste). Queda de tempo só no `LoginPage` (~40%). O critério de carga passa para a T-158, com investigação de causa raiz (axe/MSW/render) antes da correção.
- Covers: QA-04; habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: estabilidade da suíte do frontend sob carga (R-21 aceito, `asyncUtilTimeout: 4000` mantido como tolerância documentada)
- Demo: Os arquivos de teste com digitação tecla a tecla passam sob 4 execuções simultâneas sem estourar o `testTimeout` de 5 s.
- RED: reproduzir sob carga, antes de mudar: 4 execuções simultâneas de `npm --prefix frontend run test -- --run --sequence.shuffle` (em shells separados; ou 3 vitest + 3 `dotnet test` simultâneos), registrando o tempo por teste dos arquivos com `user.type` que ainda restam: `ResetPasswordPage` (9), `ForgotPasswordPage` (5), `LoginPage` (4), `controls` (3), `GoogleSignupPage` e `AccountMenu.logout`. O RED é o tempo medido (cada `fillValid` com cerca de 45 teclas custa ~1,2 s sem carga e estoura 5 s sob carga) e o teste de higiene `noTypeInFormTests.test.ts` que falha enquanto esses arquivos usarem `user.type` em campo de formulário para preencher valores longos (permitido onde o teste verifica comportamento por tecla: máscara, contador, Enter, foco).
- GREEN: trocar `user.type` por `user.paste` (ou `user.clear` + `user.paste`) para preencher valores, mantendo `user.type`/`user.keyboard` onde a tecla é o objeto do teste (comentar o motivo no teste); nenhum prazo (`testTimeout`, `asyncUtilTimeout`) alterado; nenhuma asserção enfraquecida.
- REFACTOR: helper `fillField` de `shared/testing` usado pelos arquivos tratados (o mesmo já usado em `CadastroPage.test.tsx`); remover duplicação de `fillValid`.
- Definition of done: DoD-P; 4 rodadas de 3 a 4 execuções simultâneas sem falha por timeout; o tempo por teste dos arquivos tratados cai pela metade ou mais; `setupTests.ts` inalterado.

## T-158: Primeiro teste de `GoogleSignupPage` e demais arquivos frios não estouram 5 s sob saturação de CPU (QA-11)
- Status: done
- Nota 2026-11-01 (decisão do usuário): done no escopo da tarefa. 5 rodadas de 4 vitest simultâneos com `VITEST_MAX_WORKERS=2`: 0 timeouts, teste mais lento 2,3-3,0 s. Rodada mista (3 vitest + 3 `dotnet test`): 2 timeouts (5,27 s) e backend verde; fica como achado de QA. Falhas de asserção dependentes de ordem/tempo vistas sob carga (MOT-11 em `AccountMenu.logout`, Login "200 sem retorno", S11 `403 reauth_expired`) registradas como achados novos; a do S11 é investigada antes da fase 6 por suspeita de regressão da T-156.
- Covers: QA-11; habilitadora (sem REQ direto)
- Owner: implementer
- Blocked by: T-157
- Demo: A suíte do frontend passa em 3 execuções paralelas com 3 `dotnet test` ao mesmo tempo.
- RED: reproduzir sob carga: 3 `vitest --run` e 3 `dotnet test backend/Deckloom.sln` simultâneos (a falha original ocorreu em 1 de 3 execuções; repetir até 5 rodadas): `GoogleSignupPage.test.tsx > S4 Google - consulta da pendencia (REQ-039) > mostra h1 e estado de carregamento...` falha com `Test timed out in 5000ms` (render/import frio, `environment` ~48% do tempo, jsdom criado 37 vezes). O RED é a falha reproduzida e registrada (qual rodada, quanto tempo); se não reproduzir em 5 rodadas, parar e reportar em vez de supor a causa. O `AccountMenu.logout.test.tsx` (MOT-11) não foi reproduzido: ficou fora, sem causa raiz.
- RED (medido pelo agente `debug`, decidido pelo usuário em 2026-11-01): causa raiz = excesso de workers (4 suítes x 7 workers = 28 em 8 CPUs lógicas; os testes passam 60-70% do tempo esperando CPU). Reprodução: 4 `npx vitest --run --sequence.shuffle` simultâneos **sem** `VITEST_MAX_WORKERS`; já reproduzida em 3 rodadas (13, 1 e 3 timeouts). Descartados por medição: aquecer o primeiro render (5 timeouts; equivale a aumentar prazo), axe (7%), MSW e timers da aplicação.
- GREEN: decidido pelo usuário em 2026-11-01 (substitui "aquecer + maxWorkers só no CI"): (1) `vite.config.ts`: `maxWorkers: process.env.CI ? 2 : undefined` (prevenção; o CI roda 1 suíte em 4 vCPU), com comentário citando QA-11; o config não lê `VITEST_MAX_WORKERS`, pois o vitest já lê essa variável sozinho (confirmado pelo `debug` no código do vitest); (2) `docs/test-strategy.md`: regra de que quem roda suítes vitest em paralelo (ex.: verificação de carga, junto com `dotnet test`) define `VITEST_MAX_WORKERS=2`; (3) sem aquecimento de render, sem mudar `testTimeout`, `asyncUtilTimeout` nem `setupTests.ts`; a suíte local avulsa fica inalterada.
- REFACTOR: a configuração de workers fica isolada em `vite.config.ts` com comentário apontando QA-11; sem mudar o comportamento local.
- Definition of done: DoD-P; 5 rodadas de 4 suítes simultâneas com `VITEST_MAX_WORKERS=2` -> 0 timeouts e teste mais lento < ~3 s (fora o de 2 s proposital); idem 3 suítes + 3 `dotnet test` simultâneos; essas rodadas (sem `CI` definido) provam que `VITEST_MAX_WORKERS` vale com o config sem `CI`; prazos inalterados.

## T-159: A revogação de sessões na exclusão de conta (`AccountDeletion`) é afirmada por teste (R-44)
- Status: done
- Covers: R-44; REQ-024
- Owner: implementer
- Demo: Remover a chamada `sessions.RevokeAllFor(..., AccountDeletion, ...)` de `DeleteMyAccount` faz um teste falhar, nos dois ramos (senha e Google).
- RED: decidido pelo usuário em 2026-11-01: **manter** a revogação. Em `DeleteMyAccountPasswordTests` e `DeleteMyAccountGoogleTests` (Application.Tests), com um decorador de teste de `ISessionRepository` (em `Deckloom.Accounts.Testing`, sobre o `InMemorySessionRepository`) que registra cada `RevokeAllFor(playerId, reason, ...)` e se a chamada ocorreu dentro da unidade de trabalho: a exclusão confirmada registra exatamente uma chamada com o `PlayerId` excluído e `SessionRevocationReason.AccountDeletion`, antes do commit; exclusão recusada (senha incorreta, reautenticação ausente/expirada) não registra chamada; sessões de outros Jogadores seguem intactas. O RED é por mutação temporária revertida: remover a linha `DeleteMyAccount.cs:147` faz os testes novos falharem (hoje `REQ024_all_the_players_sessions_are_revoked_...` continua verde porque o `InMemorySessionRepository` apaga em cascata via `OnPlayerRemoved`). O teste não depende da cascata do fake.
- GREEN: nenhum código de produção muda (a chamada é mantida); só o decorador de teste e as asserções. Se a mutação não fizer o teste falhar, corrigir o teste, não o código.
- REFACTOR: `SessionIsValidAsync` dos dois arquivos deixa de tratar sessão ausente como inválida de forma silenciosa (`?? false`) onde o objetivo é provar a revogação; helper compartilhado entre os dois arquivos de teste.
- Definition of done: DoD-P; mutação (remover a chamada) demonstrada no PR e revertida; `InMemorySessionRepository` mantém a cascata (contrato T-142 inalterado).

## T-160: `AccessThrottle` - `ReleaseAsync` sem `ct` ignorado e `ClearAccountFailuresAsync` não depende do cancelamento da requisição (R-49, QA-18)
- Status: done
- Covers: R-49, QA-18; REQ-032, REQ-041
- Owner: implementer
- Blocked by: T-152
- Demo: Teste mostra as falhas da conta apagadas mesmo quando o cliente fecha a conexão logo após o login bem-sucedido, e a assinatura de `ReleaseAsync` não aceita mais um `ct` que seria ignorado.
- RED: (QA-18) em `AccessThrottleTests` (Application.Tests), com o `InMemoryAccessAttemptStore` que respeita cancelamento como o EF e registra o token recebido em `DeleteByAccountAsync`: com um `CancellationTokenSource` cancelado depois do sucesso do login (e da exclusão da conta Google/DeleteMyAccount, que também chama `ClearAccountFailuresAsync`) e antes da limpeza, as falhas da conta e o evento provisório são apagados, o token passado a `DeleteByAccountAsync` **não** é o da requisição e a contagem da conta volta a zero; o `OperationCanceledException` da requisição ainda propaga ao chamador depois de limpar. Hoje o fake lança na exclusão com o token cancelado e as falhas ficam (o Jogador legítimo herda o atraso). Nos casos de uso (`LoginWithPassword`, `DeleteMyAccount`), o teste de cancelamento tardio afirma o mesmo ponta a ponta. (R-49) teste de arquitetura/reflexão em `Deckloom.ArchitectureTests`: `AccessThrottle.ReleaseAsync` e `ClearAccountFailuresAsync` não têm parâmetro `CancellationToken`; hoje falha. O RED de QA-18 é por comportamento e o de R-49 é por assinatura.
- GREEN: `ClearAccountFailuresAsync` usa a mesma regra da T-152 (`CancellationToken.None` ou token próprio de 5 s, a mesma decisão do PR da T-152, para as duas ficarem iguais); remover o parâmetro `ct` de `ReleaseAsync` e `ClearAccountFailuresAsync` e atualizar os 8 pontos de chamada (`DeleteMyAccount.cs:57,68,145`; `LinkGoogleWithPassword.cs:54,61,73`; `LoginWithPassword.cs:60,88`; `RequestPasswordReset.cs:40`), sem mudança de comportamento fora do QA-18.
- REFACTOR: um único ponto (`ReleaseProvisional`/`CompensatingDelete`) concentra a regra de token das três exclusões compensatórias, com o comentário SEC-16/ADR-0005 E4; `innerCt` deixa de ser passado à limpeza onde sobrar.
- Definition of done: DoD-P; testes de SEC-01 (100 tentativas paralelas, orçamento de 30/h) e do contrato T-144 seguem verdes. Decisão pendente do QA-18 ("tratar como a T-152 ou aceitar") resolvida pelo usuário em 2026-11-01: corrigir.

## T-161: `InMemoryAvatarStorage` aplica sempre a pré-condição de dono da chave, como o `PostgresAvatarStorage` (R-45)
- Status: done
- Covers: R-45; habilitadora (sem REQ direto)
- Owner: implementer
- Habilita: fidelidade do fake de `IAvatarStorage` ao adapter EF (design.md:352), usado nos testes de `UploadCustomAvatar` e `DeleteMyAccount`
- Demo: Um teste de caso de uso que grava avatar de um Jogador não carregado no escopo falha no fake, como falharia no EF.
- RED: em `AvatarStorageContract` (T-146, roda contra fake e adapter EF): (a) gravar avatar para um Jogador confirmado que **não** foi carregado nem adicionado pelo repositório na mesma unidade de trabalho lança `InvalidOperationException` nos dois; (b) gravar para um Jogador lido ou adicionado no escopo funciona nos dois; (c) um `InMemoryAvatarStorage` criado numa UoW sem `InMemoryPlayerRepository` (fonte de Jogadores ausente) lança ao gravar, em vez de pular a checagem; (d) dois repositórios de Jogador criados na mesma UoW: o dono é a união dos lidos/adicionados por ambos (hoje `PlayersInScope` é sobrescrito pelo último). Hoje (a) e (c) passam no fake e falham no EF (contrato vermelho).
- GREEN: `InMemoryUnitOfWork`/`InMemoryPlayerRepository`/`InMemoryAvatarStorage`: o repositório registra no escopo da UoW os Jogadores que leu ou adicionou; o armazenamento consulta esse conjunto (nunca "todo Jogador confirmado"); sem fonte de Jogadores, lança ao gravar. Ajustar os testes de caso de uso que dependiam do comportamento frouxo (se algum falhar, o teste estava errado: ele grava avatar sem carregar o dono).
- REFACTOR: o conjunto de Jogadores do escopo vive na UoW (um só ponto), sem a propriedade `PlayersInScope` sobrescrevível.
- Definition of done: DoD-P; contrato T-146 verde contra fake e adapter; nenhum teste de caso de uso enfraquecido.

## T-162: Motivos do evento 1010 emitidos pela Application viram constantes da Application e a constante morta sai (R-47)
- Status: done
- Covers: R-47; REQ-042, REQ-026
- Owner: implementer
- Demo: `HandleGoogleCallback` não tem mais literal de motivo do evento 1010 e `GoogleAuthFailureLogNames` não tem constante sem uso.
- RED: (1) em `Deckloom.ArchitectureTests`/`RepoHygieneTests`: `HandleGoogleCallback.cs` não contém literais `"session_invalid"`, `"subject_mismatch"`, `"provider_error"` (usa constantes nomeadas da Application); `GoogleAuthFailureLogNames` não declara membro que nenhum projeto de produção referencie (hoje `SessionInvalid` falha); (2) teste do catálogo (T-151, `LogRedactionTests` T-093) continua afirmando que o catálogo do evento 1010 contém `session_invalid` e que os valores das constantes da Application são idênticos aos do catálogo da Infrastructure (uma fonte de verdade verificada por teste, já que a Application não referencia a Infrastructure).
- GREEN: classe `GoogleAuthFailureReasons` (ou nome equivalente) na Application com os motivos do 1010 emitidos por ela; `HandleGoogleCallback` usa as constantes; apagar `GoogleAuthFailureLogNames.SessionInvalid` ou deixá-la só se o catálogo da Infrastructure a referenciar; comentário da constante corrigido para não sugerir ligação inexistente.
- REFACTOR: nenhum motivo literal restante no arquivo; sem mudança de comportamento (testes de T-151 inalterados).
- Definition of done: DoD-P; sem mudança no `design.md` (o catálogo do 1010 já inclui `session_invalid` pela emenda do T-151).

## T-163: Falha de configuração de `Forwarding:TrustedProxies` não ecoa o valor configurado e usa mensagens constantes (R-48)
- Status: done
- Covers: R-48; REQ-032, REQ-041
- Owner: implementer
- Blocked by: T-149
- Demo: Teste mostra que a mensagem de start para entrada inválida indica a chave e o índice, nunca o valor.
- RED: em `ConcurrencyLimiterTests`/`TrustedProxyListTests` (Api.Tests): entrada indexada inválida (ex.: `"segredo-que-nao-pode-vazar"` em `Forwarding:TrustedProxies:1`) derruba o start com mensagem que contém `Forwarding:TrustedProxies:1` e **não** contém o valor; o mesmo para a forma escalar (já cobre) e para CIDR malformado; um teste de higiene lê `TrustedProxyList.cs` e exige que as mensagens de falha sejam constantes (`const string`/formato fixo), sem interpolar o valor da entrada. Hoje falha em `contem a entrada invalida '{entry}'` (`TrustedProxyList.cs:36-40`).
- GREEN: mensagens com a chave e o índice (`Forwarding:TrustedProxies:{i}`), sem o valor; as duas mensagens em constantes.
- REFACTOR: um só construtor de mensagem para os dois formatos (escalar e indexado).
- Definition of done: DoD-P; a regra "mensagem de falha sem o valor configurado" da T-149 vale para todos os ramos.

## T-164: Primitivo `announceAfterMount` no `Alert` e S11 Google o usa; descrição da caixa lista o erro novo antes do aviso (R-46, U-01)
- Status: done
- Nota (2026-11-01, decisão do usuário): o aviso do S11 Google passa a ter o visual de `Alert` de erro (classes `alert alert--error`, com ícone), exigido pelo teste do RED; desvio aceito do item "sem alteração visual" do DoD-F. Verificação visual fica com o `ui-reviewer` na revisão curta.
- Covers: R-46, U-01; REQ-042, A11Y-31
- Owner: frontend
- Blocked by: T-156
- Demo: O aviso de reautenticação do S11 continua anunciado após a montagem, sem mexer no DOM por fora do React, e ao enviar sem marcar a caixa o leitor de tela lê primeiro o erro novo.
- RED: (R-46) em `Alert.test.tsx` (design system): `Alert`/`FocusedAlert` com `announceAfterMount` renderizam a região (`role="alert"`, com `id` opcional repassado) **vazia** no primeiro render (`renderToString` não contém o texto) e com o texto depois do efeito (`findByRole("alert")` com o texto); sem a prop, o comportamento atual (texto na montagem) não muda; o texto é filho React (re-render com `children` novos atualiza o texto, o que a mutação direta de DOM não garante). Em `DeleteAccountGoogleDialog.test.tsx`: os testes da T-156 seguem verdes (inclusive `renderToString` vazio na montagem e descrição acessível) e um teste novo troca o aviso (`reauth_expired` -> `reauth_required`) sem remontar e vê o texto novo. (U-01) no mesmo arquivo: com o aviso presente e a caixa desmarcada no envio, `toHaveAccessibleDescription` **começa** pelo erro "Marque a caixa para confirmar..." e termina com o texto do aviso; sem erro, só o aviso; sem aviso, só o erro; ordem `[erro, aviso]`. Hoje falha: o `aria-describedby` está em `[aviso, erro]` (`DeleteAccountGoogleDialog.tsx:38-39`) e o texto é escrito por `noticeRef.current.textContent`.
- GREEN: `Alert` ganha `announceAfterMount?: boolean` e `id?: string` (o texto sobe por estado + efeito, `useState("")`/`useEffect`, conteúdo renderizado como filho React); `FocusedAlert` repassa as props; `DeleteAccountGoogleDialog.tsx` usa `<Alert variant="error" announceAfterMount id={noticeId}>` no lugar do `<p>` com `ref`/`textContent`, apaga o `useRef`/efeito próprios e monta `describedBy` como `[erro, aviso]`. Sem mudança de texto nem de estilo.
- REFACTOR: nenhuma lógica de inserção tardia fora de `Alert`; comentário citando A11Y-31; DoD-F (320, 768, 1280 px, 200% de texto) sem alteração visual, inclusive a entrada MOT-12 do dialog.
- Definition of done: DoD-P (inclui DoD-F); R-43 (T-156) continua atendido; não verificado com leitor de tela real (U-02, leitura dupla aceita pelo usuário, `role="alert"` mantido), anotar no PR para o smoke de acessibilidade. Sem emenda de `design.md`: A11Y-31 (`design.md:1058`) já pede o texto inserido após a montagem e o design.md:997 só pede `role="alert"` para o aviso.

## T-165: S2 (`/entrar?aviso=sessao-expirada`) insere o aviso após a montagem na carga completa da página (U-03)
- Status: done
- Covers: U-03; REQ-012, A11Y-31
- Owner: frontend
- Blocked by: T-164
- Demo: Ao abrir `/entrar?aviso=sessao-expirada` direto, a região `role="alert"` nasce vazia e recebe o texto depois; em transição de rota pela SPA o aviso recebe o foco como hoje.
- RED: em `LoginPage.test.tsx`: (a) carga completa (render inicial com `aviso=sessao-expirada`, `isTransition` falso): `renderToString` mostra a região `role="alert"` **sem** o texto e `findByRole("alert")` o mostra depois, sem foco programático (A11Y-23/34); (b) transição de rota pela SPA: o aviso recebe o foco e o texto está presente (foco não depende da inserção tardia); (c) o texto do aviso é o mesmo em (a) e (b); (d) os demais avisos do S2 (`FocusedAlert` de erro de login etc.) não mudam. Hoje (a) falha: `LoginPage.tsx:166-169` renderiza `<FocusedAlert variant="error" focus={isTransition}>` com o texto no primeiro render.
- GREEN: `LoginPage.tsx` passa `announceAfterMount={!isTransition}` ao `FocusedAlert` do aviso de sessão expirada (na transição, o foco no aviso exige o texto presente; na carga completa, o texto vem depois da montagem, A11Y-31); sem mudança de texto, de estilo ou de ordem de foco.
- REFACTOR: nenhuma duplicação com o S11; DoD-F (320, 768, 1280 px, 200% de texto) sem alteração visual.
- Definition of done: DoD-P (inclui DoD-F); não verificado com leitor de tela real (anotar para o smoke); `design.md` sem emenda.

## T-166: `InMemoryUnitOfWork` observa o `ct` no commit e desfaz o rastreamento de Jogadores no rollback e na remoção, como o `EfUnitOfWork` (R-50, R-51)
- Status: done
- Covers: R-50, R-51, QA-18; REQ-032, REQ-041
- Owner: implementer
- Blocked by: T-160, T-161
- Habilita: fidelidade do fake de `IUnitOfWork` ao `EfUnitOfWork` (`CommitAsync(ct)`/`ChangeTracker.Clear()`); decisão do usuário de 2026-11-01 (revisão curta da fase 6)
- Demo: Com a requisição cancelada antes do commit, `LoginWithPassword`/`DeleteMyAccount` lançam `OperationCanceledException` e nada fica gravado (nem sessão, nem exclusão da conta, nem limpeza das falhas), no fake como no EF; gravar avatar depois de um bloco revertido falha nos dois.
- RED: (R-50) reescrever os 3 testes de `AccountFailuresLateCancellationTests.cs` (login, exclusão com senha, exclusão Google) para afirmar o comportamento real: com o `ct` da requisição cancelado depois do trabalho e antes do commit, `OperationCanceledException` propaga ao chamador; rollback total (nenhuma sessão criada, conta e Jogador intactos, falhas da conta e evento provisório **mantidos**, nenhuma revogação/exclusão aplicada); o token recebido por `store.DeleteByAccountAsync` continua independente do da requisição (QA-18/T-160); sem `Assert.True(result.IsSuccess)` nem o comentário que declara a lacuna. No contrato `UnitOfWorkContract` (T-140, roda contra fake e `EfUnitOfWork`): `CommitAsync(ct)` e `ExecuteAsync` com `ct` cancelado antes da aplicação lançam `OperationCanceledException` e não gravam nada nos dois. (R-51) no `AvatarStorageContract` (T-146, fake e EF): (a) gravar avatar depois de um `ExecuteAsync` que lançou, com o dono lido só dentro do bloco revertido, lança `InvalidOperationException` nos dois; (b) gravar avatar de um Jogador cuja remoção já foi confirmada na mesma UoW lança nos dois; (c) o Jogador lido de novo depois do rollback volta a ser dono e grava. Hoje falham no fake: o commit ignora o `ct` e `_trackedPlayers` nunca é limpo.
- GREEN: `InMemoryUnitOfWork.Commit(ct)` chama `ct.ThrowIfCancellationRequested()` antes de aplicar (com `Discard()` e sem efeito parcial, como o `transaction.CommitAsync(ct)` do EF); `CommitAsync(ct)` e `ExecuteAsync` repassam o token. `Discard()` limpa `_trackedPlayers` (equivalente ao `ChangeTracker.Clear()` do `EfUnitOfWork.cs:96-97`); `NotifyPlayerRemoved` retira dos rastreados o Jogador removido (o EF desanexa a entidade removida após o `SaveChanges`; os demais continuam rastreados, pois o `SaveChanges` bem-sucedido não limpa o tracker). `_hasPlayerSource` não é afetado. Só código de `Deckloom.Accounts.Testing`; nenhum código de produção muda.
- REFACTOR: um só ponto de rastreamento/limpeza dentro da UoW; comentários do fake e do teste apontam R-50/R-51 e `EfUnitOfWork.cs`; remover do teste QA-18 o código que existia só para contornar o fake.
- Definition of done: DoD-P; contratos T-140, T-144 e T-146 verdes contra fake e EF. **Risco declarado (a listar no PR com os resultados reais):** testes que cancelam o `ct` e passam hoje por o fake ignorá-lo no commit podem quebrar; verificar antes pelo menos `AccessThrottleTests`, `AccessThrottleCompensationTests`, `HandleGoogleCallbackExistingTests`, `GoogleReauthTests`, `TransactionProbeTests` e `UnitOfWorkContract` (os que usam `CancellationTokenSource`/`Cancel` com a UoW). Cada teste que quebrar: se ele esperava sucesso com `ct` cancelado antes do commit, o teste estava errado e é corrigido para a semântica real (nunca se enfraquece a asserção nem se relaxa o fake); se cancela só dentro do trabalho (antes do commit), reavaliar a expectativa e registrar na tarefa. Testes de caso de uso que gravem avatar sem reler o dono depois de um rollback (R-51) entram no mesmo risco. Sem emenda de `design.md`: design.md:352 já diz que a pré-condição "vale igual no fake e no `PostgresAvatarStorage`".

## T-167: Alert `announceAfterMount` insere o texto depois da pintura e o foco da caixa do S11 Google espera o texto na região descrita (U-06, U-04)
- Status: done
- Covers: U-06, U-04; REQ-042, A11Y-31
- Owner: frontend
- Blocked by: T-164, T-165
- Demo: Ao abrir o S11 Google com aviso, a caixa só recebe foco quando a região apontada por `aria-describedby` já contém o texto; abrir o aviso a partir de um clique não entrega região vazia e texto juntos à árvore de acessibilidade.
- RED: (U-06) em `Alert.test.tsx`: com `announceAfterMount` e `requestAnimationFrame` controlado (`vi.stubGlobal`/fake timers), a região `role="alert"` existe **vazia** depois do primeiro commit e continua vazia até o frame ser disparado; só então recebe o texto; desmontar antes do frame chama `cancelAnimationFrame` com o id e nunca atualiza o estado (sem aviso de `act`/estado em componente desmontado); o primeiro render segue vazio no `renderToString`; a montagem a partir de um evento discreto (`userEvent.click` que exibe o `Alert`) também passa pela fase vazia (hoje o texto chega no mesmo flush síncrono e o teste falha); `rerender` com `children` novos atualiza o texto; sem a prop, o texto está presente na montagem. Teste de higiene (`eslint` ou leitura do fonte): `Alert.tsx` não tem `eslint-disable`. (U-04) `Alert`/`FocusedAlert` com `announceAfterMount` aceitam `onAnnounced?: () => void`, chamada uma vez, depois de o texto estar no DOM. Em `DeleteAccountGoogleDialog.test.tsx`: um listener de `focus` na caixa captura, no instante do evento, `aria-describedby` e o `textContent` das regiões que ele referencia; com aviso presente, a captura contém o texto do aviso (e, havendo erro, o do erro antes dele); o foco na caixa não ocorre antes de `onAnnounced`; sem aviso a caixa recebe foco sem esperar; trocar o aviso (`reauth_expired` -> `reauth_required`) sem remontar não rouba nem repete o foco indevidamente. Hoje falha: o efeito de foco do `GoogleBody` roda antes de o `setMounted(true)` do `Alert` ser aplicado, e a captura vê a região vazia.
- GREEN: `Alert.tsx`: `useEffect` agenda `requestAnimationFrame` que liga o estado `mounted`, com `cancelAnimationFrame` no cleanup, e remove o `eslint-disable`; um efeito seguinte (dependente de `mounted`) chama `onAnnounced`. `FocusedAlert` repassa `onAnnounced`. `DeleteAccountGoogleDialog.tsx`: o foco da caixa fica num efeito que depende de "não há aviso ou o aviso foi anunciado" (estado `noticeReady` posto por `onAnnounced` e zerado quando o aviso muda), de modo que o foco ocorre depois de o texto estar na região; sem mudança de texto, de estilo nem de ordem de `aria-describedby` (`[erro, aviso]`, T-164).
- REFACTOR: nenhuma lógica de inserção tardia fora de `Alert`; o contrato (`onAnnounced`) documentado no tipo da prop com A11Y-31; reaproveitar o helper de teste de frame entre `Alert.test.tsx` e `DeleteAccountGoogleDialog.test.tsx`; DoD-F (320, 768, 1280 px, 200% de texto) sem alteração visual além do que a T-168 corrige.
- Definition of done: DoD-P (inclui DoD-F); R-43 (T-156), R-46/U-01 (T-164) e U-03 (T-165) seguem atendidos. **Risco declarado:** a inserção passa a chegar após um frame, então testes de `Alert`, `LoginPage`, `RouteFocus` e `DeleteAccountGoogleDialog` que leem a região de forma síncrona logo após `render` (`getByRole("alert")` com texto, sem `findBy`/`waitFor`) podem quebrar; listar no PR e trocar por `findBy` sem enfraquecer a asserção; o foco do aviso na transição de rota do S2 (T-165 (b)) usa `announceAfterMount={false}` e não muda. Não verificado com leitor de tela real (anotar para o smoke de acessibilidade, junto com U-02).
- Nota 2026-11-04 (R-55; decisões do usuário na implementação, prevalecem sobre o RED e o GREEN acima): sem aviso, o foco da caixa segue o padrão do `Dialog`, sem efeito próprio; ao trocar o aviso sem remontar, a caixa recebe foco de novo, com o texto novo já no DOM; o estado é `announced` e não é zerado na troca de aviso, só quando o aviso deixa de existir (o nome `noticeReady` do GREEN não foi usado).

## T-168: Texto do `Alert` quebra palavras longas a 320 px com texto a 200%, sem rolagem horizontal no dialog (U-05)
- Status: done
- Covers: U-05; REQ-042, A11Y-31
- Owner: frontend
- Blocked by: T-164
- Demo: No S11 Google a 320 px com texto a 200%, "Reautentique" e "confirmação" quebram dentro da coluna do aviso e o `.dialog__panel` não ganha rolagem horizontal.
- RED: em `Alert.test.tsx`: o `<div>` do texto do `Alert` tem a classe `alert__text` (em todas as variantes, com e sem `announceAfterMount`, ícone mantido); teste de CSS (leitura de `Alert.css`, no padrão usado pelos testes de CSS do design system) exige para `.alert__text` `min-width: 0` e `overflow-wrap: anywhere`. Hoje a classe não existe. A conferência visual real fica no E2E/`ui-reviewer`: acrescentar à T-127 (ou à fixture `?data=worst` de A.8.5) a verificação de `scrollWidth <= clientWidth` do `.dialog__panel` a 320 px com `font-size` do documento a 200% no S11 Google com aviso; se a fixture ainda não existir, o teste do `Alert` acima é o RED e a medição entra como passo manual no DoD.
- GREEN: `Alert.tsx` põe `className="alert__text"` no `<div>` do texto; `Alert.css` declara `.alert__text { min-width: 0; overflow-wrap: anywhere; }`. Sem outra mudança visual (cores, espaçamento e ícone inalterados).
- REFACTOR: nenhuma regra duplicada com `.delete-dialog__message` (`DeleteAccountDialog.css:8-13`); avaliar se a classe antiga ainda tem uso e removê-la se não tiver.
- Definition of done: DoD-P (inclui DoD-F): conferido a 320 px com texto a 200% no S11 Google (aviso e erro da caixa) e no aviso do S2, sem rolagem horizontal; 768 e 1280 px sem diferença visual. Pode ser feita em paralelo à T-166; se a T-167 for feita antes, nenhum conflito (arquivos distintos além de `Alert.tsx`: mesclar a classe no mesmo `<div>`).

## T-169: `announceAfterMount` que passa de `true` para `false` antes do frame mostra o texto, e testes do foco do S11 Google travam a troca e o reset do aviso (R-52, R-53)
- Status: done
- Covers: R-52, R-53; REQ-012, REQ-042, A11Y-31
- Owner: frontend
- Blocked by: T-167
- Habilita: recomendação da revisão rápida da fase 6, aceita pelo usuário em 2026-11-04
- Demo: Se a tela deixa de pedir a inserção tardia antes de o frame disparar, o aviso aparece em vez de sumir; a caixa do S11 Google recebe foco com o texto do aviso novo no DOM depois de uma troca e depois de aviso -> sem aviso -> aviso.
- RED: (R-52) em `Alert.test.tsx`, com `stubControlledFrames`: `rerender` de `announceAfterMount` `true` para `false` antes do `flush` mostra o texto; hoje falha, porque o cleanup cancela o frame, o novo efeito sai cedo e o render continua `null`. (R-53a) em `DeleteAccountGoogleDialog.test.tsx`, o teste da troca de aviso passa a exigir `expect(captures).toHaveLength(2)` e `captures[1].text` com `REQUIRED` e sem `EXPIRED`, sem `if`; provar que falha tirando temporariamente `notice` das dependências do efeito de foco. (R-53b) teste novo `EXPIRED` -> sem aviso -> `REQUIRED`: a captura do foco tem o texto; provar que falha tirando temporariamente o reset `if (!notice && announced) setAnnounced(false)`.
- GREEN: `Alert.tsx` renderiza `{!announceAfterMount || mounted ? children : null}`. Nenhuma outra mudança de produção: R-53 só acrescenta teste.
- REFACTOR: nenhum, além de reaproveitar os helpers de frame e de captura de foco já existentes.
- Definition of done: DoD-P; T-164, T-165 e T-167 seguem atendidas; sem mudança de texto, de estilo nem de `aria-describedby`.

## T-170: `<title>` próprio em `/perfil`, `/perfil/editar` e nas telas do fluxo Google (U-07)
- Status: done
- Covers: U-07; A11Y-20, A11Y-34
- Owner: frontend
- Blocked by: T-128
- Habilita: achado do smoke da fase 6 (2026-11-04)
- Demo: Depois de entrar, a aba mostra "Meu perfil - Deckloom", e não "Entrar - Deckloom"; em `/perfil/editar` mostra "Editar perfil - Deckloom"; a carga direta dessas rotas também tem o título da tela.
- RED: nos testes de `routeTitles` e de `RouteFocus`: (a) `titleForPath` devolve `<h1> - Deckloom` para `/perfil`, `/perfil/editar` e para as rotas do fluxo Google (`GOOGLE_RETURN_ROUTE`, `GOOGLE_COMPLETE_ROUTE`, `GOOGLE_LINK_ROUTE`), com o texto exato do `<h1>` de cada tela; (b) transição de `/entrar` para `/perfil` muda `document.title`; (c) toda rota de `ROUTE_PATTERNS` tem título, para uma rota nova não repetir a lacuna. Hoje falha: `ROUTE_HEADINGS` só tem as quatro rotas de visitante e `RouteTitle` mantém o título anterior quando `titleForPath` devolve `null`.
- GREEN: acrescentar as rotas a `ROUTE_HEADINGS`, com o texto do `<h1>` que cada tela já renderiza; sem mudança de texto nas telas.
- REFACTOR: atualizar o comentário de `routeTitles.ts`, que ainda diz que o perfil não tem tela própria.
- Definition of done: DoD-P; conferido no navegador no smoke da fase 6.

## T-171: Cabeçalho de visitante reflui a 320 px com texto a 200%, sem rolagem horizontal (U-09)
- Status: done
- Covers: U-09; A11Y-18, A11Y-19, UX-10
- Owner: frontend
- Blocked by: T-168
- Habilita: achado do E2E da fase 6 (2026-11-04)
- Demo: Em `/entrar` a 320 px com texto a 200%, "Entrar" e "Criar conta" ficam dentro da largura da tela e a página não rola na horizontal.
- RED: já existe: `account-deletion.spec.ts`, teste do S2 a 320 px com texto a 200% (`scrollWidth` 440 > `clientWidth` 320). Acrescentar teste de CSS no padrão do design system para as regras de quebra de `.app-header` e `.app-header__actions`.
- GREEN: `AppHeader.css`: permitir quebra de linha em `.app-header` e em `.app-header__actions`, sem mudar o layout a 768 e 1280 px com texto a 100%.
- REFACTOR: nenhum.
- Definition of done: DoD-P (inclui DoD-F); também sem estouro com sessão ativa (menu da conta) a 320 px com texto a 200%.

## T-172: `overlaps()` do E2E mede as linhas de links inline, sem falso positivo em link quebrado (QA-19)
- Status: done
- Covers: QA-19; A11Y-19, UX-10
- Owner: frontend
- Blocked by: T-127
- Habilita: falhas de S1 e S4 no E2E da fase 6 (2026-11-04)
- Demo: A varredura de acessibilidade de S1 e S4 passa em 320 px, 400%, e texto a 200%, e continua falhando quando dois controles se sobrepõem de verdade.
- RED: já existe: `accessibility.spec.ts:619` e `:657` em S1 e S4, par "Termos de Uso" x "Política de Privacidade". Antes de mudar a heurística, medir com `getClientRects` se as linhas dos dois links se cruzam; se cruzarem, o defeito é da tela, não do teste: parar e relatar.
- GREEN: `overlaps()` compara as caixas de linha (`getClientRects`) de cada alvo em vez da caixa envolvente.
- REFACTOR: nenhum.
- Definition of done: prova de dente: sobreposição real plantada temporariamente (dois controles sobrepostos e dois links inline sobrepostos) faz `overlaps()` acusar; revertida depois. Só código de teste muda.

## T-173: `Button` com `stableWidth` não estoura a 320 px com texto a 200%, e a varredura cobre essa combinação em todas as telas (U-10)
- Status: done
- Covers: U-10; A11Y-19, UX-10
- Owner: frontend
- Blocked by: T-171
- Habilita: achado da T-171 (2026-11-04)
- Demo: Em `/cadastro` a 320 px com texto a 200%, o botão "Criar conta" fica dentro da largura da tela e a página não rola na horizontal.
- RED: E2E a 320 px com texto a 200% em todas as telas alcançáveis (S1, S2, S4, S5, S6, S7, S8, S9): documento com `scrollWidth <= clientWidth`. Em `/cadastro` falha hoje (`scrollWidth` 384 > 320, botão de envio). O teste de cabeçalho de `/cadastro` da T-171 passa a medir o documento inteiro.
- GREEN: `stableWidth` do `Button` limitado à largura disponível, sem mudar a largura estável nos demais casos nem o tamanho a 100%.
- REFACTOR: remover o comentário da T-171 que explica a exceção de `/cadastro`.
- Definition of done: DoD-P (inclui DoD-F). Outra tela que estourar entra na mesma tarefa, se a causa for a mesma; se for outra, é relatada.

## T-174: Email de S4 e S5 e títulos quebram a 320 px com texto a 200% e espaçamento aumentado (U-11)
- Status: done
- Covers: U-11; A11Y-19, UX-10, WC-01
- Owner: frontend
- Blocked by: T-173
- Habilita: achado da T-173 (2026-11-04)
- Demo: S4, S5 e S6 a 320 px com texto a 200% e espaçamento aumentado não rolam na horizontal; o email longo e o título "Redefinir senha" quebram dentro do painel.
- RED: já existe: bloco "320px com texto a 200%" de `accessibility.spec.ts`, S4 e S5 (`scrollWidth` 627) e S6 (375). Falta confirmar por medida o elemento que estoura no S5.
- GREEN: `overflow-wrap: anywhere` no email somente leitura de S4 e S5 (`design.md:1180`) e no `<h1>` das telas, sem mudar o layout a 100%.
- REFACTOR: uma só regra para o `<h1>`, sem repetir por tela.
- Definition of done: DoD-P (inclui DoD-F); bloco inteiro verde em todas as telas, sem `fixme`.

## T-175: Campo de senha, `<h1>` a 24 px, título por estado e varredura de corte e sobreposição a 320 px com texto a 200% (U-12, U-13, U-08, U-14, U-15, U-17, R-58, R-62)
- Status: done
- Covers: U-12, U-13, U-08, U-14, U-15, U-17, R-58, R-62; A11Y-19, A11Y-20, UX-10, WC-06
- Owner: frontend
- Blocked by: T-174
- Habilita: revisão de UI e revisão final da fase 6; decisão do usuário de 2026-11-04 de corrigir todos
- Demo: Em `/entrar` a 320 px com texto a 200%, o botão "Mostrar senha" desce de linha e a senha digitada é legível; os `<h1>` têm 24 px calculados a 100%; o link inválido em `/redefinir-senha` tem o título "Link inválido ou expirado - Deckloom".
- RED: E2E a 320 px com texto a 200%: (U-12) área de texto do input de senha com largura mínima útil em S1, S2, S5, S7 e no S11 com senha; (R-58) `overlaps()` e `clipped()` no bloco de U-10, que também passa a cobrir S3, S7 com link inválido, S10 e S11, com medida do painel nos dois diálogos; (U-13) `<h1>` de cada tela com 24 px calculados; (U-08 e T-170) `document.title` igual a `<h1> - Deckloom` em toda tela, inclusive link inválido; (U-14) painel do menu com margens iguais. U-15 e U-17: conferir no render.
- GREEN: `TextField.css` com quebra no controle e base de largura no input, mantendo linha única a 100%; regra base do `<h1>` com `--text-2xl`, mantendo `overflow-wrap: anywhere`; `InvalidLink` define o título ao montar, inclusive depois de `reset_link_invalid`; ajustes de `AccountMenu.css`, `AppHeader.css` e `ProfilePage.css` dos achados. (R-62) título do teste de `pageShell.test.ts:95` diz o que ele verifica.
- REFACTOR: nenhuma lógica de título por estado fora de `ResetPasswordPage`.
- Definition of done: DoD-P (inclui DoD-F); sem mudança a 768 e 1280 px com texto a 100%, exceto o tamanho do `<h1>`. Quebra no meio de palavra em S3 e S6 a 320 px só com 200% e espaçamento juntos é aceita (recomendação do `ui-reviewer`).

## T-176: Ajustes de revisão em testes e notas de release (R-59, R-60, R-61)
- Status: done
- Covers: R-59, R-60, R-61
- Owner: implementer
- Blocked by: T-153
- Habilita: revisão final da fase 6; decisão do usuário de 2026-11-04 de corrigir todos
- Demo: A suíte não reprova por `docker-compose.override.yml` local; `release-notes.md` traz as portas certas.
- RED: não se aplica: só teste e documentação, sem comportamento de produção.
- GREEN: (R-59) comentário de `CompositionTests.cs:147` aponta QA-17; (R-60) `RepoHygieneTests`: tirar `Assert.True(name.Length > 0)`, nome do teste cita `privileged` e `pid`, e `docker-compose.override.yml`, que é local, deixa de reprovar; (R-61) `release-notes.md:116` com `8081` e `8025` em loopback.
- REFACTOR: nenhum.
- Definition of done: suítes verdes; `RepoHygieneTests` ainda falha com porta fora do loopback.

## T-177: `clipped()` só dispensa campos de texto e o upload não corta a 320 px com texto a 200% (R-63, R-66)
- Status: done
- Covers: R-63, R-66; A11Y-19, UX-10
- Owner: frontend
- Blocked by: T-175
- Habilita: revisão final da fase 6 (2026-11-04)
- Demo: A 320 px com texto a 200%, o controle de arquivo do S10 cabe no painel, e a varredura acusa um `input type="file"` cortado.
- RED: `clipped()` do bloco de 320 px deixa de dispensar `input type="file"` e passa a reprovar `input.file-upload__input` (242 contra 240).
- GREEN: filtro só para tipos de texto (`design.md:1179`); CSS do upload contido na largura do painel, sem mudar o layout a 100%. (R-66) título do teste de U-12 diz o que mede, ou o teste passa a medir o botão; espera fixa do teste de U-14 trocada por espera de estado; regex de `flex` em `pageShell.test.ts` exata; `NATIVELY_INTERACTIVE` cobre papéis de widget com tabindex móvel.
- REFACTOR: nenhum.
- Definition of done: DoD-P (inclui DoD-F); prova de dente com `input type="file"` cortado.
- Nota 2026-11-04: o CSS do upload não foi alterado, porque o texto interno de `input type="file"` não quebra nem rola. A detecção ficou no teste, com exceção estreita para a medida exata do S10; a decisão de aceitar ou trocar o controle está em R-63.

## T-178: Evento 1008 informa a causa da falha de envio em lista fechada, sem dados do servidor (R-57)
- Status: done
- Covers: R-57; REQ-028
- Owner: implementer
- Blocked by: T-073
- Habilita: decisão do usuário de 2026-11-04; condições do `security` no ledger
- Demo: Com credencial recusada pelo servidor SMTP, o log traz `cause=auth`, e não só três falhas sem motivo.
- RED: (a) `SmtpPasswordResetSender`: cada tipo de falha do transporte registra o `cause` certo em `connect`, `tls`, `auth`, `rejected`, `timeout`, `other`; (b) `MailKitSmtpTransport`: falha de autenticação vira exceção própria, sem texto do servidor; (c) teste de redação: transporte fake lança exceção com senha, email e link na mensagem e na `InnerException`, e nenhum desses valores aparece em nenhum campo do log, inclusive no campo `exception`; (d) catálogo de `AccountsLog` com `cause` no evento 1008.
- GREEN: classificação das exceções do MailKit num só ponto da Infrastructure; `cause` entra como string de lista fechada; a exceção nunca é passada ao logger.
- REFACTOR: nenhum.
- Definition of done: DoD-P; prova de dente passando `ex` ao logger, para o teste de redação reprovar.

## T-179: Start recusa `Email:Smtp:User` preenchido com `UseTls=false` em qualquer ambiente (SEC-22)
- Status: done
- Covers: SEC-22
- Owner: implementer
- Blocked by: T-094
- Habilita: decisão do usuário de 2026-11-04
- Demo: Com `User` preenchido e `UseTls=false`, a API não sobe e a mensagem nomeia as duas chaves; com `User` vazio, o compose com Mailpit sobe como hoje.
- RED: `EmailOptionsValidator`, em Development e fora dele: `User` preenchido com `UseTls=false` reprova; a mensagem contém as chaves e nenhum valor; `User` vazio com `UseTls=false` em Development passa; `User` preenchido com `UseTls=true` passa.
- GREEN: regra no validador, com `string.IsNullOrWhiteSpace`, como o transporte decide autenticar.
- REFACTOR: nenhum.
- Definition of done: DoD-P; README cita a regra, sem repetir a orientação.

## T-180: Testes estáveis: start com proxy inválido e flakes do E2E (SEC-24, QA-21)
- Status: done
- Covers: SEC-24, QA-21
- Owner: qa
- Blocked by: T-175
- Habilita: decisão do usuário de 2026-11-04
- Demo: As rodadas completas do E2E não falham por espera de navegação, e o teste de proxy inválido não depende de corrida do `WebApplicationFactory`.
- RED: não se aplica à estabilidade; (SEC-24) a mensagem passa a ser testada direto em `TrustedProxyList.Parse`, nomeando chave e índice e sem ecoar valor.
- GREEN: (SEC-24) o teste de host exige só que o start falhe, inclusive com `ObjectDisposedException` da corrida do framework. (QA-21) achar a causa com logs completos: ocorrências em `accessibility.spec.ts` ("responsivo", S4, URL ainda em `/entrar/google/retorno?resultado=concluir`), `profile-avatar.spec.ts:211` e `auth-email.spec.ts:87` (`ERR_CONNECTION_REFUSED` no Vite), `account-deletion.spec.ts:219` (timeout). Corrigir a espera ou o ambiente, sem `retries` e sem relaxar asserção.
- REFACTOR: nenhum.
- Definition of done: 3 rodadas completas do E2E sem falha, com a saída guardada; se a causa de alguma ocorrência não for achada, ela segue `Open` no ledger com o que foi medido, sem declarar corrigido.

## T-181: Causa `tls` sai na falha real de handshake e ajustes da revisão final (R-57, R-67, R-68, R-69, R-71, R-72)
- Status: done
- Covers: R-57, R-67, R-68, R-69, R-71, R-72
- Owner: implementer
- Blocked by: T-178, T-180
- Habilita: revisão e verificação de segurança depois dos gates (2026-11-05)
- Demo: Falha no handshake de STARTTLS registra `cause=tls` no evento 1008.
- RED: `R57_tls_handshake_failure_throws_the_tls_cause` passa a lançar `MailKit.Security.SslHandshakeException`, o tipo real da biblioteca, e falha hoje com `Connect`.
- GREEN: `Classify` reconhece `SslHandshakeException` na etapa de conexão. (R-68) teste afirma `other` no emissor de `AccountsEventLog` e valor fora da lista fechada reprova. (R-69) nomes dos testes de host de `TrustedProxyListTests` dizem o que verificam. (R-71) a exceção do upload no E2E deixa de depender da medida exata do ambiente local, mantendo-se só para `input type="file"` em `.file-upload__input` no S10; comentário atualizado com a decisão do usuário. (R-72) o teste da lista fechada exercita todas as causas.
- REFACTOR: nenhum.
- Definition of done: DoD-P; a exceção do upload ainda reprova corte em outra tela ou em outro `<input>`.

## T-182: CI verde de verdade: gitleaks com permissão, CodeQL sem upload e com gate, E2E sem depender de fonte (SEC-25, SEC-26, QA-22)
- Status: done
- Covers: SEC-25, SEC-26, QA-22
- Owner: devops-docs
- Blocked by: T-175
- Habilita: run 42055126409 do PR #3; decisão do usuário de 2026-11-05
- Demo: Os jobs do CodeQL e do gitleaks rodam até o fim; um achado do CodeQL falha o job e fica disponível como artefato.
- RED: teste do novo `scripts/ci/check_sarif.py`, no padrão dos testes de `scripts/ci`: SARIF sem resultado passa; com resultado falha e lista regra, arquivo e linha; resultado suprimido não conta; arquivo ausente ou inválido falha, nunca passa em silêncio.
- GREEN: `ci.yml`: `pull-requests: read` só no job do gitleaks; `analyze` com `upload: never` e `output`; SARIF salvo como artefato, inclusive em falha, e gate com `check_sarif.py`; `security-events: read` no lugar de `write`. Ações fixadas em SHA, como as demais.
- REFACTOR: nenhum.
- Definition of done: suítes locais verdes; CI do PR #3 confirma. Achado real do CodeQL não é suprimido para o job passar: vira linha no ledger para decisão.

## T-183: Remover `FileUpload` e `AvatarPicker`, código morto da T-101 (SEC-28)
- Status: done
- Covers: SEC-28
- Owner: frontend
- Blocked by: T-101
- Habilita: decisão do usuário de 2026-11-05; a tela real de avatar é o `AvatarDialog`
- Demo: O app segue com upload e galeria de avatar pelo `AvatarDialog`, e `FileUpload.tsx` e `AvatarPicker.tsx` não existem mais.
- RED: não se aplica: remoção sem comportamento novo; os testes de `AvatarDialog` e o E2E de avatar são a guarda.
- GREEN: apagar `FileUpload.tsx`, `AvatarPicker.tsx` e os testes deles; preservar o CSS que `AvatarDialog` importa, movendo-o se fizer sentido, sem mudança visual.
- REFACTOR: nenhuma referência sobrando em exports, testes de design system nem varreduras de CSS.
- Definition of done: DoD-P; E2E de avatar e S10 verdes; `design.md` e T-101 sem citar como vigente o que saiu, com nota datada.

## T-184: CodeQL do PR com análise completa e C# só do código publicado (SEC-29, SEC-30)
- Status: done
- Covers: SEC-29, SEC-30
- Owner: devops-docs
- Blocked by: T-182
- Habilita: run 42069199015; verdito do `security` de 2026-11-05
- Demo: O PR passa a ver os mesmos achados que a `main`, e o stub de teste não entra na análise.
- RED: testes em `scripts/ci`: o `ci.yml` define `CODEQL_ACTION_DIFF_INFORMED_QUERIES: "false"` no job `codeql`; o build do CodeQL compila `Deckloom.Api.csproj`; todo `.csproj` de `backend/src` está na closure de `Deckloom.Api`. `check_sarif.py` imprime regra, arquivo e linha de cada resultado suprimido.
- GREEN: `ci.yml` e `check_sarif.py`. Sem lista de exceções, que só entra quando houver um primeiro caso que precise dela.
- REFACTOR: nenhum.
- Definition of done: log do run no PR com `No precomputed diff ranges found; skipping diff-informed analysis stage.`; CodeQL completo com 0 resultado ativo nas duas linguagens.

## Matriz de cobertura de critérios A11Y/UX (onde cada critério é verificado)

| Critério | Tarefa(s) |
|---|---|
| A11Y-01, 02, 04, 22, 26 | T-097 (componentes), T-107, T-109 |
| A11Y-03, 16 | T-096, T-097 |
| A11Y-05 | T-105 |
| A11Y-06 | T-099, T-118, T-119 |
| A11Y-07, 10 | T-003 (lint `no-positive-tabindex`), T-127 |
| A11Y-08, 11, 12 | T-099, T-118 |
| A11Y-09, 13, 14, 21, 32, 36 | T-095, T-127 |
| A11Y-15, 29 | T-096, T-101, T-117 |
| A11Y-17 | T-100, T-108, T-109 |
| A11Y-18, 19 | T-127 (parte manual listada em T-127) |
| A11Y-20, 31, 34 | T-128, T-109, T-110 |
| A11Y-37 (movimento reduzido) | T-095 (regra global), T-096, T-099, T-102, T-119, T-127 |
| A11Y-38 (`prefers-contrast: more`) | T-095, T-099, T-102, T-118, T-127 |
| A11Y-39 (sem transparência além do scrim) | T-095, T-127 (revisão do `ui-reviewer` na fase 6) |
| MOT-01, MOT-02 | T-096, T-101, T-102 |
| MOT-03 (foco sem animação) | T-095 |
| MOT-04 | T-097, T-107 |
| MOT-05 | T-096, T-108, T-109, T-113, T-115 |
| MOT-06 | T-100, T-108 |
| MOT-07 | T-096 |
| MOT-08 | T-096, T-110 |
| MOT-09 | T-096, T-115 |
| MOT-10 | T-128, T-110, T-114, T-120 |
| MOT-11 | T-102, T-120 |
| MOT-12, MOT-13 | T-099, T-117, T-118, T-119 |
| MOT-14 | T-101, T-117 |
| MOT-15 | T-098, T-101, T-102, T-115, T-116, T-117, T-120 |
| ART-01 | T-095, T-099, T-102 |
| ART-02 | T-107, T-109, T-111, T-112, T-113, T-114, T-115, T-116, T-117, T-118, T-119, T-127 (contagem e2e) |
| ART-03, ART-06, ART-07 | T-095 (varredura global de CSS, vale em todas as tarefas de frontend) |
| ART-04 | T-095 (lint) |
| ART-05 | T-095, T-098, T-116 |
| ART-08 | T-095, T-097, T-127 |
| ART-09 (`inspect.mjs --app` a 1440, 768 e 390) | T-127; DoD-F de toda tarefa de frontend (declarar se não rodou) |
| WC-01 | T-102, T-111, T-115, T-116, T-127 |
| WC-02 | T-096, T-115 |
| WC-03 | T-102 |
| WC-04 | T-115 |
| WC-05 | T-115, T-116 |
| WC-06 | T-097, T-107, T-109, T-111, T-112, T-113, T-114, T-118 |
| WC-07 | T-111 |
| WC-08 | T-096, T-115, T-116 |
| WC-09 | T-101, T-117, T-129 |
| WC-10 | T-096, T-115, T-117 |
| WC-11 | T-101, T-117 |
| WC-12 | T-100, T-108, T-109, T-112, T-113, T-118 |
| WC-13 | T-115, T-116 |
| WC-14 | T-099, T-118, T-119, T-127 |
| WC-15 | T-098, T-107, T-111, T-118, T-119 |
| WC-16 (`?data=worst` ausente do build de produção) | T-130 |
| WC-17 | T-115 |
| WC-18 | T-098, T-116 |
| A11Y-23, 30, 33 | T-102, T-096 |
| A11Y-24, 25 | T-098, T-107 |
| A11Y-27, 28 | T-098, T-101, T-116 |
| A11Y-35 | T-115 |
| UX-01 | T-109, T-083 |
| UX-02 | T-113, T-084 |
| UX-03 | T-114, T-124 |
| UX-04 | T-107, T-111 |
| UX-05 | T-088, T-115 |
| UX-06 | T-081, T-115, T-116 |
| UX-07 | T-102, T-117, T-125 |
| UX-08, 09 | T-118, T-120, T-122, T-126 |
| UX-10 | T-127 |
| UX-11 | T-114, T-124 |
| UX-12 | T-106, T-109, T-110, T-085 |
| UX-13 | T-115, T-116 |
| UX-14 | T-119, T-126 |

## Matriz de mitigações do threat model (onde cada uma é verificada)

| Mitigação | Tarefa(s) |
|---|---|
| Força bruta / atraso progressivo, limitador por IP, semáforo de hashing | T-022, T-029, T-053, T-091 |
| Sessão: token de 256 bits só como hash, cookie `__Host-`, novo token por login, revogação | T-021, T-033, T-054, T-057, T-080 |
| CSRF (antiforgery + `Origin`), CORS desabilitado | T-076, T-078 |
| Pré-sequestro Google (vínculo só com senha, só `email_verified`, busca por `subject`) | T-018, T-037, T-038, T-043, T-069 |
| OAuth (`state`, PKCE S256, `nonce`, `id_token`, `redirect_uri` fixo, cookie de 10 min) | T-067, T-068, T-069, T-085 |
| Link de redefinição (256 bits, 60 min, uso único, só hash, fragmento, `replaceState`, `no-referrer`) | T-019, T-035, T-036, T-058, T-073, T-077, T-114, T-005 |
| Reautenticação Google antiga (`prompt=login`, `max_age=300`, `auth_time`, 5 min) | T-020, T-039, T-050, T-067, T-069, T-119 |
| IDOR | T-052, T-088 |
| Upload malicioso e bomba de descompressão (tamanho, assinatura, cabeçalho, limites, reencode) | T-070, T-071, T-072, T-089, T-091 |
| Código nativo do Skia (versão fixada, timeout, semáforo, limites) | T-070, T-072 (revisão `security` na fase 7) |
| Injeção de SQL | T-002 |
| XSS (texto puro, CSP, Cc) | T-003 (`react/no-danger`), T-008, T-009, T-077, T-115 |
| Repudiação (aceite com versão vigente, 18+, data/hora) | T-013, T-015, T-055 |
| Enumeração (mensagens únicas, hash fictício, 500 ms, HMAC) | T-033, T-035, T-083, T-084, T-109, T-113 |
| Enumeração no cadastro (exceção aceita) com atraso por IP | T-032, T-082, T-108 |
| Logs sem dados pessoais | T-023, T-079, T-093 |
| Vazamento do banco (só hashes/HMAC) | T-054, T-055, T-057, T-058, T-059, T-060 |
| Retenção de rate limit (24 h, exclusão por HMAC da conta) | T-049, T-051, T-060, T-063, T-075 |
| Email fora da leitura do perfil | T-045, T-088, T-115 |
| EXIF removido; avatar privado ao dono | T-072, T-089 |
| Open redirect (`retorno`/`returnTo`) | T-085, T-106 |
| Conexões presas por atraso, bombardeio de emails | T-029, T-074, T-091 |
| Ações destrutivas só com confirmação e reautenticação | T-020, T-049, T-050, T-118, T-119 |
| Sessão expirada/revogada com `no-store` | T-034, T-080 |
| Superusuário/dono/migrador usado pela API (papéis, DB1, DB2, DB7) | T-005, T-134, T-135, T-136 |
| Privilégio excessivo de `deckloom_app` (matriz, `GRANT` por coluna, imutabilidade de `email`/aceite; DB3, UA1) | T-066, T-132, T-133 |
| DDL ou migração em tempo de execução (DB6) | T-131 |
| Banco sem TLS (DB8) | T-136 (e fase 8 no servidor) |
| Consultas longas, locks, conexões presas/esgotadas (DB9) | T-063, T-137, T-138, T-139 |
| Senha de banco em arquivo versionado ou em log (DB10) | T-005 |

## Itens do design que não foram alocados a uma tarefa desta fase

- Meta de p95 de 3 s e disponibilidade informal de 99%: dependem de ambiente real; esta fase só instrumenta (T-092). Verificação fica para o `qa` (cobertura/medição local) e fase 8.
- Roteamento e ferramenta dos alertas, retenção de logs (proposta de 30 dias), contêiner da API sem root e com sistema de arquivos somente leitura, auditoria de dependências no CI (`dotnet list package --vulnerable`, `npm audit`) e cobertura de 80% imposta no CI: fase 8 (CI/CD). T-005 só verifica que o Dockerfile roda sem root.
- Itens do SQLite em memória sem verificação automática (`timestamptz`, `bytea`/checks, índice parcial, `xmin`, schemas e FK entre schemas, `DELETE ... RETURNING` do Npgsql, desempenho): validação manual por humano no ambiente local a partir das migrações geradas em T-066.
- Controle de acesso ao banco (emenda ADR-0015): execução do bootstrap, papéis reais, `GRANT` efetivos, posse dos objetos, limites e timeouts no servidor, TLS real, `pg_hba.conf`/`ssl = on` do servidor, backups e retenção (DB12: retenção de 30 dias e reaplicação das exclusões após restauração decididas pelo usuário em 2026-10-31; a reaplicação é conferida manualmente, por um humano, numa restauração real; criptografia e papel de backup com D12/D13) e `NOLOGIN`/`VALID UNTIL` do migrador entre implantações (UA5): humano e/ou fase 8; lista de conferência em "Validação manual por humano (ADR-0015)".
- Texto final dos Termos/Política, validação jurídica (D12/D13) e cliente OAuth Google por ambiente: pendências para o release (já registradas nos requisitos).
- Inclusão do termo "Cadastro Google Pendente" no `docs/glossary.md`: tarefa do `domain`/`knowledge-graph`, não de código.
- Funcionalidade de Decks (feature 9): `NoDecksYetPurger` (T-064) é no-op até lá.

## Paralelização

Premissa: cada tarefa paralela roda em seu próprio git worktree e toca arquivos distintos; conflitos de merge só nos pontos indicados.

- **Trilha backend x trilha frontend**: totalmente independentes depois da fundação (T-001 e T-003 em paralelo). O frontend usa MSW e não depende da API; só os E2E (grupo G) juntam as duas.
- **Fundação**: T-001 || T-003; depois T-002, T-004 e T-005 em paralelo (T-004 depende de ambas). Situação em 2026-10-23: T-001 a T-004 concluídas e commitadas; T-005 em andamento (RED escritos, compose/Dockerfiles/nginx não commitados; `.env.example` depende de um humano).
- **Domínio**: após T-001, os value objects T-006, T-007, T-008, T-010, T-012, T-013, T-014 são independentes entre si (T-009 depende de T-008; T-011 é independente). `Player` (T-015 a T-020) edita o mesmo arquivo: sequencial. `Session` (T-021) e `ProgressiveDelayPolicy` (T-022) são independentes do `Player`.
- **Aplicação**: fakes T-026, T-027 e T-028 em paralelo após T-025. Casos de uso de grupos diferentes são independentes: {T-030..T-032 cadastro}, {T-033, T-034 login/sessão}, {T-035, T-036 redefinição}, {T-037..T-043 Google}, {T-044..T-048 perfil/avatar}, {T-049, T-050 exclusão}; dentro de cada grupo, sequencial.
- **Infraestrutura**: independentes entre si e do EF: T-053, T-054, T-067..T-069 (Google), T-070..T-072 (Skia), T-073/T-074 (email). O bloco EF (T-055 a T-063, T-065, T-066) compartilha o `AccountsDbContext`: sequencial depois de T-055, com T-057 a T-062 paralelizáveis se cada um usar sua própria classe de configuração de entidade.
- **API**: após T-076 a T-080, os grupos de endpoints são independentes (cada um em seu arquivo de mapeamento de rotas): {T-081}, {T-082, T-083}, {T-084}, {T-085..T-087}, {T-088}, {T-089}, {T-090}. T-091 a T-094 dependem dos endpoints.
- **Frontend**: componentes T-096 a T-102 em paralelo depois de T-095 (T-097/T-098/T-099/T-100 independentes entre si); T-103, T-104 e T-105 em paralelo; telas S1-S11 (T-107 a T-119) em paralelo após suas dependências, com conflito possível apenas na tabela de rotas (cada tela registra a sua).
- **Novas tarefas (ADR-0014)**: T-128 (foco/título por rota) e T-130 (fixtures) são independentes de componentes e podem rodar em paralelo com T-096 a T-102; T-129 (SVGs dos presets) depende só de T-065 e T-096.
- **E2E**: T-122 a T-126 em paralelo após T-121; T-127 por último.
- **Controle de acesso ao banco (emenda ADR-0015, Grupo H)**: T-131 (só depende de T-002) e T-132 (só de T-002) rodam já, em paralelo com T-005 e com o domínio, em arquivos distintos de `ArchitectureTests`; T-134 (só de T-001) é independente e pode rodar com o domínio. T-066 passa a depender de T-132 e T-133 vem depois do bloco EF (T-063 e T-132). T-135 e T-136 estendem a composição e vêm depois de T-094 (independentes entre si, mas ambos editam `Deckloom.Api.Composition`: sequenciais ou com merge cuidadoso). T-137 vem depois dos casos de uso de cadastro, login, redefinição, Google, avatar e exclusão (T-028, T-029, T-033, T-035, T-041, T-043, T-048 a T-050) e de T-027; é o último ajuste de aplicação e conflita com esses arquivos, então roda sozinho. T-138 vem depois de T-075 e T-133; T-139 depois de T-076, T-079 e T-092, e T-093 passa a esperar T-139. A validação manual (H-1 a H-10) é paralela a tudo e só depende de T-005 e T-066 gerados.

## Validação manual por humano (ADR-0015)

Não é tarefa de agente: nenhum agente executa bootstrap, conecta ao PostgreSQL ou lê senhas. Depois de T-005 e T-066 gerados, uma pessoa confere num banco local descartável (consultas a cargo dela; a lista completa de consultas está em `Database/roles/README.md` > "Validação manual", itens 1-8 abaixo). O resultado entra no PR/`STATUS.md` antes do gate de release.

| # | Conferência (humano) | Origem | Tarefa que produz o artefato |
|---|---|---|---|
| H-1 | Volume `db-data` vazio (recriá-lo com `docker compose down -v` apaga dados locais e exige confirmação humana); `01-roles.sh` roda, falha com variável vazia e os papéis existem com os atributos de `Database/roles/*.md`; `deckloom_app` não é superusuário/`CREATEDB`/`CREATEROLE`/`REPLICATION`/`BYPASSRLS` nem membro de papel (README 1) | DB10, UA4 | T-005 |
| H-2 | A consulta de T-135 devolve o resultado esperado para `deckloom_app` (aceita) e a API recusa iniciar quando, em banco descartável, o papel ganha superusuário, `CREATEDB`, `CREATEROLE`, `BYPASSRLS`, `CREATE` ou posse de objeto | DB7, UA2 | T-134, T-135 |
| H-3 | Privilégios de tabela e coluna de `deckloom_app` (`role_table_grants`, `column_privileges`) são exatamente os da matriz; o SQL real do Npgsql (`UPDATE`, `DELETE ... RETURNING`, `xmin`) usa só colunas concedidas (README 3) | DB3, UA1 | T-066, T-132, T-133 |
| H-4 | Todos os objetos de `accounts` e `deck_suggestion`, incluindo `__ef_migrations_history`, têm dono `deckloom_owner`; o migrador sem `SET ROLE` não consegue criar objetos (README 4) | DB2 | T-005, T-066 |
| H-5 | `INSERT` em `accounts.access_attempt` como `deckloom_app` funciona sem privilégio na sequência de identidade (README 5) | Matriz | T-066 |
| H-6 | Conexão TLS real com `SSL Mode=VerifyFull` e `Root Certificate` fora do compose; no compose de dev `SSL Mode=Disable` só com `Development`; `pg_hba.conf`/`ssl = on` são fase 8 | DB8, UA3 | T-136 |
| H-7 | Parâmetros por papel (`pg_db_role_setting`) e `CONNECTION LIMIT` (`pg_roles.rolconnlimit`) conferem com os arquivos de papel; tempo de cada lote de limpeza bem abaixo de `statement_timeout` (README 6) | DB9, UA3 | T-005, T-138 |
| H-8 | Como `deckloom_app`, `CREATE TABLE accounts.x (i int)` e `UPDATE accounts.player SET email = ...` falham com `42501`; fluxos ponta a ponta (cadastro, login, perfil, avatar, redefinição, exclusão, job de limpeza) funcionam com a API como `deckloom_app` (README 2, 7, 8) | DB3, DB6 | T-066, T-131 |
| H-9 | O compose real: serviço `api` sem variável `POSTGRES_*` nem `DECKLOOM_MIGRATOR_*`, connection string com `Username=deckloom_app`, senhas só no `.env` local (não versionado) | DB10 | T-005 |
| H-10 | Fase 8 (não bloqueia a feature): backups x contas excluídas (DB12, decisão do usuário de 2026-10-31: retenção de 30 dias; numa restauração real, conferir que as exclusões de conta posteriores ao backup são reaplicadas antes de liberar o acesso e que o log da reaplicação não tem dado pessoal; conferência manual, feita por um humano, nunca por agente; criptografia com D12/D13), `NOLOGIN`/`VALID UNTIL` do migrador entre implantações (UA5), papel de backup | DB12, UA5 | - |

## Decisões da fase 4

Decididas pelo usuário em 2026-10-23 (aceitaram as recomendações do planner):

1. **Formato**: o modelo, a migração e a carga inicial de `deck_suggestion.format` ficam em `Deckloom.DeckSuggestion.Infrastructure` (dono do dado, ADR-0007), com projeto de teste próprio `Deckloom.DeckSuggestion.Infrastructure.Tests`; a porta `IFormatCatalog` só expõe leitura (T-001, T-062, T-065, T-066).
2. **Fixtures de imagem**: binários pequenos commitados (JPEG com EXIF/GPS, APNG, WebP animado, PNG com cabeçalho declarando 20000x20000, entre outros); os grandes (2,1 MB e 2.097.152 bytes) são sintetizados em memória no teste (T-070).
3. **Migrações**: `dotnet ef migrations add` com `IDesignTimeDbContextFactory` e connection string fictícia que nunca conecta; só geração de arquivos; `dotnet ef database ...` é proibido e bloqueado pelo guard de banco (T-066).
4. **Relógio no E2E**: endpoint de teste de avanço do relógio existente só no ambiente `E2E` (404 nos demais, com teste), usado nos cenários de 5 min (REQ-042) e de 10 min (pendência Google) (T-121, T-123, T-126).
5. **Host E2E**: projeto `backend/tests/Deckloom.Api.E2E.Host`; `Deckloom.Api` de produção nunca referencia `*.Testing`, garantido por regra de arquitetura (T-002, T-121).

Emenda ADR-0014 (2026-10-23), decisões de produto do usuário já refletidas nas tarefas de frontend: bio limitada a 10 linhas com "Mostrar tudo" (T-115); novo estado de S10 "Não foi possível carregar os avatares. Tente de novo." (T-101, T-105, T-117); nome completo no painel do AccountMenu e AppHeader sem o nome abaixo de 480px (T-102).

Emenda ADR-0015 (2026-10-23), escolhas de planejamento que o usuário confirma na reaprovação (decisões de engenharia dentro do que o design já fixou, não perguntas bloqueantes): (a) T-137 verifica "nenhuma transação aberta durante atraso/hash/Google/SMTP/imagem" por sondas `IsTransactionOpen` nos dublês, porque o design não define o método; (b) T-135 recusa iniciar também quando a consulta de papel falha (fail closed); (c) T-138 usa `Cleanup:BatchSize` com padrão 1000; (d) T-136 recusa `Username` `postgres`/`deckloom_owner`/`deckloom_migrator` em qualquer ambiente e chaves `POSTGRES_*`/`DECKLOOM_MIGRATOR_*` na configuração da API; (e) SQLSTATEs fora de `42501/57014/55P03/25P03/53300` não geram o evento 1021.

## Perguntas em aberto

Nenhuma.
