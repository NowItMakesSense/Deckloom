# ADR-0006: Exclusão de conta numa única transação (Jogador, sessões, dados derivados e Decks), arquivos externos removidos após o commit

- Status: Accepted (2026-10-23)
- Date: 2026-10-22
- Deciders: architect (proposta), aprovação do usuário no gate da fase 3 (feature `conta-jogador`)

## Context

REQ-024 e REQ-025: a exclusão confirmada remove imediata e permanentemente os dados pessoais do Jogador, encerra todas as sessões e exclui todos os seus Decks, sem carência (D12). Os Decks pertencem ao contexto Sugestão de Deck (feature 9, ainda não especificada). O domain-draft deixou em aberto (Q2) se a exclusão dos Decks é atômica com a da conta. Pela ADR-0001 há um único banco PostgreSQL.

## Decision

1. O caso de uso `DeleteAccount` executa **uma única transação de banco** que:
   - remove o registro do Jogador (`accounts.player`); as tabelas dependentes do schema `accounts` são removidas por `ON DELETE CASCADE` (identidade externa, formatos preferidos, token de redefinição, sessões, pendências Google com alvo nele e, se adotado, o binário do avatar);
   - apaga os eventos de rate limit com o `account_key_hash` do Jogador;
   - chama a porta `IPlayerDecksPurger.PurgeAllFor(playerId)` **dentro da mesma unidade de trabalho**.
2. Até a feature 9 existir, o adapter de `IPlayerDecksPurger` é um no-op documentado (não há Decks). **Obrigação para a feature 9**: implementar o adapter na mesma transação e criar a FK `deck.player_id -> accounts.player.id ON DELETE CASCADE` como rede de segurança.
3. Se a transação falhar, nada é excluído e o Jogador recebe erro genérico (pode tentar de novo); se for confirmada, a resposta remove o cookie de sessão.
4. Atualização de 2026-10-23: com o avatar guardado no PostgreSQL (ADR-0009), a imagem sai por cascata dentro da transação e este item não se aplica hoje. Fica como regra para qualquer recurso futuro fora do banco: recursos externos ao banco são removidos **depois do commit**, com 3 retentativas; falhas ficam registradas por chave (sem dado pessoal) e um job de varredura remove arquivos órfãos (chaves sem Jogador). A chave de armazenamento é aleatória e não contém dado pessoal.

## Alternatives considered

- **Exclusão assíncrona por evento com outbox**: necessária só se os contextos tivessem bancos separados; aqui adicionaria janela em que a conta sumiu mas os Decks não, sem benefício.
- **Exclusão lógica (soft delete)**: viola D12/REQ-024 (remoção física) e REQ-027 (email livre para novo cadastro).

## Consequences

- A exclusão é tudo-ou-nada no banco; a única janela residual é o arquivo de avatar externo entre o commit e a remoção (segundos, ou até a próxima varredura em caso de falha). Se o usuário escolher guardar o avatar no próprio PostgreSQL, essa janela desaparece.
- A feature 9 herda um contrato explícito (porta + FK em cascata), registrado em `Database/er-diagram.md`.
- Backups do banco podem conter dados de contas excluídas até o fim da sua retenção; a política de retenção de backups é pendência da fase 8 e da validação jurídica de D12.
  - Nota (2026-10-31, DB12): o usuário decidiu a retenção dos backups em 30 dias; após restaurar um backup, as exclusões de conta posteriores a ele são reaplicadas antes de liberar o acesso, com log sem dado pessoal. Requisito documentado (design.md "Backups e LGPD (DB12)"), procedimento não implementado; verificação manual H-10 em `docs/specs/conta-jogador/tasks.md`.
