# Deckloom - entity-relationship diagram (complete)

Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23) | Last updated: 2026-10-23 | Updated by: architect (feature `conta-jogador`)

This is the **complete, authoritative** ER diagram for the whole project. It is extended, never replaced, each time a feature touches the database (see `docs/specs/<feature>/design.md` -> "Feature diagram"). A single feature's own diagram only ever shows the entities/tables *that feature* touches; this file is the only place the full picture lives.

Schemas (ADR-0001): `accounts` (contexto Conta/Perfil), `deck_suggestion` (contexto Sugestão de Deck). Novos contextos ganham seu próprio schema.

## Entities and relationships

```
accounts.player (id)
  - id: uuid [PK pk_player, UUIDv7]
  - email: text [not null, unique ux_player_email, normalizado]
  - password_hash: text [null; nulo = conta sem senha local]
  - display_name: text [not null]
  - bio: text [null]
  - favorite_tcg: text [not null, default 'mtg']
  - avatar_kind: text [not null, 'preset' | 'custom']
  - avatar_preset_id: text [null, FK fk_player_avatar_preset, index ix_player_avatar_preset_id]
  - custom_avatar_key: text [null]
  - terms_version: text [not null]
  - terms_accepted_at: timestamptz [not null]
  - adult_declared: boolean [not null, = true]
  - created_at, updated_at: timestamptz [not null]
  relationships:
  - accounts.avatar_preset 1-N accounts.player via avatar_preset_id [on delete: restrict]
  - accounts.player 1-0..1 accounts.player_external_identity (por provedor) via player_id [on delete: cascade]
  - accounts.player 1-N accounts.player_preferred_format via player_id [on delete: cascade]
  - accounts.player 1-0..1 accounts.password_reset_token via player_id [on delete: cascade]
  - accounts.player 1-N accounts.player_session via player_id [on delete: cascade]
  - accounts.player 1-N accounts.pending_google_auth via target_player_id [on delete: cascade]
  - accounts.player 1-0..1 accounts.avatar_image via player_id [on delete: cascade]

accounts.player_external_identity (player_id, provider)
  - player_id: uuid [PK, FK fk_player_external_identity_player, index ix_player_external_identity_player_id (redundante com a PK)]
  - provider: text [PK, 'google']
  - subject: text [not null, unique (provider, subject) ux_player_external_identity_provider_subject]
  - linked_at: timestamptz [not null]

accounts.player_preferred_format (player_id, format_id)
  - player_id: uuid [PK, FK fk_player_preferred_format_player]
  - format_id: text [PK, FK fk_player_preferred_format_format, index ix_player_preferred_format_format_id]
  relationships:
  - deck_suggestion.format 1-N accounts.player_preferred_format via format_id [on delete: restrict]

accounts.password_reset_token (player_id)
  - player_id: uuid [PK, FK fk_password_reset_token_player]
  - token_hash: bytea [not null, unique ux_password_reset_token_token_hash]
  - issued_at, expires_at: timestamptz [not null]

accounts.player_session (id)
  - id: uuid [PK]
  - token_hash: bytea [not null, unique ux_player_session_token_hash]
  - player_id: uuid [not null, FK fk_player_session_player, index ix_player_session_player_id]
  - remember_me: boolean [not null]
  - created_at, last_seen_at: timestamptz [not null]
  - expires_at: timestamptz [not null, index ix_player_session_expires_at]
  - revoked_at, reauthenticated_at: timestamptz [null]

accounts.pending_google_auth (id)
  - id: uuid [PK]
  - token_hash: bytea [not null, unique ux_pending_google_auth_token_hash]
  - kind: text ['signup' | 'link']
  - google_subject, email: text [not null]
  - suggested_display_name: text [null]
  - target_player_id: uuid [null, FK fk_pending_google_auth_target_player, index ix_pending_google_auth_target_player_id]
  - created_at: timestamptz; expires_at: timestamptz [index ix_pending_google_auth_expires_at]

accounts.access_attempt (id)
  - id: bigint [PK, identity]
  - action: text ['login' | 'password_reset_request' | 'google_link' | 'account_deletion' | 'registration']
  - ip_key_hash: bytea [not null]
  - account_key_hash: bytea [null]
  - attempted_at: timestamptz [not null]
  indexes: ix_access_attempt_ip_key, ix_access_attempt_account_key, ix_access_attempt_attempted_at
  relationships: nenhuma FK (chaves HMAC existem mesmo sem conta - anti-enumeração)

accounts.avatar_preset (id)
  - id: text [PK, slug]
  - label, image_path: text [not null]
  - is_default: boolean [unique parcial ux_avatar_preset_single_default WHERE is_default]
  - is_active: boolean; sort_order: integer

accounts.avatar_image (storage_key)   -- avatar customizado no PostgreSQL (ADR-0009)
  - storage_key: text [PK]
  - player_id: uuid [not null, unique ux_avatar_image_player_id, FK fk_avatar_image_player]
  - content_type: text ['image/webp']
  - content: bytea [<= 256 KB]
  - created_at: timestamptz

deck_suggestion.format (id)   -- dono: contexto Sugestão de Deck
  - id: text [PK, slug]
  - name: text [not null]
  - is_active: boolean [not null]
  - sort_order: integer [not null]
```

