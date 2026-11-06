# accounts.password_reset_token

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Token de Redefinição (`PasswordResetToken`), no máximo um ativo por Jogador (entidade interna do agregado `Player`).

| Column | Type | Constraints | Description |
|---|---|---|---|
| player_id | uuid | PK (`pk_password_reset_token`), not null, FK `fk_password_reset_token_player` | Jogador. A PK garante no máximo um token por Jogador |
| token_hash | bytea | not null, unique (`ux_password_reset_token_token_hash`) | SHA-256 do token de 256 bits enviado por email. O token em claro nunca é persistido |
| issued_at | timestamptz | not null | Emissão |
| expires_at | timestamptz | not null | Emissão + 60 min (REQ-029) |

## Notes
- Emitir novo token sobrescreve a linha (invalida o anterior, REQ-029).
- Uso único: a redefinição bem-sucedida apaga a linha na mesma transação que troca o hash da senha (`DELETE ... WHERE token_hash = $1 AND expires_at > now()`; 1 linha afetada = token válido). Senha curta não apaga (REQ-031).
- Expirados são apagados pelo job horário de limpeza. Removido por cascata na exclusão de conta.
- Vazamento do banco não revela tokens utilizáveis (só hash).
