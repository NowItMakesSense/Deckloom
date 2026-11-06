# ux_password_reset_token_token_hash

Table: accounts.password_reset_token
Columns: token_hash
Type: btree | Unique: yes
Introduced by feature: conta-jogador
Reason: localizar e consumir o token na redefinição (REQ-030, REQ-031) com `DELETE ... WHERE token_hash = $1`.
