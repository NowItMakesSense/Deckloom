# Discovery — Deckloom

Status: Concluído | Data: 2026-10-21 | Confirmado por: usuário (dono do projeto (NowItMakesSense))

## Problema e objetivo

Deckloom ajuda jogadores de TCG (começando por Magic: The Gathering) a montar decks melhores e a economizar na compra das cartas que faltam. A partir de cartas que o jogador já possui (ou de uma busca manual) e do formato que ele quer jogar, o sistema sugere um arquétipo/estratégia (mecânica), as cartas que faltam para completar o deck, e **onde comprar essas cartas pelo melhor preço**, comparando lojas externas de TCG.

Diferencial frente ao concorrente direto mais relevante (LigaMagic, que já tem uma feature de "compra por lista" dentro do próprio marketplace dela): o Deckloom não depende de lojas cadastradas em uma plataforma própria — compara preços de **lojas externas e independentes** via scraping, e seu produto principal é o **motor de sugestão de deck/mecânica**, usando a comparação de preço como alavanca de conversão, não como o produto em si.

## Atores

- **Jogador** — único ator com conta/login no sistema. Cria decks, informa cartas (texto ou busca manual), recebe sugestões de arquétipo/cartas faltantes e de onde comprar.
- **Loja** — não é um ator do sistema (sem login, sem dashboard). É uma **entidade de dados** representando uma loja externa de TCG cujo site é raspado para extrair preços de cartas.

## Escopo (MVP)

- TCG suportado: **somente Magic: The Gathering**. Modelo de dados pensado para expandir a outros TCGs depois, mas nada além de Magic é implementado agora.
- Entrada de cartas do jogador: **colar decklist em texto** OU **busca/seleção manual** de cartas — o jogador escolhe livremente qual usar.
- Sugestão de deck: baseada em similaridade/regras (overlap de cartas contra decks de meta/torneio já coletados), não ML treinado no MVP.
- Sugestão de "mecânica": rótulo de arquétipo/estratégia do deck (ex: "Aggro Vermelho/Branco", "Sinergia de Sacrifício"), derivado dos dados de meta/EDHREC — não um motor de análise de texto de cartas próprio.
- Comparação de preço: entre lojas **externas e independentes** (ver Integrações), via scraping periódico — nunca em tempo real durante a requisição do usuário. Preço sempre exibido com timestamp de coleta.
- Histórico de preço: cada coleta gera um **snapshot imutável** (não sobrescreve o anterior), sem expurgo automático no MVP — abre caminho para gráficos de variação/alertas de preço no futuro.
- Conta do jogador: cadastro por **email+senha** ou **login social Google**. Perfil pensado para o produto (ver Entidades).
- Painéis de "decks em alta" e "comandantes preferidos", alimentados pelas fontes de meta/torneio.

## Fora de escopo (MVP)

- Outros TCGs além de Magic (Pokémon, Yu-Gi-Oh, etc.).
- Checkout/pagamento dentro da plataforma — o sistema apenas linka para o site de origem da loja.
- Cadastro, login ou dashboard para lojas.
- Reconhecimento de imagem/foto de cartas ou coleção.
- App mobile nativo — apenas web responsivo.
- Hospedagem/infraestrutura de produção definitiva (ver Questões abertas).

## Integrações (fontes de dados externas, via scraping)

**Preço** (lojas independentes, sem conta na plataforma):
- LigaMagic
- MYP Cards
- XPlace
- Meruru
- Epic Game

**Meta / torneio** (popularidade, arquétipos, decks em alta):
- MTGMelee — fonte primária/oficial de decklists de torneio
- EDHREC — dados específicos de Commander (comandantes populares, cartas por comandante)
- MTGGoldfish
- MTGTop8

**Medidas preventivas de scraping** (para reduzir risco de bloqueio):
- Respeitar `robots.txt` quando possível.
- Rate limiting / delay educado entre requisições a cada fonte.
- Cache agressivo para minimizar frequência de acesso.
- Rotação de User-Agent realista.
- Retry com backoff exponencial em falha.
- Alerta automático quando a estrutura de uma página muda (parser quebrado).
- Preferir API oficial da fonte, quando existir, em vez de HTML scraping.
- Degradação graciosa: se uma fonte falhar, as demais continuam normalmente; o erro é logado e alertado, não derruba a funcionalidade.

**Risco assumido pelo usuário**: scraping de preços públicos pode violar os Termos de Uso de cada site. Risco de negócio aceito explicitamente pelo usuário; recomenda-se revisão jurídica antes de produção (ver Questões abertas).

## Entidades principais (visão de domínio, não o schema final)

- **Jogador**: email, senha (hash, se não for login via Google), nome de exibição, avatar (preset OU upload customizado, ambos no MVP), TCG favorito (enum — hoje só Magic), formato(s) preferido(s) (opcional, multi-valor), bio (opcional).
- **Deck**: pertence a um Jogador; lista de cartas; formato; arquétipo sugerido.
- **Carta**: catálogo de cartas de Magic (nome, edição, etc. — detalhado na fase de arquitetura).
- **Loja**: nome, site de origem, logo (se disponível) — só dado, sem login.
- **SnapshotDePreço**: carta + loja + preço + timestamp de coleta — imutável.
- **Fonte de Meta**: arquétipo/decklist de torneio ou dados de popularidade (Commander), com referência à fonte e timestamp.

