# ix_pending_google_auth_expires_at

Table: accounts.pending_google_auth
Columns: expires_at
Type: btree | Unique: no
Introduced by feature: conta-jogador
Reason: job de limpeza apaga pendências expiradas (TTL de 10 min; dado pessoal transitório).
