# ix_access_attempt_ip_key

Table: accounts.access_attempt
Columns: ip_key_hash, attempted_at
Type: btree | Unique: no
Introduced by feature: conta-jogador
Reason: contar eventos por IP na janela de 15 min para o atraso progressivo (REQ-032, ADR-0005).