## Requisitos não funcionais

- **Performance**: sugestão de deck e comparação de preço respondem em até 3s p95 (dados pré-computados; nunca scraping síncrono na requisição do usuário).
- **Disponibilidade**: meta informal de 99% uptime, sem SLA formal, sem exigência de multi-região.
- **Acessibilidade**: WCAG 2.1 nível AA.
- **Observabilidade**: logs estruturados; alerta quando um scraper falha ou uma fonte fica stale (sem dado novo por X horas).
- **Privacidade/Compliance (LGPD)**: nenhum dado sensível (CPF, pagamento) é coletado — não há checkout na plataforma. Direito ao esquecimento (exclusão de conta e dados pessoais) deve ser suportado.

## Ambiente e infraestrutura

- Sem nuvem/produção definida agora (restrição de custo do usuário). Ambiente de desenvolvimento local via Docker Compose (API .NET + PostgreSQL + frontend React).
- Decisão de hospedagem (gratuita ou paga) fica para mais perto da fase de release (fase 8 do `/orchestrate`).

## Equipe e prazo

- Time (não solo). Sem prazo fixo para o MVP — ritmo próprio.

## Métricas de sucesso do MVP

1. **Taxa de conclusão de sugestão de deck** — % de jogadores que, após informar cartas/formato, chegam a uma sugestão de deck completa.
2. **Taxa de clique em "comprar"** — % de sugestões de compra (link para loja) que são clicadas.
3. **Retenção simples** — % de jogadores que criam/editam um segundo deck em 30 dias.

## Decisões (com justificativa)

| # | Decisão | Justificativa |
|---|---|---|
| 1 | Único ator com login é o Jogador; Loja é só dado | Não há lojas cadastradas/pagantes na plataforma — modelo de negócio do usuário é agregador externo, não marketplace |
| 2 | Preço vem só de lojas externas e independentes, via scraping | Objetivo explícito do usuário: não ter lojas cadastradas dentro da plataforma |
| 3 | MVP cobre somente Magic: The Gathering | Usuário quer desenvolver um TCG por vez |
| 4 | Motor de sugestão por similaridade/regras, não ML | Explicável, sem infraestrutura de treinamento, usa os dados já coletados |
| 5 | Entrada de decklist: texto OU busca manual, à escolha do jogador | Atende aos dois perfis de usuário (quem já tem lista e quem está montando do zero) |
| 6 | Avatar com preset E upload customizado já no MVP | Pedido explícito do usuário, mesmo sabendo do custo extra de infraestrutura de storage |
| 7 | Histórico de preço guardado como snapshots imutáveis | Dado barato de armazenar; habilita features futuras (variação de preço, alertas) sem retrabalho de modelagem |
| 8 | Sem ambiente de nuvem/produção definido agora | Restrição de custo do usuário nesta fase |

## Termos de glossário (também adicionados a `docs/glossary.md`)

Jogador, Loja, Fonte de Preço, Fonte de Meta, Snapshot de Preço, Decklist, Arquétipo, Comandante, Formato, Sugestão de Compra.

## Questões abertas

- Hospedagem/infraestrutura de produção definitiva (gratuita ou paga) — revisitar antes da fase 8 (release) do `/orchestrate`.
- Revisão jurídica formal do risco de scraping dos Termos de Uso das 5 fontes de preço e 4 fontes de meta.
- Estratégia de armazenamento do avatar customizado (ex: blob storage) — decidir na fase de arquitetura.
- Comandos exatos de teste/lint/build (`TEST_CMD`/`LINT_CMD`/`BUILD_CMD` em `CLAUDE.md`) — decidir na fase de arquitetura, junto com a estrutura do repositório (monorepo backend/frontend).
- Tamanho exato da equipe e papéis de cada pessoa — não detalhado nesta entrevista.
- Fontes de preço adicionais (ex: Paladins Games, Two Head Games) — candidatas a avaliar pós-MVP.

## Lista de features propostas (para `/spec-writer` por feature)

1. Cadastro e login (email/senha + Google OAuth) e perfil do jogador (avatar, TCG favorito, formatos preferidos, bio).
2. Entrada de decklist (colar texto) e busca/seleção manual de cartas.
3. Motor de sugestão de deck/arquétipo por similaridade com dados de meta/torneio.
4. Sugestão de cartas faltantes e mecânica/arquétipo do deck.
5. Scraper de preços (LigaMagic, MYP Cards, XPlace, Meruru, Epic Game) com snapshot histórico e timestamp.
6. Scraper de meta/torneio (MTGMelee, EDHREC, MTGGoldfish, MTGTop8).
7. Comparador de preço / sugestão de compra (onde comprar cartas faltantes, por melhor preço, com link de saída).
8. Painel de "decks em alta" e "comandantes preferidos".
9. Gerenciamento de deck salvo (CRUD de decks do jogador).
10. Observabilidade/alertas de scraper (fonte stale, falha, quebra de parser).
