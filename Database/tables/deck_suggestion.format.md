# deck_suggestion.format

Introduced by feature: conta-jogador (primeira consumidora; **dono: contexto Sugestão de Deck**)
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Catálogo de Formatos (REQ-037, ADR-0007).

| Column | Type | Constraints | Description |
|---|---|---|---|
| id | text | PK (`pk_format`), not null, check `id ~ '^[a-z0-9-]{1,40}$'` | Identificador estável (`standard`, `modern`, `pioneer`, `commander`, `pauper`) |
| name | text | not null | Nome de exibição ("Standard", ...) |
| is_active | boolean | not null, default true | Selecionável. "Remover do catálogo" = desativar |
| sort_order | integer | not null | Ordem de exibição |

## Notes
- Sem PII. Carga inicial: Standard, Modern, Pioneer, Commander, Pauper (todos ativos).
- Nunca apagar fisicamente um Formato referenciado (FK `RESTRICT` a partir de `accounts.player_preferred_format` e, no futuro, de Decks).
- Features futuras de Sugestão de Deck podem acrescentar colunas (ex. regras do formato) sem afetar Conta/Perfil, que só lê `id`, `name`, `is_active`, `sort_order` via `IFormatCatalog`.
