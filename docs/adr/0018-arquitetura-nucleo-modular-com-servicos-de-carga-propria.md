# ADR-0018: Arquitetura OPT-C, com núcleo transacional modular e três serviços independentes

- Status: Accepted (2026-11-06)
- Date: 2026-11-06
- Deciders: usuário (dono do projeto, NowItMakesSense), escolha da opção OPT-C no APR-004 (2026-11-06)
- Supersedes: ADR-0001 em parte (somente a parte de "único deployável"; o restante da ADR-0001 continua valendo dentro do núcleo)

## Context

O usuário redefiniu o produto do Deckloom: lojas passam a ser atores do sistema, o catálogo cobre cinco TCGs (Magic, Pokémon TCG, Yu-Gi-Oh!, One Piece e Lorcana), e o produto ganha IA híbrida, coleção, social e alertas. Ao mesmo tempo, o usuário pediu microsserviços, mantendo o backend em .NET e o frontend em React.

A ADR-0001 (monólito modular, um único processo de API) deixa de cobrir o desenho completo. Como o APR-004 exigia que a decisão fosse explícita, o usuário escolheu entre três opções apresentadas no próprio APR-004 (`.aisef/approvals/APR-004.json`, aprovado em 2026-11-06).

## Decision

1. **Núcleo transacional em .NET modular.** Contém identidade, catálogo, lojas, ofertas, pedidos, coleção, decks e social. A Clean Architecture da ADR-0001 (camadas `Domain`, `Application`, `Infrastructure` e interface, com dependências apontando para dentro) continua valendo dentro do núcleo.
2. **Três serviços independentes**, cada um com deploy e banco próprios:
   - **Sugestão + IA**: sugestão de decks e cartas e recursos de IA híbrida;
   - **Ingestão de meta**: coleta e normalização de dados de meta de fontes externas;
   - **Notificações e alertas**.
3. **Integração por mensageria com padrão outbox no núcleo.** O evento é gravado na mesma transação do caso de uso que o gera e publicado uma única vez. A integração síncrona entre partes acontece por API interna.
4. **Fronteiras verificadas por teste de arquitetura.** O núcleo não referencia projetos de serviço, e os serviços não referenciam projetos do núcleo.
5. **Conta-jogador e infraestrutura transversal ficam no núcleo.** Os serviços novos nascem separados.
6. **Pontos em aberto, não decididos nesta ADR:**
   - escolha concreta do broker de mensageria;
   - provedor de IA;
   - hospedagem de produção (D8).
   Essas escolhas exigem decisão própria do usuário e não são inferidas aqui.

## Alternatives considered

- **OPT-A, microsserviços por contexto** (nove serviços, banco por serviço, API gateway, mensageria com outbox). Rejeitada: custo operacional alto para um time pequeno, fronteiras caras de mudar depois de definidas, e hospedagem de produção (D8) indefinida. A infraestrutura mínima de produção seria bem maior.
- **OPT-B, monólito modular com workers** (uma API, módulos com fronteira forte, workers para ingestão de meta, IA e alertas, comunicação por fila com outbox). Rejeitada: não atende ao pedido explícito de independência dos serviços. Foi considerada a opção de menor custo e de migração sem reescrita.
- **OPT-C, escolhida.** Separa apenas os componentes com carga, custo e risco próprios (IA com provedor externo, ingestão de meta sujeita a bloqueio de scraping, e notificações), e mantém no núcleo as transações de pedido, estoque e lojas, que se beneficiam de consistência conjunta.

## Consequences

- O checkout permanece dentro do núcleo, sem saga distribuída para pedido e estoque.
- A infraestrutura fica moderada: um núcleo, três serviços e um broker. Processos independentes são limitados aos que têm carga ou ritmo diferentes, para conter o custo operacional (mitigação registrada no APR-004).
- Exige observabilidade distribuída (rastreamento entre núcleo e serviços) e testes de contrato para os três serviços.
- A integração entre partes depende de eventos publicados pelo outbox e de chamadas à API interna.
- A conta-jogador e a infraestrutura transversal permanecem no núcleo, sem reescrita. Os testes existentes do backend continuam sendo a base de regressão do núcleo.
- Serviços novos nascem com deploy e banco próprios; não há banco compartilhado entre o núcleo e os serviços.
- Os pontos em aberto (broker, provedor de IA e D8) continuam pendentes e bloqueiam a definição de infraestrutura de produção. Até lá, nenhuma escolha deve ser feita sem decisão explícita do usuário.
- A ADR-0001 continua válida para o núcleo, exceto na parte de "único deployável".

## Emendas

As emendas abaixo complementam a Decision e as Consequences originais, sem reescrevê-las. Onde houver diferença, a emenda mais recente prevalece apenas no escopo que ela delimita.

### Emenda 1 (2026-11-06): broker, outbox no Accounts e obrigações antes de produção

- **Broker decidido: RabbitMQ** (APR-016, 2026-11-06). O ponto em aberto "escolha concreta do broker de mensageria" do item 6 e das Consequences está resolvido. Provedor de IA e hospedagem de produção (D8) continuam em aberto.
- **Sem porta publicada no compose de desenvolvimento** (APR-020, SEC-17): o serviço `rabbitmq` não publica porta no host e é alcançado só pela rede interna do compose, como o `db`. Só `web` e `mail` publicam porta, em loopback.
- **Outbox no módulo Accounts, com publicador padrão no-op/log** até a entrega do adapter real (APR-017). A outbox grava o evento na mesma transação do caso de uso, como previsto no item 3.
- **Obrigação antes do primeiro consumidor ou de produção:** entregar (a) o adapter RabbitMQ do publicador e (b) a retenção e limpeza da outbox, incluindo a remoção das mensagens do jogador na exclusão de conta (LGPD, APR-020).
- **Semântica de entrega at-least-once:** consumidores devem deduplicar pelo id da mensagem.
