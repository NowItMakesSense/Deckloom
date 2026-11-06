# ix_player_avatar_preset_id

Table: accounts.player
Columns: avatar_preset_id
Type: btree | Unique: no
Introduced by feature: conta-jogador
Reason: suporte à FK `fk_player_avatar_preset` (`ON DELETE RESTRICT`, para `accounts.avatar_preset`): a verificação de RESTRICT ao excluir ou alterar um preset localiza os Jogadores que o referenciam sem varrer a tabela. Criado pelo EF Core por convenção na FK; documentado com nome explícito.
