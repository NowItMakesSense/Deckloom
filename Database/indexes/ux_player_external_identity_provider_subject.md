# ux_player_external_identity_provider_subject

Table: accounts.player_external_identity
Columns: provider, subject
Type: btree | Unique: yes
Introduced by feature: conta-jogador
Reason: login Google localiza o Jogador por (`provider`, `subject`) (REQ-007); impede o mesmo `subject` vinculado a dois Jogadores (invariante J7).
