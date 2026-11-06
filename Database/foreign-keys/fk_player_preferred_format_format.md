# fk_player_preferred_format_format

Source table/column: accounts.player_preferred_format.format_id
Target table/column: deck_suggestion.format.id
On delete: restrict
On update: no action
Introduced by feature: conta-jogador

FK entre schemas (ADR-0001, ADR-0007). Formato não é apagado; é desativado.
