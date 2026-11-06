# Discovery - Deckloom (revisão do produto)

Data: 2026-11-06 | Modo: projeto existente com **revisão de produto** pedida pelo usuário ("o projeto foi criado de forma errada... as features estão erradas, quero repensar algumas decisões") | Alcance confirmado: **repensar tudo, reaproveitando o que servir**.

Classificação: **[C]** confirmado pelo usuário (com data) | **[E]** encontrado (com caminho) | **[I]** inferido (vira pergunta) | **[D]** desconhecido. Só [C] e [E] viram requisito.

## Decisões revistas em 2026-11-06 (confirmadas pelo usuário)
| # | Antes (discovery 2026-10-21) | Agora |
|---|---|---|
| D1/D2 | Loja é só dado; preço por scraping de 5 lojas externas | [C] **Lojas são atores com conta.** Publicam preço e estoque, recebem pedidos/leads, vendem com **checkout na plataforma** e pagam para aparecer (lojas são clientes pagantes) |
| D3 | MVP só Magic | [C] **Vários TCGs desde o MVP**: Magic, Pokémon TCG, Yu-Gi-Oh!, One Piece e/ou Lorcana (ver Desconhecidos) |
| D4 | Sugestão por similaridade/regras, sem ML | [C] **Híbrido**: regras/similaridade como base + IA para explicar/refinar |
| D5 | Decklist colada OU busca manual | [C] Mantém e **acrescenta importação de coleção** (o jogador registra as cartas que possui) |
| D6 | Avatar preset E upload | [C] Sem mudança pedida |
| D7 | Snapshots imutáveis de preço | [C] Não revista (vale para o preço publicado pelas lojas, ver Desconhecidos) |
| D8 | Sem nuvem definida | [C] **Continua indefinido**; decidir com comparação de custos |
| Scraping | Preço e meta por scraping | [C] **Só dados de meta** (MTGMelee, EDHREC, MTGGoldfish, MTGTop8 e equivalentes dos outros TCGs). Preço vem das lojas parceiras |
| Stack | .NET + EF + PostgreSQL, React, monolito modular (ADR-0001) | [C] Mantém .NET + React; usuário quer **microsserviços**. É mudança de arquitetura aprovada (ADR-0001), portanto exige `architecture_decision` com cenários antes do blueprint (regra 3) |
| Prioridade | Uma feature por vez, só Magic | [C] **"Tudo junto, todos os TCGs"** no MVP |
| Features novas | - | [C] **Social/comunidade** e **alertas de preço** |

## Visão (revista)
- [C] Plataforma para jogadores de vários TCGs: montar decks a partir da coleção, receber sugestões (regras + IA) e comprar cartas de **lojas parceiras** com checkout na própria plataforma. Inclui comunidade e alertas de preço.
- [C] Atores com login: **Jogador** e **Loja**.
- [D] Proposta de valor e diferencial frente a LigaMagic/TCGplayer/Cardmarket depois da mudança para marketplace (o diferencial antigo era "não depender de lojas cadastradas").
- [D] Métricas de sucesso do novo MVP (as antigas eram de agregador: clique em "comprar").

## Features (candidatas, a validar em Feature Contracts)
Lista montada só com o que foi confirmado. Nenhuma tem requisito detalhado ainda.
- Conta do jogador: [E] já implementada (`conta-jogador`, REQ-001..049). [I] Precisa de revisão para "vários TCGs" (o campo TCG favorito hoje é enum só Magic) e para conviver com a conta de loja.
- [C] Conta e painel da loja (cadastro, publicação de preço e estoque).
- [C] Catálogo de cartas multi-TCG.
- [C] Coleção do jogador (importação).
- [C] Decks: entrada por texto ou busca, CRUD (herdado das antigas F2/F9).
- [C] Sugestão híbrida de deck/cartas (antigas F3/F4).
- [C] Ingestão de meta por scraping (antiga F6) e painéis de decks em alta/comandantes (F8). [I] "Comandantes" é conceito de Magic; equivalente nos outros TCGs a definir.
- [C] Carrinho, checkout e pedidos com lojas; leads/pedidos para a loja.
- [C] Monetização de lojas (pagar para aparecer).
- [C] Alertas de preço.
- [C] Social/comunidade.
- Sai: [C] scraper de preços de lojas externas (antiga F5); comparador por scraping (F7) vira comparação entre lojas parceiras.

## O que se reaproveita do código atual
- [E] Infra transversal da API: autenticação por sessão com cookie `__Host-`, CSRF, rate limit progressivo, redação de logs, problem details, proxy confiável (ADR-0002, 0005, 0016), papéis de banco (ADR-0015), CI completo.
- [E] Módulo `Accounts` (jogador, login email/Google, perfil, avatar, exclusão LGPD) e design system v0.1.
- [I] A divisão em microsserviços muda onde isso fica (provável serviço de identidade). Depende da decisão de arquitetura.

## Arquitetura atual (fatos)
- [E] Monolito modular .NET 10 com Clean Architecture (ADR-0001), módulos `Accounts` e `DeckSuggestion`, PostgreSQL 17 com schemas por módulo, React 19 + Vite. Detalhes: `backend/src/` (migrations em `backend/src/Modules/*/Infrastructure/Migrations`), `docs/adr/`.

