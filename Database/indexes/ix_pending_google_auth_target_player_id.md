# ix_pending_google_auth_target_player_id

Table: accounts.pending_google_auth
Columns: target_player_id
Type: btree | Unique: no
Introduced by feature: conta-jogador
Reason: suporte à FK `fk_pending_google_auth_target_player` (`ON DELETE CASCADE`): localiza as pendências de vínculo do Jogador alvo na exclusão de conta sem varrer a tabela. Criado pelo EF Core por convenção na FK; documentado com nome explícito.
