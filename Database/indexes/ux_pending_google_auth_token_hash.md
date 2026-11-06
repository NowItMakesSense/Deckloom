# ux_pending_google_auth_token_hash

Table: accounts.pending_google_auth
Columns: token_hash
Type: btree | Unique: yes
Introduced by feature: conta-jogador
Reason: localizar e consumir (uso único) a pendência pelo hash do token do cookie `__Host-dl_pending` (REQ-006, REQ-008, REQ-036).
