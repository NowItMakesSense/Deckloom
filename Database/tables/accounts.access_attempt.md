# accounts.access_attempt

Introduced by feature: conta-jogador
Status: Approved (design.md de `conta-jogador` aprovado em 2026-10-23)

Eventos contáveis para o Atraso Progressivo (REQ-032, ADR-0005).

| Column | Type | Constraints | Description |
|---|---|---|---|
| id | bigint | PK (`pk_access_attempt`), generated always as identity | Identificador |
| action | text | not null, check in (`'login'`, `'password_reset_request'`, `'google_link'`, `'account_deletion'`, `'registration'`) | Ação contada (`registration` = cadastro recusado por email em uso, só chave de IP) |
| ip_key_hash | bytea | not null | HMAC-SHA256(segredo, IP do cliente) |
| account_key_hash | bytea | null | HMAC-SHA256(segredo, email normalizado), exista ou não a conta |
| attempted_at | timestamptz | not null | Momento do evento |

Índices: `ix_access_attempt_ip_key` (`ip_key_hash`, `attempted_at`), `ix_access_attempt_account_key` (`account_key_hash`, `attempted_at`), `ix_access_attempt_attempted_at` (`attempted_at`).

## Notes
- **PII (LGPD)**: IP e email são dados pessoais; aqui só existem como HMAC com segredo do servidor (pseudonimização). Sem FK para `player` (a chave existe mesmo sem conta, anti-enumeração).
- **Retenção (REQ-047)**: cerca de 24 h (job horário apaga `attempted_at < now() - interval '24 hours'`). Na exclusão de conta, a aplicação recalcula `HMAC(segredo, email normalizado do Jogador)` antes de apagar o Jogador e apaga todas as linhas com esse `account_key_hash`, na mesma transação.
- Linhas sem `account_key_hash` (ação `registration`, só IP) e linhas de outras chaves de conta vindas do mesmo IP não são atribuíveis a um Jogador; saem só pela retenção de 24 h.
- Trocar o segredo HMAC torna as linhas antigas não localizáveis pela exclusão: a troca exige apagar todas as linhas da tabela no mesmo procedimento.
