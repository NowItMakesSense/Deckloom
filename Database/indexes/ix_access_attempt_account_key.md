# ix_access_attempt_account_key

Table: accounts.access_attempt
Columns: account_key_hash, attempted_at
Type: btree | Unique: no
Introduced by feature: conta-jogador
Reason: contar eventos por conta na janela de 15 min (REQ-032); apagar eventos da conta no login bem-sucedido e na exclusão de conta.
