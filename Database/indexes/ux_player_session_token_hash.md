# ux_player_session_token_hash

Table: accounts.player_session
Columns: token_hash
Type: btree | Unique: yes
Introduced by feature: conta-jogador
Reason: autenticação de cada requisição pelo hash do token do cookie (REQ-010, REQ-011).
