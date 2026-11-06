# accounts.player_preferred_format

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Formatos Preferidos do Jogador (conjunto, zero ou mais).

| Column | Type | Constraints | Description |
|---|---|---|---|
| player_id | uuid | PK parte 1 (`pk_player_preferred_format`), not null, FK `fk_player_preferred_format_player` | Jogador |
| format_id | text | PK parte 2, not null, FK `fk_player_preferred_format_format`, index `ix_player_preferred_format_format_id` | Formato do catálogo (`deck_suggestion.format`) |

## Notes
- **PII (LGPD)**: preferência de jogo. Removida por cascata na exclusão de conta.
- Referências a Formato inativo são mantidas e ignoradas na leitura; o salvamento do Perfil as preserva (ADR-0007).
- FK entre schemas aceita pela ADR-0001.
