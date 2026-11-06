# ADR-0001: Monólito modular em Clean Architecture, um único PostgreSQL com um schema por contexto

- Status: Superseded in part by ADR-0018 (2026-11-06); Accepted (2026-10-23); emendada em 2026-10-24 (ver "Emendas")
- Date: 2026-10-22
- Deciders: architect (proposta), aprovação do usuário no gate da fase 3 (feature `conta-jogador`)

## Context

O Deckloom tem vários contextos delimitados previstos na discovery (Conta/Perfil, Sugestão de Deck, Comparação de Preço) e uma equipe pequena, sem nuvem definida e com restrição de custo. A stack é fixa: backend C#/.NET com EF Core, PostgreSQL, frontend React/TypeScript, ambiente local via Docker Compose. A primeira feature (`conta-jogador`) precisa de regras de domínio testáveis sem IO (TDD estrito) e precisa excluir os Decks do Jogador junto com a conta (REQ-025), dado que os Decks pertencem a outro contexto.

## Decision

1. **Um único processo de API (monólito modular)**, com um módulo por contexto delimitado. Cada módulo segue Clean Architecture em quatro camadas: `Domain` (sem dependências de framework), `Application` (casos de uso e portas), `Infrastructure` (adapters: EF Core, Google, email, imagem, armazenamento) e a camada de interface (endpoints HTTP no host `Api`). Dependências sempre apontam para dentro: `Api -> Infrastructure -> Application -> Domain`; `Api` só conhece `Application` e o ponto de composição (`Infrastructure` registra os adapters via DI).
2. **Um único banco PostgreSQL**, com **um schema por contexto** (`accounts` para Conta/Perfil, `deck_suggestion` para Sugestão de Deck). Cada módulo tem seu próprio `DbContext` mapeado só ao seu schema. Um módulo nunca lê tabelas de outro diretamente pelo seu domínio; o acesso entre contextos passa por portas (camada anticorrupção), como `IFormatCatalog` e `IPlayerDecksPurger` (ver Emenda 1).
3. Chaves estrangeiras entre schemas são permitidas apenas quando protegem integridade de identidade (ex.: Deck -> Jogador, preferência -> Formato) e ficam documentadas em `Database/`.

## Alternatives considered

- **Microsserviços (um serviço por contexto)**: isolamento forte, mas custo operacional (vários deploys, rede, consistência eventual na exclusão de conta) incompatível com o MVP sem nuvem e com equipe pequena.
- **Monólito em camadas único (sem módulos)**: mais simples no início, mas mistura contextos e dificulta extrair o módulo Identidade e Acesso depois (domain-draft).
- **Um banco por contexto**: isolamento forte, mas tornaria a exclusão de conta + Decks (REQ-025) distribuída (outbox, retentativa) sem necessidade.

## Consequences

- A exclusão de conta pode excluir os Decks na mesma transação (ver ADR-0006).
- Extrair um módulo para um serviço no futuro exige trocar os adapters das portas entre contextos, não o domínio.
- É preciso disciplina para que um `DbContext` não mapeie tabelas de outro schema; o `reviewer` verifica isso (ver Emenda 1).
- A estrutura física do repositório (pastas, nomes de projetos) depende da decisão do usuário sobre o layout do monorepo (pendente em `docs/specs/conta-jogador/design.md`).

## Emendas

As emendas abaixo complementam a Decision e as Consequences originais, sem reescrevê-las. Onde houver diferença, a emenda mais recente prevalece apenas no escopo que ela delimita.

### Emenda 1 (2026-10-24): tipo âncora de chave estrangeira entre schemas

- Origem: desvio da FK `accounts.player_preferred_format.format_id -> deck_suggestion.format(id)` (RESTRICT, REQ-046), identificado em T-062 (`docs/specs/conta-jogador/tasks.md`); opção A aprovada pelo usuário.
- Escopo: exceção explícita e controlada ao item 2 da Decision ("Cada módulo tem seu próprio `DbContext` mapeado só ao seu schema") e à Consequence "É preciso disciplina para que um `DbContext` não mapeie tabelas de outro schema". Fora das condições abaixo, a regra original continua valendo sem exceção.

**Decisão.** Um `DbContext` de um módulo pode mapear a tabela de outro schema **somente como tipo âncora de chave estrangeira**, desde que todas as condições sejam atendidas:

1. O tipo âncora contém **apenas** a coluna da chave referenciada (ex.: `Id`); nenhuma outra coluna, propriedade ou navegação de volta.
2. O tipo âncora é `internal`, marcado para exclusão das migrações (`ExcludeFromMigrations`) e **não** tem `DbSet` no `DbContext`.
3. O tipo âncora nunca é consultado nem gravado pelo módulo que o mapeia; o acesso a dados do outro contexto continua passando pelas portas (ex.: `IFormatCatalog`).
4. A tabela é criada e migrada **somente** pelo módulo dono (no caso, Sugestão de Deck, `deck_suggestion.format`, ADR-0007).
5. A ordem de aplicação das migrações garante que a tabela existe antes da FK (a migração de DeckSuggestion é aplicada antes da migração de Accounts que cria `fk_player_preferred_format_format`).

A FK entre schemas continua sujeita ao item 3 da Decision (só para integridade de identidade, documentada em `Database/`).

**Justificativa.** Mantém a FK RESTRICT visível e testável no modelo EF e no SQLite em memória, sem reestruturar módulos nem mover a tabela de Formatos. A ADR-0007 não muda: Sugestão de Deck continua dona do dado e da sua migração.

**Alternativas consideradas.** As opções B e C da análise do architect para T-062 não foram escolhidas pelo usuário (`docs/specs/conta-jogador/tasks.md` menciona as três opções, mas não descreve B e C).

**Consequências.**

- Um teste de arquitetura passa a verificar as condições (1) a (3) para todo tipo âncora mapeado de outro schema. A condição (5) fica registrada na validação manual da aplicação das migrações (nota de T-062 em `docs/specs/conta-jogador/tasks.md`).
- Mudanças na coluna `id` de `deck_suggestion.format` (tipo, nome, chave) passam a exigir revisão do módulo Accounts.
- O `reviewer` continua verificando que nenhum `DbContext` mapeia tabelas de outro schema, exceto tipos âncora que cumpram esta emenda.
