# fk_player_avatar_preset

Source table/column: accounts.player.avatar_preset_id
Target table/column: accounts.avatar_preset.id
On delete: restrict
On update: no action
Introduced by feature: conta-jogador

Presets não são apagados enquanto usados; desativar (`is_active = false`) só os tira da galeria.
