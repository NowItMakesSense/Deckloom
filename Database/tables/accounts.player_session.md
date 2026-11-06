# accounts.player_session

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Agregado `Session` (ADR-0002).

| Column | Type | Constraints | Description |
|---|---|---|---|
| id | uuid | PK (`pk_player_session`), not null | `SessionId` interno (UUIDv7), usado em logs |
| token_hash | bytea | not null, unique (`ux_player_session_token_hash`) | SHA-256 do token opaco do cookie `__Host-dl_session` |
| player_id | uuid | not null, FK `fk_player_session_player`, index `ix_player_session_player_id` | Jogador |
| remember_me | boolean | not null | "Lembrar-me" (REQ-035) |
| created_at | timestamptz | not null | Criação |
| last_seen_at | timestamptz | not null | Última requisição autenticada (inatividade de 30 min, REQ-012) |
| expires_at | timestamptz | not null, index `ix_player_session_expires_at` | Com lembrar-me: criação + 30 dias. Sem: `last_seen_at` + 30 min (atualizado a cada requisição) |
| revoked_at | timestamptz | null | Revogação (logout, redefinição de senha) |
| reauthenticated_at | timestamptz | null | Momento da última reautenticação Google bem-sucedida nesta sessão (válida por 5 min para exclusão de conta) |

## Notes
- Sem IP nem user agent (minimização LGPD).
- Sessões expiradas ou revogadas são apagadas pelo job horário de limpeza. Todas as sessões do Jogador são removidas por cascata na exclusão de conta (REQ-024).