## Dados
- [E] Schema `accounts` (9 tabelas) e `deck_suggestion.format`, papéis `deckloom_owner/migrator/app`. Fonte: migrations em `backend/src/Modules/*/Infrastructure/Migrations` e ADR-0015/ADR-0019.
- [D] Estado real de qualquer banco: não consultado (T5).
- [I] Novas entidades: Loja, Usuário da loja, Oferta (preço + estoque), Pedido, Pagamento, Coleção, Carta por TCG, Alerta, Post/Comentário. Detalhamento só no blueprint.

## Integrações
- [E] Google OIDC, SMTP.
- [C] Scraping de fontes de meta (com as medidas preventivas do discovery antigo: robots.txt, rate limit, cache, backoff, alerta de parser quebrado).
- [C] Provedor de pagamento (exigido pelo checkout). [D] Qual (Stripe, Mercado Pago, Pagar.me...).
- [C] IA para a sugestão híbrida. [D] Provedor e modelo.
- [D] Fontes de importação de coleção (CSV, Moxfield, Archidekt, ManaBox, TCGplayer...).
- [D] Fontes de catálogo por TCG (ex.: Scryfall para Magic; equivalentes para os outros).

## Segurança e compliance
- [E] Baseline `docs/security-policy.md`; LGPD.
- [I] Checkout com lojas como vendedoras aumenta muito o escopo regulatório: dados de pagamento (PCI-DSS, mitigado com checkout hospedado do provedor), nota fiscal, CDC (direito de arrependimento), split de pagamento e KYC de lojas, dados pessoais de compradores compartilhados com lojas (LGPD: controlador x operador). Precisa de confirmação e de assessoria jurídica.

## Riscos
- [I] **Escopo do MVP muito grande** (decisão do usuário: "tudo junto, todos os TCGs"): marketplace com pagamento + 5 TCGs + IA + coleção + social + alertas. Em comparação, a `conta-jogador` sozinha teve 184 tarefas. Risco alto de prazo e de qualidade. Registrado aqui; a decisão continua do usuário.
- [I] **Microsserviços** para um time pequeno: custo operacional (deploy, observabilidade, consistência distribuída, pagamentos com saga/outbox) bem maior que o do monolito modular. Vai para `architecture_decision` com cenários.
- [C] Risco jurídico do scraping (agora só meta) segue aceito pelo usuário, com revisão jurídica antes de produção.
- [E] Pendências herdadas da `conta-jogador` (Lows de "corrigir agora", backlog, bloqueios de go-live) seguem em `docs/specs/conta-jogador/retro.md` e `release-notes.md`. A prioridade delas precisa ser revista com o novo plano.
- [C] **Perda de verificacao automatica de privilegios** (FEAT-SEM-DATABASE-DOCS, RISK-FEAT-SEM-DATABASE-DOCS-1, severidade media, risco aceito pelo usuario em 2026-11-06; ADR-0019): sem testes que conferem os GRANT das migrations contra a matriz, um privilegio excessivo do `deckloom_app` nao e detectado automaticamente. A revisao humana das migrations continua obrigatoria antes de aplicar.

## Respostas da 2ª rodada (2026-11-06, confirmadas pelo usuário)
- [C] TCGs do MVP: **Magic, Pokémon TCG, Yu-Gi-Oh!, One Piece e Lorcana** (5).
- [C] Fluxo de dinheiro: **pagamento direto à loja**. O checkout usa a conta de pagamento da própria loja; o Deckloom não toca o dinheiro e cobra das lojas só a assinatura/destaque. Isso reduz bastante o escopo fiscal e de PCI do Deckloom. [D] O provedor (ou provedores) que as lojas usam e como o checkout se integra a cada um.
- [C] IA: **explica sugestões, sugere cartas, monta deck a partir de pedido em linguagem natural e funciona como assistente/chat** (deck, regras, coleção).
- [C] Arquitetura: preparar a **decisão formal** (APR-004) com cenários antes de qualquer blueprint.

## Desconhecidos (perguntas para as próximas rodadas)
1. ~~TCGs exatos~~ (respondido).
2. Provedor(es) de pagamento das lojas e modelo de integração do checkout (link de pagamento, conta conectada, gateway por loja).
3. "Pagar para aparecer": assinatura, destaque pago, ou ambos? Preço?
4. Provedor/modelo de IA, limites de custo por usuário e salvaguardas do chat.
5. Fontes de importação de coleção e de catálogo por TCG.
6. Escopo de social: compartilhar decks, seguir, comentar, curtir?
7. Alertas de preço: canal (email, push, in-app) e gatilho.
8. Proposta de valor, diferencial e métricas do novo MVP.
9. Hospedagem (D8) e orçamento.
10. Destino do histórico de preço (D7) com preço vindo das lojas.

## Fontes
Respostas do usuário em 2026-11-06 nesta sessão; `docs/specs/discovery.md` (decisões antigas); `docs/specs/conta-jogador/*`; `docs/adr/0001..0017`; `backend/src/` (incluindo as migrations).
