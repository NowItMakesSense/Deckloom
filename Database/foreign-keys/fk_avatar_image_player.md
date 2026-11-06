# fk_avatar_image_player

Source table/column: accounts.avatar_image.player_id
Target table/column: accounts.player.id
On delete: cascade
On update: no action
Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23; armazenamento decidido na ADR-0009)

Cascata remove o avatar customizado na exclusão de conta, na mesma transação (REQ-024).