### Contratos futuros já assumidos (ainda não são tabelas)

- **Deck (feature 9, contexto Sugestão de Deck)**: deverá ter `player_id uuid not null` com FK para `accounts.player.id` **`ON DELETE CASCADE`** e implementar `IPlayerDecksPurger` na mesma transação da exclusão de conta (ADR-0006, REQ-025). Deck referencia Formato por `deck_suggestion.format.id` com `RESTRICT`.

## Added by feature

| Feature | Objects added/changed | Date |
|---|---|---|
| conta-jogador | Schemas `accounts`, `deck_suggestion`. Tables: `accounts.player`, `accounts.player_external_identity`, `accounts.player_preferred_format`, `accounts.password_reset_token`, `accounts.player_session`, `accounts.pending_google_auth`, `accounts.access_attempt`, `accounts.avatar_preset`, `deck_suggestion.format`, `accounts.avatar_image`. PKs: `pk_player`, `pk_player_external_identity`, `pk_player_preferred_format`, `pk_password_reset_token`, `pk_player_session`, `pk_pending_google_auth`, `pk_access_attempt`, `pk_avatar_preset`, `pk_format`, `pk_avatar_image`. FKs: `fk_player_avatar_preset`, `fk_player_external_identity_player`, `fk_player_preferred_format_player`, `fk_player_preferred_format_format`, `fk_password_reset_token_player`, `fk_player_session_player`, `fk_pending_google_auth_target_player`, `fk_avatar_image_player`. Indexes: `ux_player_email`, `ux_player_external_identity_provider_subject`, `ux_password_reset_token_token_hash`, `ux_player_session_token_hash`, `ix_player_session_player_id`, `ix_player_session_expires_at`, `ux_pending_google_auth_token_hash`, `ix_pending_google_auth_expires_at`, `ix_access_attempt_ip_key`, `ix_access_attempt_account_key`, `ix_access_attempt_attempted_at`, `ix_player_preferred_format_format_id`, `ux_avatar_preset_single_default`, `ux_avatar_image_player_id`, `ix_pending_google_auth_target_player_id`, `ix_player_external_identity_player_id`, `ix_player_avatar_preset_id` (os três criados por convenção do EF nas FKs, documentados em 2026-10-24; `ix_player_external_identity_player_id` é redundante com a PK e mantido como custo aceito). Packages/functions: nenhum. Todos Draft até a aprovação do design. | 2026-10-22 (avatar_image confirmado em 2026-10-23) |
| conta-jogador (emenda de controle de acesso, ADR-0015) | Roles: `postgres` (bootstrap), `deckloom_owner` (NOLOGIN, dono dos schemas `accounts`/`deck_suggestion` e de todos os objetos), `deckloom_migrator` (login humano para migrações), `deckloom_app` (único papel da API) e `roles/privilege-matrix.md`. Nenhuma tabela, coluna, chave ou índice alterado. Draft até a reaprovação do design. | 2026-10-23 |

Keep this table in sync with `tables/`, `indexes/`, `foreign-keys/`, `primary-keys/`, `packages/`, `functions/`, `roles/` - it is the changelog that lets a reader trace which feature introduced which object.
