# Deckloom

Deck builder para jogadores de TCG (trading card games). As especificações ficam em `docs/specs/`; as decisões de arquitetura em `docs/adr/`; os termos do domínio em `docs/glossary.md`.

Estado atual: a feature `conta-jogador` (cadastro, login com email e Google, perfil e exclusão de conta) está implementada e fechada. Não há deploy de produção; os bloqueios de go-live estão em `docs/specs/conta-jogador/release-notes.md`. O histórico de mudanças está em `CHANGELOG.md`.

## Stack e estrutura

- **Backend:** C# / .NET 10, Entity Framework Core, PostgreSQL 17 (monolito modular, ADR-0001). Solução: `backend/Deckloom.sln`.
- **Frontend:** TypeScript, React 19, Vite, Node.js 24. Projeto: `frontend/`. Testes E2E com Playwright em `frontend/e2e/`.
- **Outros diretórios:** `scripts/ci/` (portão de cobertura e auditoria NuGet usados pelo CI), `.github/workflows/ci.yml` (pipeline).

## Pré-requisitos

- .NET SDK 10.0 (build, testes e formatação).
- Node.js 24 e npm.
- Docker com Docker Compose (execução local completa).
- Para E2E: navegador Chromium do Playwright (instalado por comando, ver abaixo).

## Executar localmente com Docker Compose

1. Crie o arquivo `.env` a partir do `.env.example` e preencha os valores. O `.env` não vai para o git. As variáveis que o `docker-compose.yml` lê são estas (apenas os nomes; os valores ficam somente no seu `.env`):
   - Banco: `POSTGRES_PASSWORD`, `DECKLOOM_APP_DB_PASSWORD`, `DECKLOOM_MIGRATOR_DB_PASSWORD`.
   - Autenticação: `Auth__PublicOrigin`, `Auth__Google__ClientId`, `Auth__Google__ClientSecret`, `Auth__RateLimitHmacKey`.
   - Email: `Email__From`, `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__UseTls`, `Email__Smtp__User`, `Email__Smtp__Password`.
2. Suba os serviços: `docker compose up --build`.
3. Acesse a aplicação em `http://localhost:8081` (nginx, que também repassa `/api`). A API não publica porta de propósito: os cookies `__Host-` exigem origem única. A caixa de email de desenvolvimento (Mailpit) fica em `http://localhost:8025`.

Limitações do ambiente local:

- As portas `8081` e `8025` são publicadas só em `127.0.0.1` (SEC-17): não ficam acessíveis a outros hosts da rede. O Mailpit não tem autenticação e mostra os links de redefinição de senha; não mude esse bind para uma interface de rede. No compose local, deixe `Email__Smtp__User` e `Email__Smtp__Password` vazios: com usuário preenchido a API tenta autenticar, o Mailpit não aceita e o email de redefinição não sai (evento 1008). Além disso, a API recusa iniciar, em qualquer ambiente, com `Email__Smtp__User` preenchido e `Email__Smtp__UseTls` falso, porque a senha SMTP seguiria em claro.
- O compose roda a API com `ASPNETCORE_ENVIRONMENT=Development`. Nesse ambiente são aceitos SMTP sem TLS (desde que `Email__Smtp__User` esteja vazio, ver acima) e conexão com o banco sem TLS (`SSL Mode=Disable`, só na rede interna do Docker). Os dados são fictícios.
- A API não cria o schema. As migrações são arquivos revisados aplicados por uma pessoa com o papel `deckloom_migrator`. O procedimento local de aplicação ainda não está documentado: é pendência (ver `docs/specs/conta-jogador/release-notes.md`).

## Testes, cobertura, lint e build

Execute a partir da raiz do repositório.

- **Testes:** `dotnet test backend/Deckloom.sln` e `npm --prefix frontend run test -- --run`.
- **Cobertura (como o CI):**
  - Backend: `dotnet test backend/Deckloom.sln -c Release --settings backend/coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory backend/TestResults` e depois `python3 scripts/ci/check_coverage.py --reports-dir backend/TestResults --threshold 80`. Código gerado (migrações, `*.g.cs`, `obj/`) é excluído pelo `backend/coverlet.runsettings`.
  - Frontend: `npm --prefix frontend run test -- --run --coverage`. O limite de 80% (linhas, instruções, funções e ramos) está em `frontend/vite.config.ts`.
