# ADR-0021: Estilos arquiteturais cobrados pelo AISEF

- Status: Proposed
- Date: 2026-11-19
- Deciders: usuário (dono do projeto, NowItMakesSense)
- Relaciona: ADR-0001 (Clean Architecture no núcleo), ADR-0018 (OPT-C: núcleo modular + três serviços)

## Context

O AISEF 0.12.0 detecta o estilo arquitetural do repositório (`aisef_project.py detect`) e, depois da adoção explícita, faz o `architecture-reviewer` cobrar esse estilo em cada entrega. A detecção, conferida no código em 2026-11-19, encontrou:

- **Clean Architecture**: `Accounts.Domain` referencia só o `SharedKernel`; Application depende de Domain; Infrastructure (EF Core, MailKit, SkiaSharp) depende de Application. `aisef_detect.py deps`: 0 violações.
- **DDD tático**: value objects com `Result<T>` (ex.: `Email`), `AggregateRoot`, `DomainEvent`.
- **DDD estratégico**: `SharedKernel`, módulos por contexto e `docs/glossary.md`.
- **Monólito modular**: módulos `Accounts`, `Catalog` e `DeckSuggestion`; a integração entre módulos é por porta (ex.: `EfFormatCatalog` implementa `IFormatCatalog` de Contas).
- **Microsserviços**: `SuggestionService`, `MetaIngestionService` e `NotificationService`, por enquanto só com health check (ADR-0018).
- **Fatias verticais no frontend**: `frontend/src/features/{conta,inicio,landing}` + `shared`.

## Decision

Adotar no AISEF os estilos `clean`, `ddd-strategic`, `ddd-tactical`, `modular-monolith`, `microservices` e `vertical-slices`, que são o desenho já decidido nas ADR-0001 e ADR-0018.

## Alternatives considered

- **Adiar `microservices`** até o primeiro serviço ganhar código. Rejeitada: a fronteira (banco próprio, contrato explícito, sem referência ao núcleo) deve ser cobrada desde a primeira linha.
- **Adotar só o núcleo do backend** (`clean`, `ddd-tactical`, `modular-monolith`). Rejeitada: o frontend e os serviços ficariam sem revisão de estilo.

## Consequences

- O `architecture-reviewer` passa a cobrar regra de dependência, fronteira entre módulos, fronteira dos serviços e organização por feature no frontend.
- As práticas de `.aisef/architecture.json` (`practices`) podem ser instaladas em `.claude/rules/aisef/` (T3).
- A recomendação REC-SERVICE-BOUNDARIES (banco e contrato por serviço) fica valendo para os três serviços.
