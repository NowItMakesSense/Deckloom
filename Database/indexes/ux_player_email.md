# ux_player_email

Table: accounts.player
Columns: email
Type: btree | Unique: yes
Introduced by feature: conta-jogador
Reason: login e verificação de cadastro por email (REQ-002, REQ-004); reforça no armazenamento a unicidade do Email contra corridas (invariante J1). Violação no `INSERT` é traduzida em `email_unavailable`. O email já chega normalizado (minúsculas), por isso não se usa `citext` nem índice de expressão.
