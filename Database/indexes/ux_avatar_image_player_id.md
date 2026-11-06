# ux_avatar_image_player_id

Table: accounts.avatar_image
Columns: player_id
Type: btree | Unique: yes
Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23; armazenamento decidido na ADR-0009)
Reason: no máximo uma imagem customizada por Jogador; leitura do avatar do Jogador autenticado; apoia a cascata da exclusão de conta.