- **Lint e formatação:** `dotnet format backend/Deckloom.sln --verify-no-changes && npm --prefix frontend run lint && npm --prefix frontend run format:check`.
- **Build:** `dotnet build backend/Deckloom.sln -c Release && npm --prefix frontend run build`.
- **Testes dos scripts de CI:** `python3 -m unittest discover -s scripts/ci -p "test_*.py"`.
- **E2E (Playwright):** instale o Chromium uma vez com `npm --prefix frontend exec -- playwright install --with-deps chromium` e rode `npm --prefix frontend run e2e`. O comando sobe o host de teste `backend/tests/Deckloom.Api.E2E.Host` (ambiente `E2E`, persistência em memória, relógio falso, Google simulado) e o Vite. Não usa banco nem credenciais reais.

Os testes de backend não acessam banco: a persistência é feita por fakes em memória e por SQLite em memória (ADR-0012).

## Pipeline de CI

O workflow `.github/workflows/ci.yml` roda em pull requests e em pushes para `main`:

- **ci-scripts:** testes dos scripts de CI (`scripts/ci/`).
- **backend:** `dotnet format --verify-no-changes`, build Release, testes com cobertura e portão de 80% nos projetos de produto.
- **frontend:** lint, `format:check`, build, testes com cobertura (limite 80%).
- **e2e:** Playwright contra o host de teste, sem banco.
- **codeql:** SAST com CodeQL para C# (build manual só de `backend/src/Deckloom.Api/Deckloom.Api.csproj`) e JavaScript/TypeScript. Os resultados vão para o Code Scanning do GitHub, e o job falha com qualquer achado ativo.
- **secret-scan:** gitleaks.
- **dependency-audit:** `npm audit --omit=dev` e `dotnet list ... package --vulnerable --include-transitive`, falhando em qualquer achado.

O CI não recebe credencial de banco e não executa migrações. O único segredo que o workflow lê é o `GITHUB_TOKEN` automático, usado pelo gitleaks. A governança dos agentes de IA não roda no CI: é feita localmente pelo AISEF (ver "Desenvolvimento com agentes (AISEF)"). O E2E usa os valores falsos já versionados em `backend/tests/Deckloom.Api.E2E.Host/appsettings.E2E.json`. As ações do CodeQL, como as demais, são fixadas em SHA de commit.

## Configuração de produção

Os valores abaixo não vão para o repositório. Defina-os no ambiente do host ou no gerenciador de segredos da plataforma. Os nomes seguem a convenção de configuração do ASP.NET Core (`__` como separador em variáveis de ambiente).

- **`Forwarding:TrustedProxies`** (`Forwarding__TrustedProxies__0`, ...): IP ou CIDR do reverse proxy que fica na frente da API. Só esses proxies têm o `X-Forwarded-For` aceito (ADR-0016). Liste apenas o proxy da sua topologia; não use faixas públicas.
- **`Auth:PublicOrigin`** (`Auth__PublicOrigin`): origem pública em `https` (por exemplo, `https://<seu-dominio>`). Fora de `Development` a API recusa `http`. O atributo `Secure` dos cookies depende desta chave.
- **`Email:Smtp`**: `Host`, `Port`, `UseTls` (deve ser `true` fora de `Development`), `User` e `Password` quando o servidor exigir autenticação. `Email:From` define o remetente.
- **`ConnectionStrings:Deckloom`**: deve usar o papel `deckloom_app`, com `SSL Mode=VerifyFull` e `Root Certificate`. Fora de `Development` a API recusa outro `SSL Mode`, e recusa papel privilegiado (DB7). Nunca use o superusuário, o dono do schema ou o migrador.
- **`Auth:Google:ClientId`, `Auth:Google:ClientSecret`, `Auth:RateLimitHmacKey`**: o `ClientSecret` e a chave HMAC são segredos. Não reutilize valores entre ambientes.
- **Chaves do Data Protection** (cookie de correlação do login Google): o volume de chaves precisa ser persistente. Com mais de uma instância, o armazenamento compartilhado ainda é pendência (ver `docs/specs/conta-jogador/release-notes.md`).

### Papéis do banco (ADR-0015)

