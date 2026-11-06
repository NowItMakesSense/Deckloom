# fk_player_session_player

Source table/column: accounts.player_session.player_id
Target table/column: accounts.player.id
On delete: cascade
On update: no action
Introduced by feature: conta-jogador

Cascata encerra todas as sessões na exclusão de conta (REQ-024).
