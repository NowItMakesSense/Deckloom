# ADR-0009: Avatar customizado armazenado no PostgreSQL (`bytea`)

- Status: Accepted (2026-10-23)
- Date: 2026-10-23
- Deciders: usuário (escolha da opção a, 2026-10-23), a partir da proposta do architect (feature `conta-jogador`)

## Context

REQ-019 a REQ-021 e REQ-024 exigem armazenar o avatar customizado já reencodado, remover a imagem anterior ao trocar e removê-la na exclusão de conta, de forma imediata e permanente. Não há nuvem definida (restrição de custo, discovery). A imagem final é pequena (WebP 256x256, tipicamente 10-40 KB).

## Decision

- A porta `IAvatarStorage` é implementada por `PostgresAvatarStorage`, que grava em `accounts.avatar_image` (`storage_key` PK aleatória de 128 bits, `player_id` único com FK `ON DELETE CASCADE`, `content_type = image/webp`, `content bytea` de no máximo 256 KB).
- A troca de avatar grava a nova linha, atualiza o Jogador e apaga a linha anterior **na mesma transação** (REQ-021).
- A exclusão de conta remove a imagem por cascata na transação única da ADR-0006, sem etapa pós-commit.
- A imagem é servida por `GET /api/v1/me/avatar/custom?v=<storage_key>` só para o dono, com `Cache-Control: private, max-age=31536000, immutable`.

## Alternatives considered

- **(b) Disco/volume Docker**: simples e sem custo, mas não funciona com mais de uma instância sem volume compartilhado, exige backup separado e remoção pós-commit (risco de órfãos com dado pessoal).
- **(c) Armazenamento de objetos compatível com S3** (MinIO no dev; R2/B2 em produção): escala e permite CDN, mas acrescenta um serviço, custo e conta em provedor, e também exige remoção pós-commit.

## Consequences

- Exclusão e troca de avatar são totalmente atômicas; a janela residual descrita na ADR-0006 deixa de existir para o avatar, e a varredura de órfãos não é necessária.
- O banco cresce ~250 MB para 10.000 Jogadores com avatar; backups incluem as imagens (a retenção de backups continua pendência da fase 8 e da validação jurídica de D12).
  - Nota (2026-10-31, DB12): a retenção dos backups foi decidida pelo usuário em 30 dias, com reaplicação das exclusões de conta após restauração (ver ADR-0006 e design.md "Backups e LGPD (DB12)").
- Toda leitura de avatar passa pela API; aceitável com cache imutável no navegador. Migrar para armazenamento de objetos no futuro é trocar o adapter e copiar os dados.
