# accounts.avatar_image

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23). Armazenamento do avatar no PostgreSQL decidido pelo usuário em 2026-10-23 (ADR-0009).

Binário do avatar customizado já redimensionado e reencodado pelo SkiaSharp (WebP 256x256, sem EXIF/XMP/ICC, ADR-0011).

| Column | Type | Constraints | Description |
|---|---|---|---|
| storage_key | text | PK (`pk_avatar_image`), not null | Chave aleatória (128 bits, base64url), igual a `accounts.player.custom_avatar_key` |
| player_id | uuid | not null, unique (`ux_avatar_image_player_id`), FK `fk_avatar_image_player` | Dono (no máximo uma imagem por Jogador) |
| content_type | text | not null, check in (`'image/webp'`) | Formato de saída do reencode |
| content | bytea | not null, check `octet_length(content) <= 262144` | Imagem 256x256 reencodada (tipicamente 10-40 KB) |
| created_at | timestamptz | not null | Criação |

## Notes
- **PII (LGPD)**: imagem enviada pelo Jogador. Removida por cascata na exclusão de conta, na mesma transação (ADR-0006, ADR-0009); não há etapa pós-commit nem varredura de órfãos.
- Substituição ou troca por preset apaga a linha anterior na mesma transação que atualiza `accounts.player` (REQ-021).
- Lida só pelo dono via `GET /api/v1/me/avatar/custom` (cache privado imutável no navegador).
- Volume estimado: ~250 MB para 10.000 Jogadores com avatar customizado.
