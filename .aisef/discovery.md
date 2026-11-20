# Discovery: Deckloom

Atualizado em 2026-11-20 (AISEF 0.12.0, `.aisef/` reiniciado em 2026-11-19). O discovery de produto anterior ao AISEF está em `docs/specs/discovery.md` e continua valendo. Este arquivo consolida a arquitetura e as decisões desde a reinicialização.

## Visão
Deck builder para jogadores de TCG, começando por Magic: The Gathering (encontrado: `CLAUDE.md`, `docs/specs/discovery.md`).

## Requisitos (com origem)
- Os requisitos de produto estão em `docs/specs/discovery.md` e `docs/specs/conta-jogador/` (investigated).
- Nenhum requisito novo foi levantado nesta rodada.

## Regras de negócio
Ver `docs/glossary.md` e as specs acima. Nada novo nesta rodada.

## Arquitetura atual
- **Estilos detectados** (`aisef_project.py detect`, 2026-11-19) e **conferidos no código**:
  - Clean Architecture: **confirmado**. `Accounts.Domain` só referencia o `SharedKernel`; Application depende de Domain; Infrastructure (EF Core, MailKit, SkiaSharp) depende de Application (`*.csproj`).
  - DDD tático: **confirmado**. Value objects com `Result<T>` (`Accounts.Domain/Email.cs`), `AggregateRoot`, `DomainEvent`.
  - DDD estratégico: encontrado (`Deckloom.SharedKernel`, `docs/glossary.md`, módulos por contexto).
  - Monólito modular: **confirmado**. Os módulos são `Accounts`, `Catalog` e `DeckSuggestion` (ADR-0001, ADR-0018).
  - Microsserviços: **em parte**. `SuggestionService`, `MetaIngestionService` e `NotificationService` são esqueletos (só health check). O desenho vem da ADR-0018 (OPT-C).
  - Fatias verticais no frontend: encontrado (`frontend/src/features/{conta,inicio,landing}` + `shared`).
- **Adoção**: o usuário escolheu adotar `clean`, `ddd-strategic`, `ddd-tactical`, `modular-monolith`, `microservices` e `vertical-slices` (2026-11-19). A ADR-0021 está em Proposed. O registro (`request` → `decide` → `detect --adopt`) está **pendente**: o AISEF 0.12.0 exige `feature_id` em `architecture_decision`, e a correção (PR #21 do AISEF) entra na versão final. O rascunho do pedido está fora do repo, em `deckloom-t2-pendente/APR-001-architecture_decision.json`.
- **Recomendação** REC-SERVICE-BOUNDARIES: banco e contrato explícito por serviço (já previsto na ADR-0018).

## Dívida técnica
- **DT-01: o contrato da porta `IFormatCatalog` vaza um tipo de domínio entre módulos.** `Accounts.Application/IFormatCatalog.cs:5` declara `FormatItem(FormatId Id, string Name)`, e `FormatId` vem de `Accounts.Domain`. Por isso o adapter `DeckSuggestion.Infrastructure/EfFormatCatalog.cs:2` depende de `Deckloom.Accounts.Domain`, o que é uma violação cross-module-internal (achada pelo detect do PR #21 do AISEF). Correção: o contrato da porta usa um tipo da Application de Contas. Decisão do usuário (2026-11-20): só registrar por enquanto; a correção vira feature depois.

## Boas práticas adotadas
18 práticas instaladas em `.claude/rules/aisef/` em 2026-11-20, com o OK do usuário: `common/*` (9), `lang/{typescript,web,csharp}` e `architecture/{ddd-strategic,ddd-tactical,modular-monolith,microservices,clean-architecture,vertical-slices}`.

## Dados
Schema por migrations do EF Core, por módulo (ADR-0019: sem documentação do banco à parte). O banco não foi acessado.

## Integrações
RabbitMQ com outbox (ADR-0018, emenda 1); SMTP via MailKit/Mailpit (ADR-0010); Google OIDC (ADR-0004).

## Segurança
`docs/security-policy.md`; ADRs 0002 a 0006, 0015, 0016 e 0020.

## Testes
`docs/test-strategy.md`; fakes em memória e EF sobre SQLite em memória (ADR-0012); cobertura mínima de 80%.

## Riscos
- Os serviços ainda não têm código: as fronteiras da ADR-0018 ainda não foram exercitadas.

## Desconhecidos
- Provedor de IA e hospedagem de produção (D8), em aberto desde a ADR-0018.

## Fontes
`CLAUDE.md`, `docs/adr/0001`, `0018` e `0021`, `.aisef/architecture.json`, os `*.csproj` de `backend/src`, `frontend/src/features`.
