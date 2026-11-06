# ix_player_external_identity_player_id

Table: accounts.player_external_identity
Columns: player_id
Type: btree | Unique: no
Introduced by feature: conta-jogador
Reason: criado pelo EF Core por convenção na FK `fk_player_external_identity_player`. É redundante: a PK `pk_player_external_identity` (`player_id`, `provider`) já cobre buscas e a cascata por `player_id` pelo seu prefixo. Mantido no modelo como custo aceito (escrita extra pequena, tabela com no máximo uma linha por Jogador); não é removido.
