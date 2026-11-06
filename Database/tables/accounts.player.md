# accounts.player

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Raiz do agregado `Player` (Jogador): Conta (acesso) + Perfil + Aceite de Termos.

| Column | Type | Constraints | Description |
|---|---|---|---|
| id | uuid | PK (`pk_player`), not null | `PlayerId`, UUIDv7 gerado pela aplicação |
| email | text | not null, unique (`ux_player_email`), check `char_length(email) <= 254` | Email normalizado (trim + minúsculas). Identificador de login |
| password_hash | text | null | Hash versionado (ADR-0003). Nulo para conta criada via Google sem senha local |
| display_name | text | not null | Nome de exibição já normalizado (NFC + trim); 3 a 30 elementos de texto validados no domínio |
| bio | text | null, check `char_length(bio) <= 1200` | Bio (até 280 elementos de texto validados no domínio; o check de bytes/caracteres é só rede de segurança) |
| favorite_tcg | text | not null, default `'mtg'` | Código do TCG favorito (enum extensível; hoje só `mtg`) |
| avatar_kind | text | not null, check in (`'preset'`, `'custom'`) | Tipo do avatar |
| avatar_preset_id | text | null, FK `fk_player_avatar_preset`, index `ix_player_avatar_preset_id` | Preset escolhido, quando `avatar_kind = 'preset'` |
| custom_avatar_key | text | null | Chave aleatória do avatar customizado no armazenamento, quando `avatar_kind = 'custom'` |
| terms_version | text | not null | Versão dos Termos de Uso e da Política de Privacidade aceita (REQ-033) |
| terms_accepted_at | timestamptz | not null | Data/hora do aceite (UTC) |
| adult_declared | boolean | not null, check `adult_declared = true` | Autodeclaração de 18+ (REQ-034), evidência do aceite |
| created_at | timestamptz | not null | Criação da conta |
| updated_at | timestamptz | not null | Última alteração |

Check adicional `ck_player_avatar_consistency`: `(avatar_kind = 'preset' AND avatar_preset_id IS NOT NULL AND custom_avatar_key IS NULL) OR (avatar_kind = 'custom' AND custom_avatar_key IS NOT NULL AND avatar_preset_id IS NULL)`.

Concorrência otimista: coluna de sistema `xmin` do PostgreSQL mapeada como token de concorrência no EF Core.

## Notes
- **PII (LGPD)**: email, password_hash, display_name, bio, preferências, aceite e referência ao avatar. Nenhum dado sensível.
- **Retenção**: enquanto a conta existir. Exclusão de conta = `DELETE` físico, imediato, em transação única (ADR-0006); dependentes saem por cascata.
- Invariante "ao menos um método de autenticação" (senha ou identidade externa) é garantida no domínio, não por constraint (envolve outra tabela).
- Nunca selecionar `password_hash` em consultas de leitura de Perfil.
