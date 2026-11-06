# Matriz de privilégios

Introduced by feature: conta-jogador
Status: Approved (emenda de controle de acesso ao banco, 2026-10-23; ADR-0015) - aguardando reaprovação do `design.md`

Fonte da verdade dos privilégios de `deckloom_app` (o único papel de aplicação, ADR-0015). Toda tabela de `Database/tables/` tem exatamente uma linha na tabela "Tabelas"; os `GRANT` das migrações têm de ser iguais a esta matriz. Um teste estático (planejado pelo `planner`) lê esta tabela, então o formato abaixo é **estrito**.

## Formato (estrito, lido por teste)

- Coluna `Tabela`: nome qualificado entre crases, igual ao nome do arquivo em `Database/tables/` sem `.md`.
- Colunas `SELECT`, `INSERT`, `DELETE`: `x` = concedido na tabela inteira; `-` = não concedido.
- Coluna `UPDATE`: `-` = não concedido; `x` = tabela inteira (não usado hoje); `(col_a, col_b)` = concedido **só** nessas colunas (`GRANT UPDATE (col_a, col_b) ON ...`).
- `TRUNCATE`, `REFERENCES` e `TRIGGER` nunca são concedidos a `deckloom_app` e por isso não têm coluna.
- `deckloom_owner` é dono de tudo (todos os privilégios por posse); `deckloom_migrator` e `postgres` não aparecem aqui porque não recebem `GRANT` em tabelas (ver os arquivos de cada papel).

## Banco e schemas

| Objeto | Privilégio de `deckloom_app` | Privilégio de `PUBLIC` | Observação |
|---|---|---|---|
| banco `deckloom` | `CONNECT` | nenhum (`REVOKE ALL ... FROM PUBLIC`) | Sem `CREATE` e sem `TEMPORARY` |
| schema `accounts` | `USAGE` | nenhum | Sem `CREATE`. Dono: `deckloom_owner` |
| schema `deck_suggestion` | `USAGE` | nenhum | Sem `CREATE`. Dono: `deckloom_owner`. Só por causa de `format` (leitura via `IFormatCatalog`) |
| schema `public` | nenhum | nenhum (`REVOKE ALL ON SCHEMA public FROM PUBLIC`) | Não usado pelo Deckloom |
| funções futuras | nenhum por padrão | nenhum (`ALTER DEFAULT PRIVILEGES FOR ROLE deckloom_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC`) | Hoje não há funções (`Database/functions/` vazio) |

## Tabelas

| Tabela | SELECT | INSERT | UPDATE | DELETE | Uso no design (porta / caso de uso) |
|---|---|---|---|---|---|
| `accounts.player` | x | x | (password_hash, display_name, bio, favorite_tcg, avatar_kind, avatar_preset_id, custom_avatar_key, updated_at) | x | `IPlayerRepository`: leitura (`GetById`, `FindBy*`, `ExistsByEmail`), cadastro, `UpdateProfile`, troca de avatar, `ResetPassword` e rehash (`NeedsRehash`), `Remove` na exclusão |
| `accounts.player_external_identity` | x | x | - | x | Login Google por (`provider`, `subject`); vínculo e cadastro Google; `DELETE` porque o EF Core apaga dependentes rastreados do agregado `Player` antes do principal na exclusão |
| `accounts.player_preferred_format` | x | x | - | x | `UpdateProfile` troca o conjunto (apaga e insere; tabela só com chave, nada a atualizar) |
| `accounts.password_reset_token` | x | x | (token_hash, issued_at, expires_at) | x | `IssuePasswordReset` sobrescreve a linha (`UPDATE`); consumo `DELETE ... WHERE token_hash = ... AND expires_at > ...`; job de limpeza |
| `accounts.player_session` | x | x | (last_seen_at, expires_at, revoked_at, reauthenticated_at) | x | `ISessionRepository`: renovação por requisição, revogação (logout, `RevokeAllFor` na redefinição), reautenticação Google; job apaga expiradas/revogadas |
| `accounts.pending_google_auth` | x | x | - | x | `IPendingGoogleAuthStore`: criação, leitura, consumo atômico `DELETE ... RETURNING`, cancelamento, job de limpeza |
| `accounts.access_attempt` | x | x | - | x | `IAccessAttemptStore`: contagem na janela, registro de evento, limpeza de falhas no login bem-sucedido, exclusão por `account_key_hash`, retenção de 24 h. Eventos são só acrescentados: nunca `UPDATE` |
| `accounts.avatar_image` | x | x | - | x | `PostgresAvatarStorage`: grava a nova imagem, lê a do dono, apaga a anterior (troca = inserir + apagar, nunca `UPDATE`) |
| `accounts.avatar_preset` | x | - | - | - | `IAvatarPresetCatalog` (leitura). Carga e mudanças de catálogo só por migração (ADR-0007) |
| `deck_suggestion.format` | x | - | - | - | `IFormatCatalog` (leitura, `FormatCatalogDbContext` sem escrita). Carga e mudanças só por migração do módulo DeckSuggestion (ADR-0007) |

## Sequências e identidade

| Objeto | Privilégio de `deckloom_app` | Justificativa |
|---|---|---|
| sequência implícita de `accounts.access_attempt.id` (`generated always as identity`) | nenhum | No PostgreSQL, o valor de uma coluna de identidade é gerado pelo `INSERT` sem checagem de privilégio na sequência; conceder `USAGE`/`UPDATE` permitiria `nextval`/`setval` diretos sem necessidade. Confirmar na validação manual (README desta pasta) |
| demais tabelas | - | Não há outras sequências: as chaves são UUIDv7 gerados pela aplicação ou identificadores textuais |

## Tabelas de histórico de migração

| Tabela | Privilégio de `deckloom_app` | Justificativa |
|---|---|---|
| `accounts.__ef_migrations_history` | nenhum | Só o dono lê/escreve; a API não consulta migrações pendentes (ADR-0015, item 6) |
| `deck_suggestion.__ef_migrations_history` | nenhum | Idem |

## Notas

- Chaves estrangeiras e cascatas: as verificações de FK e as ações `ON DELETE CASCADE` do PostgreSQL são executadas com os privilégios do dono das tabelas, então `deckloom_app` não precisa de `REFERENCES` nem de `DELETE` nas filhas só para a cascata. Os `DELETE` concedidos acima existem porque o código os executa diretamente.
- Colunas fora da lista de `UPDATE` são imutáveis para a aplicação: `accounts.player` (`id`, `email`, `terms_version`, `terms_accepted_at`, `adult_declared`, `created_at` - evidência do aceite e identificador de login), `accounts.player_session` (`id`, `token_hash`, `player_id`, `remember_me`, `created_at`), `accounts.password_reset_token` (`player_id`). Uma feature futura que mude o email acrescenta a coluna aqui e na migração.
- Concorrência otimista por `xmin` (`accounts.player`) só exige `SELECT` (coluna de sistema lida no `WHERE`/`RETURNING`).
- Feature 9 (Decks): acrescentar a tabela de Decks com o `DELETE` exigido por `IPlayerDecksPurger` (ADR-0006).
