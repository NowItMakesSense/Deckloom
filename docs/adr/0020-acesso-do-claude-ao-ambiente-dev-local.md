# ADR-0020: Acesso do Claude ao ambiente de desenvolvimento local (banco do compose e `.env`)

- Status: Accepted (2026-11-06)
- Date: 2026-11-06
- Deciders: usuário (dono do projeto, NowItMakesSense), decisão arquitetural APR-021 (opção OPT-DEV-LOCAL)
- Supersedes: em parte a regra "nenhum agente acessa o banco nem lê segredos" (regra 2 do `CLAUDE.md` e do `AGENTS.md`, seção "Non-negotiable" da `docs/security-policy.md`, "prime directive 2" citada na ADR-0015), somente para o ambiente de desenvolvimento local.

## Context
Desde a `conta-jogador`, nenhum agente podia acessar o banco do sistema nem ler `.env`, chaves ou connection strings, em nenhum ambiente. Em 2026-11-06, durante a FEAT-PLATAFORMA, o `.env.example` precisou de duas variáveis novas do RabbitMQ e nem o orquestrador nem os agentes conseguiam editá-lo. O usuário pediu acesso amplo ("banco, repositório, etc.").

Hoje só existe o ambiente de desenvolvimento local: o PostgreSQL do `docker-compose.yml` e o `.env` local, fora do git. Não há staging nem produção (hospedagem D8 indefinida). O repositório é público e o projeto segue a LGPD.

## Decision
1. No ambiente **dev local**, o Claude (orquestrador da sessão principal) pode:
   - ler e editar `.env` e `.env.example`;
   - acessar o PostgreSQL do compose de desenvolvimento (`psql`, `docker compose exec`, `dotnet ef database update`, `pg_dump`/`pg_restore`) para aplicar migrations, conferir schema e fazer smoke local.
2. Cada operação em banco ou arquivo sensível continua passando pelo Execution Gateway do AISEF e pela confirmação nativa do Claude Code (aprovação por operação). Operações destrutivas (DROP, TRUNCATE, apagar volume) exigem aprovação reforçada.
3. **Staging e produção continuam proibidos** para qualquer agente, em qualquer forma, até uma nova ADR.
4. Segredos nunca vão para logs, relatórios, Execution Reports, painel, commits, PRs nem mensagens: o Claude cita só o nome da variável. `.env` continua fora do git.
5. Agentes especialistas (subagentes) **não** ganham `sensitive_files` nem acesso ao banco no teto dos Agent Contracts; quando uma tarefa precisar, o orquestrador executa a parte sensível.
6. Testes automatizados continuam com fakes em memória ou EF Core sobre SQLite em memória (ADR-0012); o banco local não entra na suíte nem no CI.
7. Migrations continuam sendo geradas como arquivo e revisadas; aplicá-las no banco local passa a poder ser feito pelo Claude, com aprovação por operação.

## Alternatives considered
- **Tudo, sem restrição (OPT-TUDO):** incluiria staging/produção futuros e subagentes. Rejeitada: risco de LGPD, de vazamento num repositório público e de alteração de dados sem revisão.
- **Só arquivos de exemplo (OPT-EXEMPLO):** liberaria apenas `.env.example`. Rejeitada: não atende ao pedido do usuário.

## Consequences
- `CLAUDE.md`, `AGENTS.md`, `docs/security-policy.md` e `.claude/settings.json` foram ajustados nesta data.
- Valores do `.env` local passam a poder entrar no contexto do modelo; a mitigação é a regra 4.
- A ADR-0015 (papéis do banco) continua valendo: a aplicação usa `deckloom_app`; migrations rodam com `deckloom_migrator`.
- Quando existir staging ou produção, uma nova ADR decide o acesso a eles; até lá, a resposta é não.
