# fk_player_external_identity_player

Source table/column: accounts.player_external_identity.player_id
Target table/column: accounts.player.id
On delete: cascade
On update: no action
Introduced by feature: conta-jogador

Cascata remove o vínculo Google na exclusão de conta (REQ-024).