- `postgres`: superusuário de bootstrap. A aplicação nunca o usa.
- `deckloom_owner` (NOLOGIN): dono de todos os objetos e dono da DDL.
- `deckloom_migrator`: login usado só por uma pessoa, para aplicar migrações já revisadas. Tem limites próprios (conexões e timeouts).
- `deckloom_app`: papel de runtime da API, com DML por coluna conforme as migrations em `backend/src/Modules/*/Infrastructure/Migrations` e a ADR-0015/ADR-0019.

A API não chama `Migrate`, `EnsureCreated` nem `GetPendingMigrations` (há teste de arquitetura para isso). As migrações são geradas como arquivos com `dotnet ef migrations add`, revisadas e aplicadas por uma pessoa. Nenhum agente nem o CI executa `dotnet ef database update`.

## Operação: backups e retenção (LGPD)

- **Retenção:** os backups do banco são mantidos por 30 dias. Uma conta excluída pode continuar em backups por até esse prazo.
- **Restauração:** antes de liberar o acesso à API após restaurar um backup, reaplique as exclusões de conta feitas depois da data do backup. Sem essa etapa, a restauração não é liberada.
- **Log:** a reaplicação é registrada sem dado pessoal (data de corte e quantidade de exclusões, sem e-mail, nome, identificador de conta ou IP).
- Requisito documentado, sem procedimento implementado. Criptografia dos backups e papel de backup seguem pendentes (ver `docs/specs/conta-jogador/release-notes.md`, item 7).

## Validação manual (H-1 a H-10)

A lista completa, com o critério de cada item, está em `docs/specs/conta-jogador/tasks.md`, seção "Validação manual por humano (ADR-0015)". Em resumo:

| Item | Verifica |
|---|---|
| H-1 | Inicialização dos papéis com o volume `db-data` vazio |
| H-2 | A API recusa iniciar se o papel de aplicação ganhar superusuário |
| H-3 | Privilégios de tabela e coluna de `deckloom_app` iguais à matriz |
| H-4 | Todos os objetos têm dono `deckloom_owner` |
| H-5 | `INSERT` em `accounts.access_attempt` sem privilégio extra na sequência |
| H-6 | TLS real com `SSL Mode=VerifyFull` e `Root Certificate` fora do compose |
| H-7 | Parâmetros por papel e `CONNECTION LIMIT` conferem com os arquivos de papel |
| H-8 | DDL como `deckloom_app` falha com `42501`; fluxos ponta a ponta |
| H-9 | Compose real: a API não recebe variáveis do Postgres nem do migrador |
| H-10 | Fase 8, não bloqueia a feature: backups x LGPD, `NOLOGIN`/`VALID UNTIL` do migrador, papel de backup |

## Segurança e LGPD

- Baseline de segurança: `docs/security-policy.md`. Relatório da fase 7 (checklist ASVS, evidências e riscos aceitos): `docs/specs/conta-jogador/security-report.md`.
- Compliance com a LGPD: o relatório traz a situação por tema (minimização, eliminação, acesso, retenção). Não há certificação de conformidade. A retenção de backups está definida em 30 dias (ver "Operação: backups e retenção"); base legal e encarregado dependem de decisão humana.

## Desenvolvimento com agentes (AISEF)

O projeto é governado pelo AISEF (plugin `aisef` do Claude Code), adotado na ADR-0017. O estado fica em `.aisef/`: discovery, Feature Contracts, Engineering Blueprints, Task Contracts, aprovações e um log de auditoria encadeado e assinado com Ed25519. A chave privada fica fora do repositório, em `~/.aisef/`.

- No Claude Code, o Execution Gateway do plugin bloqueia acesso ao banco e leitura de segredos, e exige aprovação do usuário para operações Git e destrutivas.
- Em outras ferramentas (Codex, Cursor, Gemini), as regras da seção AISEF do `AGENTS.md` são obrigatórias, mas não há bloqueio técnico.
- Nunca edite à mão `.aisef/audit/`, `.aisef/keys/`, `.aisef/approvals/` nem `.aisef/authorizations/`.

## Documentação

- `.aisef/`: estado de engenharia atual (discovery, contratos, aprovações, auditoria).
- `docs/specs/`: registro histórico das especificações anteriores ao AISEF (discovery inicial e a feature `conta-jogador`).
- `docs/adr/`: decisões de arquitetura.
- `docs/glossary.md`: linguagem ubíqua.
- `CHANGELOG.md`: mudanças por versão.
- `docs/specs/conta-jogador/release-notes.md`: notas de lançamento e checklist operacional antes de produção.
