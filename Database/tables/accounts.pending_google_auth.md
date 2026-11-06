# accounts.pending_google_auth

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Estado transitório da aplicação após o retorno do Google (ADR-0004): **Cadastro Google Pendente** (`kind = 'signup'`, conta nova aguardando aceite/maioridade/nome na tela S4) ou **Vínculo Google Pendente** (`kind = 'link'`, aguardando a senha na tela S5).

| Column | Type | Constraints | Description |
|---|---|---|---|
| id | uuid | PK (`pk_pending_google_auth`), not null | Identificador interno |
| token_hash | bytea | not null, unique (`ux_pending_google_auth_token_hash`) | SHA-256 do token opaco do cookie `__Host-dl_pending` |
| kind | text | not null, check in (`'signup'`, `'link'`) | Tipo da pendência |
| google_subject | text | not null | `sub` verificado do Google |
| email | text | not null | Email verificado pelo Google (normalizado) |
| suggested_display_name | text | null | Nome do perfil Google (só para `signup`, pré-preenche S4) |
| target_player_id | uuid | null, FK `fk_pending_google_auth_target_player`, index `ix_pending_google_auth_target_player_id` | Jogador alvo do vínculo (só para `link`) |
| created_at | timestamptz | not null | Criação |
| expires_at | timestamptz | not null, index `ix_pending_google_auth_expires_at` | Criação + 10 min |

Check `ck_pending_google_auth_kind_target`: `(kind = 'link' AND target_player_id IS NOT NULL) OR (kind = 'signup' AND target_player_id IS NULL)`.

## Notes
- **PII (LGPD) transitória**: email, subject e nome do Google por no máximo 10 min. Uso único: a linha é apagada atomicamente na conclusão bem-sucedida, no cancelamento e pelo job de limpeza após expirar.
- Removida por cascata se o Jogador alvo for excluído.
