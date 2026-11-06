# Discovery - Deckloom (projeto existente)

Data: 2026-11-06 | Modo: projeto existente (migração para o AISEF, ADR-0017) | Feito pelo orquestrador AISEF a partir do repositório.

Classificação de cada item: **[C]** confirmado pelo usuário (no discovery de 2026-10-21 ou depois) | **[E]** encontrado (com caminho) | **[I]** inferido (vira pergunta) | **[D]** desconhecido.

## Visão
- [C] Deck builder para jogadores de TCG, começando por Magic: The Gathering. A partir das cartas do jogador (decklist colada ou busca manual) e do formato, sugere arquétipo/mecânica, cartas faltantes e **onde comprar pelo melhor preço** em lojas externas independentes (scraping). O produto principal é o motor de sugestão; a comparação de preço é a alavanca de conversão. Fonte: `docs/specs/discovery.md` (Problema e objetivo).
- [C] Único ator com login: Jogador. Loja é só dado, sem conta. Fonte: `docs/specs/discovery.md` (Atores, decisão 1).
- [C] MVP só Magic; o modelo de dados prevê outros TCGs. Fora do MVP: outros TCGs, checkout, login de loja, reconhecimento de imagem, app nativo. Fonte: `docs/specs/discovery.md` (Escopo, Fora de escopo).
- [C] Métricas de sucesso: conclusão de sugestão, clique em "comprar", retenção de segundo deck em 30 dias. Fonte: `docs/specs/discovery.md`.

## Requisitos (com origem)
### Já implementados - feature `conta-jogador` (fechada em 2026-11-05, sem deploy)
- [E] REQ-001..REQ-049 aprovados e entregues: cadastro email+senha com aceite de termos e 18+, login email+senha, login Google OIDC com PKCE e vínculo, redefinição de senha por email (60 min, uso único, derruba sessões), perfil (nome, bio, TCG favorito, formatos), avatar preset ou upload reprocessado, exclusão de conta com reautenticação, catálogos como dados. Fonte: `docs/specs/conta-jogador/requirements.md`, `CHANGELOG.md`.
- [E] 184/184 tarefas; cobertura acima de 93% no backend e de 98% no frontend (limite 80); rastreabilidade REQ x tarefa com 0 falhas no fechamento. Fonte: `docs/specs/conta-jogador/retro.md`, `STATUS.md` da feature.

### Propostos e ainda não especificados (ordem do discovery inicial)
- [C] F2 Entrada de decklist (texto) e busca manual de cartas.
- [C] F3 Motor de sugestão de deck/arquétipo por similaridade/regras com dados de meta (sem ML).
- [C] F4 Sugestão de cartas faltantes e mecânica.
- [C] F5 Scraper de preços: LigaMagic, MYP Cards, XPlace, Meruru, Epic Game; snapshot imutável com timestamp.
- [C] F6 Scraper de meta: MTGMelee, EDHREC, MTGGoldfish, MTGTop8.
- [C] F7 Comparador de preço / sugestão de compra (link de saída).
- [C] F8 Painel de decks em alta e comandantes preferidos.
- [C] F9 CRUD de decks do jogador.
- [C] F10 Observabilidade/alertas de scraper.
Fonte: `docs/specs/discovery.md` (Lista de features propostas).

### Não funcionais
- [C] p95 de 3 s em sugestão e comparação de preço (dados pré-computados; nunca scraping síncrono); 99% de uptime informal; WCAG 2.1 AA; logs estruturados e alerta de fonte parada; LGPD (direito ao esquecimento; sem CPF nem pagamento). Fonte: `docs/specs/discovery.md` (Requisitos não funcionais).

## Regras de negócio
- [C] Preço sempre exibido com o timestamp da coleta; coleta periódica, nunca na requisição. Fonte: `docs/specs/discovery.md`.
- [C] Snapshots de preço imutáveis, sem expurgo no MVP. Fonte: idem, decisão 7.
- [C] Scraping preventivo: robots.txt quando possível, rate limit, cache, retry com backoff, alerta de parser quebrado, API oficial quando existir, degradação graciosa por fonte. Fonte: idem (Integrações).
- [E] Regras da conta (atraso progressivo, sessão por cookie opaco, exclusão atômica, hash PBKDF2...): ADR-0002..ADR-0006, `docs/specs/conta-jogador/requirements.md`.

## Arquitetura atual
- [E] Monolito modular com Clean Architecture (ADR-0001), monorepo `backend/` + `frontend/` (ADR-0008).
- [E] Backend .NET 10: `Deckloom.Api` (composição, auth, http, logging, problems, security), `Deckloom.SharedKernel`, módulo `Accounts` (Domain/Application/Infrastructure) e módulo `DeckSuggestion` (só Infrastructure, com o catálogo de formatos). Testes: unitários por camada, infraestrutura (EF sobre SQLite em memória, ADR-0012), API, arquitetura e host E2E.
- [E] Frontend React 19 + Vite + TypeScript: `src/app` (rotas, sessão, guards), `src/features/conta`, `src/design-system`, `src/shared`. E2E Playwright em `frontend/e2e/`. Design system em `docs/design-system.md` (v0.1).
- [E] Email por SMTP com MailKit e Mailpit em dev (ADR-0010); imagem com SkiaSharp (ADR-0011); avatar customizado no PostgreSQL (ADR-0009); proxy confiável (ADR-0016).
- [E] CI (`.github/workflows/ci.yml`): ci-scripts, backend, frontend, e2e, codeql, secret-scan (gitleaks), dependency-audit.

