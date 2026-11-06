# ux_avatar_preset_single_default

Table: accounts.avatar_preset
Columns: is_default (parcial: `WHERE is_default = true`)
Type: btree | Unique: yes
Introduced by feature: conta-jogador
Reason: garante no máximo um preset padrão (REQ-022); a aplicação falha ao iniciar se não houver nenhum padrão ativo.
