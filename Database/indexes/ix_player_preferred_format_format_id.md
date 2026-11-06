# ix_player_preferred_format_format_id

Table: accounts.player_preferred_format
Columns: format_id
Type: btree | Unique: no
Introduced by feature: conta-jogador
Reason: suporte à FK `fk_player_preferred_format_format` (verificação de `RESTRICT` ao alterar o catálogo) e a consultas futuras de Jogadores por Formato.