## Dados
- [E] PostgreSQL 17. Schema `accounts`: `player`, `player_session`, `player_external_identity`, `player_preferred_format`, `password_reset_token`, `pending_google_auth`, `access_attempt`, `avatar_image`, `avatar_preset`. Schema `deck_suggestion`: `format`. Fonte: `Database/tables/`, `Database/er-diagram.md`.
- [E] Migrations: `20261025020132_CreateAccountsSchema` e `20261025015138_CreateFormatCatalog`. Geradas como arquivo; aplicadas por uma pessoa com `deckloom_migrator`.
- [E] Papéis `postgres` (bootstrap), `deckloom_owner` (NOLOGIN), `deckloom_migrator`, `deckloom_app` (DML por coluna). Fonte: ADR-0015, `Database/roles/privilege-matrix.md`.
- [D] Estado real de qualquer banco: não consultado, por regra (T5). Tudo acima vem de arquivos.
- [C] Entidades futuras: Deck, Carta, Loja, SnapshotDePreço, Fonte de Meta. Fonte: `docs/specs/discovery.md` (Entidades).

## Integrações
- [E] Google OIDC (login), SMTP (email). Fonte: ADR-0004, ADR-0010.
- [C] Futuras: 5 lojas de preço e 4 fontes de meta via scraping (ver Requisitos). Risco de ToS aceito pelo usuário, com revisão jurídica recomendada antes de produção.

## Segurança
- [E] Baseline: `docs/security-policy.md` (atualizada para o AISEF). Relatório ASVS da conta: `docs/specs/conta-jogador/security-report.md`. Nenhum achado High/Critical aberto (retro).
- [E] Governança dos agentes: Execution Gateway do AISEF, regras nativas em `.claude/settings.json` (ask do AISEF, mais deny extras para `dotnet ef database`, dumps e CLIs de cofre de segredos), 9 Agent Contracts ativos.
- [E] Repositório agora **público**. O CodeQL envia ao Code Scanning, o que fecha o item "CodeQL sem aba de alertas" de `release-notes.md`.

## Testes
- [E] Pirâmide e regras em `docs/test-strategy.md`: suíte de contrato por fake de persistência (fake x EF), composição real (`__Host-` com `Secure`), `VITEST_MAX_WORKERS=2` em suítes paralelas (QA-11).
- [E] Cobertura mínima de 80% (backend: `scripts/ci/check_coverage.py`; frontend: `frontend/vite.config.ts`).

## Riscos e dívida técnica
Fonte de tudo abaixo: `docs/specs/conta-jogador/retro.md` e `release-notes.md`.
- [C] **Corrigir antes da próxima feature** (triagem aprovada em 2026-11-01): R-24, R-27, R-28, R-29, R-36, R-39, R-41, R-43, QA-04, QA-11, SEC-10, SEC-16, SEC-17, SEC-19, mais os testes de contrato fake x EF (`IUnitOfWork.ExecuteAsync` sem `CommitAsync`) e o teste de `Set-Cookie` com `__Host-` (todos com `Secure`). Emenda do design: R-26.
- [I] Parte desses itens pode já ter sido resolvida na rodada pós-retro (2026-11-04/05). Precisa ser conferida no `findings-ledger.md` antes de virar tarefa.
- [C] **Backlog**: R-25, R-37, R-40, SEC-07, SEC-08, SEC-09, SEC-13, SEC-14, QA-08, SEC-11 (imagens base por digest), SEC-23 (filtro de log do EF).
- [C] **Risco aceito**: SEC-12, SEC-20, SEC-21.
- [C] **Carried forward da rodada pós-retro, ainda não aplicados**: (1) gate só com CI verde no commit aprovado, com link do run; (2) agentes encerram os processos que sobem e a E2E não reutiliza servidor antigo; (3) QA-21: guardar o log completo na próxima falha intermitente da E2E.
- [C] **Bloqueios de go-live**: validação jurídica (D12/D13), H-1..H-10 manuais, imagens por digest, cliente Google OAuth e provedor de email por ambiente, retenção de logs e backups, alertas, meta de throughput (D14), medição do p95, chaves compartilhadas do Data Protection.
- [E] Histórico git reiniciado: SHAs citados em docs antigos (por exemplo, `96e0919`) só existem na cópia `Deckloom_old`.
- [E] O CI deixou de verificar a guarda de banco e a rastreabilidade (ADR-0017); a rastreabilidade agora é do `aisef-check`, local.

## Desconhecidos
- [D] Hospedagem de produção (gratuita ou paga).
- [D] Estratégia de armazenamento do avatar em escala (hoje no PostgreSQL, ADR-0009).
- [D] Tamanho e papéis da equipe.
- [D] Procedimento local documentado para aplicar migrações (pergunta aberta 1 do `release-notes.md`).
- [D] Se R-18 e R-21 bloqueiam o lançamento; confirmação do ASVS L2 como alvo.
- [D] Não verificados: leitor de tela real (NVDA/VoiceOver), login Google real, certificado SMTP recusado e evento 1008 em log real.
- [D] Primeiro run do CI no repositório novo (público): gitleaks com `.gitleaks.toml` (limpo localmente com `gitleaks dir .` em 2026-11-06) e CodeQL com upload ainda não executados.

## Fontes
`docs/specs/discovery.md`, `docs/specs/conta-jogador/{requirements,retro,release-notes,findings-ledger,security-report}.md`, `docs/adr/0001..0017`, `Database/`, `backend/src/`, `frontend/src/`, `.github/workflows/ci.yml`, `docs/test-strategy.md`, `docs/security-policy.md`, `CHANGELOG.md`.
