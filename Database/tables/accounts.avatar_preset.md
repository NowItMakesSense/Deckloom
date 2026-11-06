# accounts.avatar_preset

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Catálogo de avatares preset, mantido como dado (ADR-0007). MVP: 8 a 12 placeholders neutros.

| Column | Type | Constraints | Description |
|---|---|---|---|
| id | text | PK (`pk_avatar_preset`), not null, check `id ~ '^[a-z0-9-]{1,40}$'` | Identificador estável (ex. `mago-azul`) |
| label | text | not null | Nome acessível em pt-BR (usado no `alt` e na galeria) |
| image_path | text | not null | Caminho relativo do arquivo estático no frontend (ex. `/avatars/presets/mago-azul.svg`) |
| is_default | boolean | not null, default false | Preset atribuído a todo Jogador novo (REQ-022) |
| is_active | boolean | not null, default true | Selecionável na galeria |
| sort_order | integer | not null | Ordem na galeria |

Índice único parcial `ux_avatar_preset_single_default` em (`is_default`) `WHERE is_default = true`.

## Notes
- Sem PII. Carga inicial por migração (gerada como arquivo, aplicada por humano).
- Preset inativo continua válido para Jogadores que já o usam; só deixa de ser selecionável.
