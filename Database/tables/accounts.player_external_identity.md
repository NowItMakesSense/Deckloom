# accounts.player_external_identity

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Identidade Externa (`ExternalIdentity`) do Jogador. Hoje só o provedor Google, no máximo uma por Jogador.

| Column | Type | Constraints | Description |
|---|---|---|---|
| player_id | uuid | PK parte 1 (`pk_player_external_identity`), not null, FK `fk_player_external_identity_player`, index `ix_player_external_identity_player_id` (redundante com o prefixo da PK; custo aceito) | Jogador dono |
| provider | text | PK parte 2, not null, check in (`'google'`) | Provedor |
| subject | text | not null, unique com provider (`ux_player_external_identity_provider_subject`) | `sub` estável do Google |
| linked_at | timestamptz | not null | Data/hora do vínculo (cadastro via Google ou REQ-036) |

## Notes
- **PII (LGPD)**: `subject` é identificador pessoal do provedor. Removido por cascata na exclusão de conta (REQ-024).
- Email, nome e foto do Google **não** são guardados aqui (REQ-022).
- Login Google localiza o Jogador primeiro por (`provider`, `subject`).
